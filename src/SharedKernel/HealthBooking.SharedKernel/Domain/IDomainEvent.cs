using MediatR;

namespace HealthBooking.SharedKernel.Domain;

/*
 * IDomainEvent
 * -----------
 * Marker interface that every domain event in the system must implement.
 *
 * WHO USES IT:
 *   All service Domain layers: AppointmentService, PatientService,
 *   ProviderService, and NotificationService derive their events from this.
 *
 * WHY THIS APPROACH:
 *   Extending MediatR's INotification allows domain events to be dispatched
 *   in-process (e.g., for unit tests or future in-process handlers) while
 *   also being captured by the OutboxPublishingInterceptor and serialised to
 *   the outbox table for durable, at-least-once delivery via MassTransit.
 */
public interface IDomainEvent : INotification
{
}
