using HealthBooking.Contracts.Appointments.V1;
using MassTransit;
using Microsoft.Extensions.Logging;
using NotificationService.Application.Interfaces;
using NotificationService.Domain.Entities;

namespace NotificationService.Application.Consumers;

/// <summary>
/// Subscribes to V1_AppointmentCancelledEvent and sends a cancellation email.
/// </summary>
public sealed class AppointmentCancelledConsumer(
    INotificationLogRepository repository,
    IEmailService               emailService,
    ILogger<AppointmentCancelledConsumer> logger)
    : IConsumer<V1_AppointmentCancelledEvent>
{
    public async Task Consume(ConsumeContext<V1_AppointmentCancelledEvent> context)
    {
        var msg = context.Message;
        const string eventType = nameof(V1_AppointmentCancelledEvent);

        if (await repository.ExistsByCorrelationAndTypeAsync(msg.AppointmentId, eventType, context.CancellationToken))
        {
            logger.LogInformation(
                "Skipping duplicate cancellation notification for AppointmentId={AppointmentId}.", msg.AppointmentId);
            return;
        }

        var subject = "Your appointment has been cancelled";
        var body    = BuildCancellationEmail(msg);

        var log = NotificationLog.Create(
            correlationId  : msg.AppointmentId,
            eventType      : eventType,
            recipientEmail : $"patient-{msg.PatientId}@placeholder.local",
            subject        : subject,
            body           : body);

        try
        {
            await emailService.SendAsync(log.RecipientEmail, subject, body, context.CancellationToken);
            log.MarkSent();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send cancellation email for AppointmentId={AppointmentId}.", msg.AppointmentId);
            log.MarkFailed(ex.Message);
        }

        await repository.AddAsync(log, context.CancellationToken);
        await repository.SaveChangesAsync(context.CancellationToken);
    }

    private static string BuildCancellationEmail(V1_AppointmentCancelledEvent msg) =>
        $"""
        <h2>Appointment Cancelled</h2>
        <p>Your appointment (ID: <strong>{msg.AppointmentId}</strong>) has been cancelled.</p>
        <p>Reason: <em>{msg.CancellationReason}</em></p>
        <p>Please contact support if you have any questions.</p>
        """;
}
