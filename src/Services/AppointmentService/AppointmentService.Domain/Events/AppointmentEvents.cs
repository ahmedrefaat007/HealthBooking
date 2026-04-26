using HealthBooking.SharedKernel.Domain;

namespace AppointmentService.Domain.Events;

/*
 * AppointmentEvents
 * -----------------
 * Domain event records raised by the Appointment aggregate on each state transition.
 * All events implement IDomainEvent and are captured by OutboxPublishingInterceptor
 * during SaveChanges, then published to RabbitMQ by OutboxProcessor.
 *
 * WHO USES IT:
 *   - NotificationService consumers (Booked, Cancelled, Rescheduled): send emails.
 *   - ProviderService consumers (Booked, SlotReleased via Cancelled/Rescheduled):
 *     update slot status.
 *   - Audit/analytics services: build event-sourced history.
 */
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
