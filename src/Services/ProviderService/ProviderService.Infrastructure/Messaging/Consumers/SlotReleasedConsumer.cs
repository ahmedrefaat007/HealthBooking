using HealthBooking.Contracts.Appointments.V1;
using MassTransit;
using Microsoft.Extensions.Logging;
using ProviderService.Application.Interfaces;
using ProviderService.Domain.Enums;

namespace ProviderService.Infrastructure.Messaging.Consumers;

/// <summary>
/// Subscribes to V1_SlotReleasedEvent and transitions the slot back to Available.
/// Published when an appointment is cancelled or rescheduled.
/// Idempotent: if slot is already Available, the no-op guard skips the operation.
/// </summary>
public sealed class SlotReleasedConsumer(
    ISlotRepository slots,
    ICacheService cache,
    ILogger<SlotReleasedConsumer> logger)
    : IConsumer<V1_SlotReleasedEvent>
{
    public async Task Consume(ConsumeContext<V1_SlotReleasedEvent> context)
    {
        var msg = context.Message;

        var slot = await slots.GetByIdAsync(msg.SlotId, context.CancellationToken);
        if (slot is null)
        {
            logger.LogWarning(
                "SlotReleasedConsumer: slot {SlotId} not found.", msg.SlotId);
            return;
        }

        // Idempotency: already available
        if (slot.Status == SlotStatus.Available)
        {
            logger.LogInformation(
                "SlotReleasedConsumer: slot {SlotId} is already released.", msg.SlotId);
            return;
        }

        slot.Release();

        await slots.SaveChangesAsync(context.CancellationToken);

        // Invalidate Redis cache
        await cache.RemoveAsync($"slot:{msg.SlotId}", context.CancellationToken);

        logger.LogInformation(
            "Slot {SlotId} released back to Available (appointment {AppointmentId}).",
            msg.SlotId, msg.AppointmentId);
    }
}
