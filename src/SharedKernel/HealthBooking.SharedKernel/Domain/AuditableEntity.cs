namespace HealthBooking.SharedKernel.Domain;

/*
 * AuditableEntity
 * ---------------
 * Base class that automatically stamps audit columns on every persisted entity.
 *
 * WHO USES IT:
 *   Every aggregate root and non-aggregate entity that requires audit tracking:
 *   Appointment, Patient, Provider, AvailabilitySlot, NotificationLog.
 *
 * WHY THIS APPROACH:
 *   Centralising CreatedAt / CreatedBy / ModifiedAt / ModifiedBy in a base
 *   class ensures consistency across all services and removes boilerplate.
 *   The AuditInterceptor (EF Core SaveChangesInterceptor) populates these
 *   fields automatically, so application code never needs to set them manually.
 */
public abstract class AuditableEntity
{
    /* Timestamp and user identity recorded when the row was first inserted. */
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = default!;

    /* Populated on every subsequent UPDATE; null until the first update occurs. */
    public DateTimeOffset? ModifiedAt { get; set; }
    public string? ModifiedBy { get; set; }
}
