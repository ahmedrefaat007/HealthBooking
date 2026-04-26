namespace HealthBooking.Contracts.Appointments.V1;

/*
 * V1_AppointmentBookedEvent
 * -------------------------
 * Published by AppointmentService (via the outbox) after the booking saga
 * successfully persists an Appointment entity.
 *
 * WHO USES IT:
 *   - NotificationService: sends a booking-confirmation email.
 *   - ProviderService (AppointmentBookedConsumer): transitions slot from Locked → Booked.
 *
 * WHY VERSIONED (V1_):
 *   Versioning in the type name lets consumers stay on V1 while a V2 is rolled
 *   out, enabling non-breaking schema evolution across services.
 */
public sealed record V1_AppointmentBookedEvent(
    Guid AppointmentId,
    Guid PatientId,
    Guid ProviderId,
    Guid SlotId,
    DateTimeOffset ScheduledStartUtc,
    DateTimeOffset ScheduledEndUtc,
    Guid SagaCorrelationId,
    DateTimeOffset OccurredAt);

/*
 * V1_AppointmentCancelledEvent
 * ----------------------------
 * Published when a patient or admin cancels an appointment.
 *
 * WHO USES IT:
 *   - NotificationService: sends a cancellation email.
 *   - ProviderService: can release the slot via the SlotReleasedConsumer.
 */
public sealed record V1_AppointmentCancelledEvent(
    Guid AppointmentId,
    Guid PatientId,
    Guid ProviderId,
    Guid SlotId,
    string CancellationReason,
    Guid SagaCorrelationId,
    DateTimeOffset OccurredAt);

/*
 * V1_AppointmentRescheduledEvent
 * -------------------------------
 * Published when an appointment is moved to a different slot.
 *
 * WHO USES IT:
 *   - NotificationService: sends a rescheduled notification email.
 */
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

/*
 * V1_SlotReleasedEvent
 * --------------------
 * Published when a slot needs to be returned to Available status,
 * e.g., after a cancellation or a failed booking saga.
 *
 * WHO USES IT:
 *   - ProviderService (SlotReleasedConsumer): sets slot back to Available.
 */
public sealed record V1_SlotReleasedEvent(
    Guid SlotId,
    Guid ProviderId,
    Guid AppointmentId,
    DateTimeOffset OccurredAt);
