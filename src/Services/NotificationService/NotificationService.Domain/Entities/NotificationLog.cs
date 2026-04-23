using HealthBooking.SharedKernel.Domain;

namespace NotificationService.Domain.Entities;

public sealed class NotificationLog : AuditableEntity
{
    public Guid Id { get; private set; }
    public Guid CorrelationId { get; private set; }   // AppointmentId or event id
    public string EventType { get; private set; } = default!;
    public string RecipientEmail { get; private set; } = default!;
    public string Subject { get; private set; } = default!;
    public string Body { get; private set; } = default!;
    public NotificationStatus Status { get; private set; }
    public string? FailureReason { get; private set; }
    public int RetryCount { get; private set; }
    public DateTimeOffset SentAt { get; private set; }

    private NotificationLog() { }

    public static NotificationLog Create(
        Guid correlationId, string eventType, string recipientEmail,
        string subject, string body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipientEmail);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);

        return new NotificationLog
        {
            Id = Guid.NewGuid(),
            CorrelationId = correlationId,
            EventType = eventType,
            RecipientEmail = recipientEmail.Trim().ToLowerInvariant(),
            Subject = subject.Trim(),
            Body = body.Trim(),
            Status = NotificationStatus.Pending,
            SentAt = DateTimeOffset.UtcNow
        };
    }

    public void MarkSent()
    {
        Status = NotificationStatus.Sent;
    }

    public void MarkFailed(string reason)
    {
        Status = NotificationStatus.Failed;
        FailureReason = reason;
        RetryCount++;
    }
}

public enum NotificationStatus
{
    Pending = 0,
    Sent = 1,
    Failed = 2
}
