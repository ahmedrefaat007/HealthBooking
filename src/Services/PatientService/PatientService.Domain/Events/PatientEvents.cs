namespace PatientService.Domain.Events;

/*
 * PatientRegisteredEvent
 * ----------------------
 * Domain event raised by Patient.Register() when a new patient is created.
 *
 * WHO USES IT:
 *   - OutboxPublishingInterceptor: serialises it to the OutboxMessage table
 *     inside the same DB transaction as the patient INSERT.
 *   - OutboxProcessor: deserialises and publishes to RabbitMQ via MassTransit.
 *   - (Future) notification or analytics consumers.
 *
 * WHY THIS APPROACH:
 *   Domain events decouple registration from side-effects (identity provisioning,
 *   email confirmation).  The outbox guarantees at-least-once delivery even if
 *   the message broker is temporarily unavailable.
 */
public sealed record PatientRegisteredEvent(
    Guid PatientId,
    string FirstName,
    string LastName,
    string ContactEmail,
    DateTimeOffset OccurredAt) : HealthBooking.SharedKernel.Domain.IDomainEvent;

/*
 * PatientProfileUpdatedEvent
 * --------------------------
 * Domain event raised by Patient.UpdateProfile() when profile fields change.
 *
 * WHO USES IT:
 *   Same outbox pipeline as PatientRegisteredEvent.
 *   Audit-log consumers that record profile change history.
 *
 * WHY THIS APPROACH:
 *   Consumers can react without polling the database, providing an event-sourced
 *   audit trail of all profile changes over time.
 */
public sealed record PatientProfileUpdatedEvent(
    Guid PatientId,
    string FirstName,
    string LastName,
    string PhoneNumber,
    DateTimeOffset OccurredAt) : HealthBooking.SharedKernel.Domain.IDomainEvent;
