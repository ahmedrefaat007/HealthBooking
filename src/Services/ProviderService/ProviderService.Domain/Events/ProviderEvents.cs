using HealthBooking.SharedKernel.Domain;

namespace ProviderService.Domain.Events;

public sealed record ProviderRegisteredEvent(
    Guid ProviderId,
    string FullName,
    string Specialty,
    DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record AvailabilityDefinedEvent(
    Guid ProviderId,
    DateOnly Date,
    int SlotCount,
    DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record SlotLockedEvent(
    Guid SlotId,
    Guid AppointmentId,
    DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record SlotReleasedEvent(
    Guid SlotId,
    DateTimeOffset OccurredAt) : IDomainEvent;
