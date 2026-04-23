using HealthBooking.Contracts.Appointments.V1;
using MassTransit;
using Microsoft.Extensions.Logging;
using ProviderService.Application.Interfaces;

namespace ProviderService.Infrastructure.Messaging.Consumers;

/// <summary>
/// Subscribes to V1_AppointmentBookedEvent and transitions the slot from Locked → Booked.
/// Idempotent: if AppointmentId already matches slot.AppointmentId the slot is already Booked.
/// </summary>
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
