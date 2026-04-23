using Grpc.Core;
using HealthBooking.Contracts.Grpc;
using Microsoft.EntityFrameworkCore;
using ProviderService.Application.Interfaces;
using ProviderService.Infrastructure.Persistence;

namespace ProviderService.API.Grpc;

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
