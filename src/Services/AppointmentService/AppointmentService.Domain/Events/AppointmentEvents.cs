using HealthBooking.SharedKernel.Domain;

namespace AppointmentService.Domain.Events;

public sealed record AppointmentBookedEvent(
    Guid AppointmentId,
    Guid PatientId,
    Guid SlotId,
    DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record AppointmentCancelledEvent(
    Guid AppointmentId,
    string Reason,
    DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record AppointmentCompletedEvent(
    Guid AppointmentId,
    DateTimeOffset OccurredAt) : IDomainEvent;
