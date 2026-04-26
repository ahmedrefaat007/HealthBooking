using NotificationService.Domain.Entities;

namespace NotificationService.Application.Interfaces;

/*
 * ICurrentUserService
 * -------------------
 * Provides the authenticated user ID within the HTTP request scope.
 * Used by AuditInterceptor to stamp CreatedBy/ModifiedBy fields.
 */
public interface ICurrentUserService
{
    string? UserId { get; }
}

/*
 * INotificationLogRepository
 * --------------------------
 * Persistence abstraction for NotificationLog entities.
 *
 * WHO USES IT:
 *   All three notification consumers: AddAsync + SaveChangesAsync after each send.
 *   ExistsByCorrelationAndTypeAsync: idempotency guard before sending.
 */
public interface INotificationLogRepository
{
    Task<NotificationLog?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<bool> ExistsByCorrelationAndTypeAsync(
        Guid correlationId, string eventType, CancellationToken ct = default);
    Task AddAsync(NotificationLog log, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

/*
 * IEmailService
 * -------------
 * Abstraction over email delivery.
 * Production swap: replace LoggingEmailService with a SendGrid/SMTP adapter
 * without touching any consumer or domain code.
 */
public interface IEmailService
{
    Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default);
}

/*
 * IPatientEmailClient
 * -------------------
 * Fetches the patient's contact email from PatientService via gRPC.
 * Falls back gracefully (returns null) when the patient is not found or gRPC is unavailable.
 *
 * WHO USES IT:
 *   All three notification consumers to resolve the patient's real email address.
 */
public interface IPatientEmailClient
{
    Task<string?> GetPatientEmailAsync(Guid patientId, CancellationToken ct = default);
}
