using HealthBooking.Contracts.Appointments.V1;
using MassTransit;
using Microsoft.Extensions.Logging;
using ProviderService.Application.Interfaces;

namespace ProviderService.Infrastructure.Messaging.Consumers;

/*
 * AppointmentBookedConsumer
 * -------------------------
 * MassTransit consumer that transitions a slot from Locked → Booked when an
 * appointment is confirmed and the V1_AppointmentBookedEvent is received.
 *
 * WHO USES IT:
 *   MassTransit RabbitMQ subscriber configured in Program.cs.
 *   Published by AppointmentService OutboxProcessor after an appointment is booked.
 *
 * WHY THIS APPROACH:
 *   Idempotent check on AppointmentId prevents double-booking if the event is
 *   redelivered (at-least-once delivery guarantee of the outbox/MassTransit).
 *   Redis cache invalidation ensures subsequent slot queries reflect the new status.
 */
public sealed class AppointmentBookedConsumer(
    ISlotRepository slots,
    ICacheService cache,
    ILogger<AppointmentBookedConsumer> logger)
    : IConsumer<V1_AppointmentBookedEvent>
{
    public async Task Consume(ConsumeContext<V1_AppointmentBookedEvent> context)
    {
        var msg = context.Message;

        var slot = await slots.GetByIdAsync(msg.SlotId, context.CancellationToken);
        if (slot is null)
        {
            logger.LogWarning(
                "AppointmentBookedConsumer: slot {SlotId} not found.", msg.SlotId);
            return;
        }

        // Idempotency: already booked for this appointment
        if (slot.AppointmentId == msg.AppointmentId)
        {
            logger.LogInformation(
                "AppointmentBookedConsumer: slot {SlotId} already booked for appointment {AppointmentId}.",
                msg.SlotId, msg.AppointmentId);
            return;
        }

        slot.Book();

        await slots.SaveChangesAsync(context.CancellationToken);

        // Invalidate Redis cache so next read reflects Booked status
        await cache.RemoveAsync($"slot:{msg.SlotId}", context.CancellationToken);

        logger.LogInformation(
            "Slot {SlotId} transitioned to Booked for appointment {AppointmentId}.",
            msg.SlotId, msg.AppointmentId);
    }
}
