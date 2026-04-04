using HealthBooking.Contracts.Appointments.V1;
using MassTransit;
using Microsoft.Extensions.Logging;
using NotificationService.Application.Interfaces;
using NotificationService.Domain.Entities;

namespace NotificationService.Application.Consumers;

/// <summary>
/// Subscribes to V1_AppointmentRescheduledEvent and sends a reschedule
/// notification email to the patient.
/// Idempotent: skips if a log entry already exists for (AppointmentId, EventType).
/// </summary>
public sealed class AppointmentRescheduledConsumer(
    INotificationLogRepository                   repository,
    IEmailService                                 emailService,
    IPatientEmailClient                           patientEmailClient,
    ILogger<AppointmentRescheduledConsumer>       logger)
    : IConsumer<V1_AppointmentRescheduledEvent>
{
    public async Task Consume(ConsumeContext<V1_AppointmentRescheduledEvent> context)
    {
        var msg = context.Message;
        const string eventType = nameof(V1_AppointmentRescheduledEvent);

        // Idempotency guard
        if (await repository.ExistsByCorrelationAndTypeAsync(msg.AppointmentId, eventType, context.CancellationToken))
        {
            logger.LogInformation(
                "Skipping duplicate reschedule notification for AppointmentId={AppointmentId}.", msg.AppointmentId);
            return;
        }

        var patientEmail = await patientEmailClient.GetPatientEmailAsync(msg.PatientId, context.CancellationToken)
                           ?? $"patient-{msg.PatientId}@placeholder.local";

        var subject = "Your appointment has been rescheduled";
        var body    = BuildRescheduleEmail(msg);

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
            logger.LogError(ex,
                "Failed to send reschedule notification for AppointmentId={AppointmentId}.", msg.AppointmentId);
            log.MarkFailed(ex.Message);
        }

        await repository.AddAsync(log, context.CancellationToken);
        await repository.SaveChangesAsync(context.CancellationToken);
    }

    private static string BuildRescheduleEmail(V1_AppointmentRescheduledEvent msg) =>
        $"""
        <h2>Appointment Rescheduled</h2>
        <p>Your appointment (ID: <strong>{msg.AppointmentId}</strong>) has been moved.</p>
        <p>New scheduled time: <strong>{msg.NewStartUtc:f} UTC</strong></p>
        <p>Thank you for using HealthBooking.</p>
        """;
}
