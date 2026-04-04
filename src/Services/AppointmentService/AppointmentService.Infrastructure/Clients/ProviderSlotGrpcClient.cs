using AppointmentService.Application.Interfaces;
using AppointmentService.Application.Commands.BookAppointment;
using Grpc.Core;
using HealthBooking.Contracts.Grpc;

namespace AppointmentService.Infrastructure.Clients;

public sealed class ProviderSlotGrpcClient(ProviderGrpc.ProviderGrpcClient grpcClient)
    : IProviderSlotGrpcClient
{
    public async Task<SlotInfo?> GetSlotByIdAsync(
        Guid slotId, CancellationToken ct = default)
    {
        try
        {
            var response = await grpcClient.GetSlotByIdAsync(
                new GetSlotRequest { SlotId = slotId.ToString() },
                cancellationToken: ct);

            return new SlotInfo(
                Guid.Parse(response.SlotId),
                Guid.Parse(response.ProviderId),
                DateTimeOffset.Parse(response.StartTimeUtc),
                DateTimeOffset.Parse(response.EndTimeUtc),
                response.Status);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<bool> LockSlotAsync(
        Guid slotId, Guid appointmentId, CancellationToken ct = default)
    {
        try
        {
            var response = await grpcClient.LockSlotAsync(
                new LockSlotRequest
                {
                    SlotId        = slotId.ToString(),
                    AppointmentId = appointmentId.ToString()
                },
                cancellationToken: ct);

            return response.Success;
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Aborted)
        {
            // Optimistic concurrency conflict — slot was concurrently modified
            throw new SlotConflictException(
                $"Slot {slotId} is no longer available: {ex.Status.Detail}");
        }
    }

    public async Task<bool> ReleaseSlotAsync(
        Guid slotId, CancellationToken ct = default)
    {
        var response = await grpcClient.ReleaseSlotAsync(
            new ReleaseSlotRequest { SlotId = slotId.ToString() },
            cancellationToken: ct);

        return response.Success;
    }
}
