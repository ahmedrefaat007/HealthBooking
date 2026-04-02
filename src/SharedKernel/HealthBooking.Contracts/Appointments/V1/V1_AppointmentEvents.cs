namespace HealthBooking.Contracts.Appointments.V1;

public sealed record V1_AppointmentBookedEvent(
    Guid AppointmentId,
    Guid PatientId,
    Guid ProviderId,
    Guid SlotId,
    DateTimeOffset ScheduledStartUtc,
    DateTimeOffset ScheduledEndUtc,
    Guid SagaCorrelationId,
    DateTimeOffset OccurredAt);

public sealed record V1_AppointmentCancelledEvent(
    Guid AppointmentId,
    Guid PatientId,
    Guid ProviderId,
    Guid SlotId,
    string CancellationReason,
    Guid SagaCorrelationId,
    DateTimeOffset OccurredAt);

public sealed record V1_AppointmentRescheduledEvent(
    Guid AppointmentId,
    Guid PatientId,
    Guid ProviderId,
    Guid OldSlotId,
    Guid NewSlotId,
    DateTimeOffset NewStartUtc,
    DateTimeOffset NewEndUtc,
    Guid SagaCorrelationId,
    DateTimeOffset OccurredAt);

public sealed record V1_SlotReleasedEvent(
    Guid SlotId,
    Guid ProviderId,
    Guid AppointmentId,
    DateTimeOffset OccurredAt);
