using NotificationService.Domain.Entities;

namespace NotificationService.Application.Interfaces;

public interface INotificationLogRepository
{
    Task<NotificationLog?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<bool> ExistsByCorrelationAndTypeAsync(
        Guid correlationId, string eventType, CancellationToken ct = default);
    Task AddAsync(NotificationLog log, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

public interface IEmailService
{
    Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default);
}

/// <summary>
/// Fetches the patient's contact e-mail from PatientService via gRPC.
/// Falls back gracefully when the patient is not found or gRPC is unavailable.
/// </summary>
public interface IPatientEmailClient
{
    Task<string?> GetPatientEmailAsync(Guid patientId, CancellationToken ct = default);
}
