using Grpc.Core;
using HealthBooking.Contracts.Grpc;
using Microsoft.EntityFrameworkCore;
using ProviderService.Application.Interfaces;
using ProviderService.Infrastructure.Persistence;

namespace ProviderService.API.Grpc;

/*
 * ProviderGrpcService
 * -------------------
 * gRPC service implementation for synchronous slot operations called by
 * AppointmentService during the booking saga.
 *
 * WHO USES IT:
 *   AppointmentService LockSlotActivity: calls LockSlot() to reserve a slot.
 *   AppointmentService (cancel/reschedule commands): calls ReleaseSlot().
 *   AppointmentService VerifyPatientActivity: calls GetSlotById() to get start time.
 *
 * WHY THIS APPROACH:
 *   gRPC provides strongly-typed, low-latency synchronous calls for critical
 *   booking operations where HTTP REST overhead is undesirable.
 *   Optimistic concurrency via RowVersion on AvailabilitySlot catches concurrent
 *   lock attempts and returns StatusCode.Aborted so the caller (saga) can retry
 *   or compensate without silent data corruption.
 *
 * CONCURRENCY NOTE:
 *   LockSlot catches DbUpdateConcurrencyException and maps it to gRPC Aborted,
 *   which ProviderSlotGrpcClient translates to a SlotConflictException for the saga.
 */
public sealed class ProviderGrpcService(ProviderDbContext db, ICacheService cache)
    : ProviderGrpc.ProviderGrpcBase
{
    private static string SlotCacheKey(Guid providerId) => $"slots:{providerId}";
    public override async Task<SlotResponse> GetSlotById(
        GetSlotRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.SlotId, out var id))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid slot id"));

        var slot = await db.AvailabilitySlots.FindAsync([id], context.CancellationToken);
        if (slot is null)
            throw new RpcException(new Status(StatusCode.NotFound, "Slot not found"));

        return new SlotResponse
        {
            SlotId = slot.Id.ToString(),
            ProviderId = slot.ProviderId.ToString(),
            StartTimeUtc = slot.Date.ToDateTime(slot.StartTime, DateTimeKind.Utc).ToString("O"),
            EndTimeUtc = slot.Date.ToDateTime(slot.EndTime, DateTimeKind.Utc).ToString("O"),
            Status = slot.Status.ToString()
        };
    }

    public override async Task<LockSlotResponse> LockSlot(
        LockSlotRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.SlotId, out var slotId) ||
            !Guid.TryParse(request.AppointmentId, out var appointmentId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid id format"));

        var slot = await db.AvailabilitySlots.FindAsync([slotId], context.CancellationToken);
        if (slot is null)
            throw new RpcException(new Status(StatusCode.NotFound, "Slot not found"));

        try
        {
            slot.Lock(appointmentId);
            await db.SaveChangesAsync(context.CancellationToken);
            // Invalidate the slot list cache for this provider
            await cache.RemoveAsync(SlotCacheKey(slot.ProviderId), context.CancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, ex.Message));
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new RpcException(new Status(StatusCode.Aborted, "Slot was modified concurrently. Retry."));
        }

        return new LockSlotResponse { Success = true };
    }

    public override async Task<ReleaseSlotResponse> ReleaseSlot(
        ReleaseSlotRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.SlotId, out var slotId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid slot id"));

        var slot = await db.AvailabilitySlots.FindAsync([slotId], context.CancellationToken);
        if (slot is null)
            throw new RpcException(new Status(StatusCode.NotFound, "Slot not found"));

        try
        {
            slot.Release();
            await db.SaveChangesAsync(context.CancellationToken);
            // Invalidate the slot list cache so released slot becomes available again
            await cache.RemoveAsync(SlotCacheKey(slot.ProviderId), context.CancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, ex.Message));
        }

        return new ReleaseSlotResponse { Success = true };
    }
}
