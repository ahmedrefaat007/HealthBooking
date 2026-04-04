using HealthBooking.SharedKernel.Domain;

namespace AppointmentService.Domain.Events;

public sealed record AppointmentBookedEvent(
    Guid AppointmentId,
    Guid PatientId,
    Guid SlotId,
    DateTimeOffset ScheduledStartUtc,
    DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record AppointmentCancelledEvent(
    Guid AppointmentId,
    string Reason,
    DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record AppointmentCompletedEvent(
    Guid AppointmentId,
    DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record AppointmentRescheduledDomainEvent(
    Guid AppointmentId,
    Guid PatientId,
    Guid OldSlotId,
    Guid NewSlotId,
    DateTimeOffset NewScheduledStartUtc,
    DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record AppointmentConfirmedDomainEvent(
    Guid AppointmentId,
    Guid PatientId,
    Guid SlotId,
    DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record AppointmentNoShowDomainEvent(
    Guid AppointmentId,
    Guid PatientId,
    Guid SlotId,
    DateTimeOffset OccurredAt) : IDomainEvent;
