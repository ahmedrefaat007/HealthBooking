using HealthBooking.SharedKernel.Domain;

namespace ProviderService.Domain.Events;

/*
 * ProviderRegisteredEvent
 * -----------------------
 * Domain event raised by Provider.Register() on creation.
 * Published via the outbox to RabbitMQ for audit / analytics consumers.
 */
public sealed record ProviderRegisteredEvent(
    Guid ProviderId,
    string FullName,
    string Specialty,
    DateTimeOffset OccurredAt) : IDomainEvent;

/*
 * AvailabilityDefinedEvent
 * ------------------------
 * Raised by Provider.DefineDailyAvailability() once new slots are created.
 * Consumers can react (e.g., notify patients of new availability).
 */
public sealed record AvailabilityDefinedEvent(
    Guid ProviderId,
    DateOnly Date,
    int SlotCount,
    DateTimeOffset OccurredAt) : IDomainEvent;

/*
 * SlotLockedEvent / SlotReleasedEvent
 * ------------------------------------
 * Internal domain events for slot state transitions.
 * Not currently wired to the outbox but available for future auditing.
 */
public sealed record SlotLockedEvent(
    Guid SlotId,
    Guid AppointmentId,
    DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record SlotReleasedEvent(
    Guid SlotId,
    DateTimeOffset OccurredAt) : IDomainEvent;
