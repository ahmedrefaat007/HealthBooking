using HealthBooking.Contracts.Appointments.V1;
using MassTransit;
using Microsoft.Extensions.Logging;
using NotificationService.Application.Interfaces;
using NotificationService.Domain.Entities;

namespace NotificationService.Application.Consumers;

/// <summary>
/// Subscribes to V1_AppointmentBookedEvent from RabbitMQ and sends a
/// booking confirmation email to the patient.  Idempotent: if a log
/// entry already exists for (AppointmentId, EventType) we skip sending.
/// </summary>
public sealed class AppointmentBookedConsumer(
    INotificationLogRepository repository,
    IEmailService               emailService,
    IPatientEmailClient         patientEmailClient,
    ILogger<AppointmentBookedConsumer> logger)
    : IConsumer<V1_AppointmentBookedEvent>
{
    public async Task Consume(ConsumeContext<V1_AppointmentBookedEvent> context)
    {
        var msg = context.Message;
        const string eventType = nameof(V1_AppointmentBookedEvent);

        // Idempotency guard — don't send twice for the same appointment event
        if (await repository.ExistsByCorrelationAndTypeAsync(msg.AppointmentId, eventType, context.CancellationToken))
        {
            logger.LogInformation(
                "Skipping duplicate notification for AppointmentId={AppointmentId}.", msg.AppointmentId);
            return;
        }

        // Resolve real patient e-mail via gRPC; fall back to placeholder if unavailable
        var patientEmail = await patientEmailClient.GetPatientEmailAsync(msg.PatientId, context.CancellationToken)
                           ?? $"patient-{msg.PatientId}@placeholder.local";

        var subject = "Your appointment has been confirmed";
        var body    = BuildBookingEmail(msg);

        var log = NotificationLog.Create(
            correlationId  : msg.AppointmentId,
            eventType      : eventType,
            recipientEmail : patientEmail,
            subject        : subject,
            body           : body);

        try
        {
            await emailService.SendAsync(log.RecipientEmail, subject, body, context.CancellationToken);
            log.MarkSent();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send booking confirmation for AppointmentId={AppointmentId}.", msg.AppointmentId);
            log.MarkFailed(ex.Message);
        }

        await repository.AddAsync(log, context.CancellationToken);
        await repository.SaveChangesAsync(context.CancellationToken);
    }

    private static string BuildBookingEmail(V1_AppointmentBookedEvent msg) =>
        $"""
        <h2>Appointment Confirmed</h2>
        <p>Your appointment (ID: <strong>{msg.AppointmentId}</strong>) has been booked.</p>
        <p>Scheduled: <strong>{msg.ScheduledStartUtc:f} UTC</strong></p>
        <p>Thank you for choosing HealthBooking.</p>
        """;
}
