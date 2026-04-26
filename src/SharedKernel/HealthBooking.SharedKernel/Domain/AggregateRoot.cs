namespace HealthBooking.SharedKernel.Domain;

/*
 * AggregateRoot
 * -------------
 * Base class for all DDD aggregate roots.  Inherits AuditableEntity and adds
 * a private in-memory collection of domain events raised during a unit of work.
 *
 * WHO USES IT:
 *   AppointmentService.Domain.Entities.Appointment,
 *   PatientService.Domain.Entities.Patient,
 *   ProviderService.Domain.Entities.Provider.
 *
 * WHY THIS APPROACH:
 *   Aggregates raise events via AddDomainEvent() as side-effects of domain
 *   operations (Book, Cancel, Register, etc.).  The OutboxPublishingInterceptor
 *   drains those events during SaveChangesAsync, serialises them to the Outbox
 *   table, and then calls ClearDomainEvents() – ensuring transactional
 *   consistency between the state change and the event record.
 */
public abstract class AggregateRoot : AuditableEntity
{
    /* Backing store for events raised during the current unit of work. */
    private readonly List<IDomainEvent> _domainEvents = [];

    /* Exposes the collected events as an immutable read-only view. */
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /* Called by domain methods (e.g., Appointment.Cancel) to record an event. */
    protected void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    /*
     * Called by OutboxPublishingInterceptor after the events have been written
     * to the outbox table so they are not double-published on subsequent saves.
     */
    public void ClearDomainEvents() => _domainEvents.Clear();
}
