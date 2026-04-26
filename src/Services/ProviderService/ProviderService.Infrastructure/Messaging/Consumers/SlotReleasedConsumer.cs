using HealthBooking.Contracts.Appointments.V1;
using MassTransit;
using Microsoft.Extensions.Logging;
using ProviderService.Application.Interfaces;
using ProviderService.Domain.Enums;

namespace ProviderService.Infrastructure.Messaging.Consumers;

/*
 * SlotReleasedConsumer
 * --------------------
 * MassTransit consumer that transitions a slot back to Available when a
 * V1_SlotReleasedEvent is published (appointment cancelled or rescheduled).
 *
 * WHO USES IT:
 *   MassTransit RabbitMQ subscriber configured in Program.cs.
 *   Published by AppointmentService when CancelAppointment or RescheduleAppointment
 *   commands complete and the old slot must be freed.
 *
 * WHY THIS APPROACH:
 *   Idempotent guard (slot.Status == Available) ensures repeated delivery of the
 *   same release event does not cause an error or double-release.
 *   Redis invalidation keeps the provider's slot-list cache fresh.
 */
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
