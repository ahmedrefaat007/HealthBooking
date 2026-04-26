namespace HealthBooking.Contracts.Appointments.V1;

/*
 * V1_InitiateBookingCommand
 * -------------------------
 * Sent by the AppointmentService HTTP endpoint to the MassTransit saga bus
 * to start the three-step booking workflow.
 *
 * WHO USES IT:
 *   - AppointmentsEndpoints: publishes this via IRequestClient<V1_InitiateBookingCommand>.
 *   - BookingStateMachine: the initial trigger event.
 *
 * WHY REQUEST/RESPONSE:
 *   Using MassTransit's request/response pattern gives the HTTP caller a
 *   synchronous-feeling 201 Created or Conflict response while the saga runs
 *   asynchronously inside the message bus (backed by RabbitMQ).
 */
/// <summary>Sent by the HTTP endpoint to initiate the booking saga.</summary>
public sealed record V1_InitiateBookingCommand(
    Guid CorrelationId,
    Guid PatientId,
    Guid SlotId,
    string IdempotencyKey);

/*
 * V1_BookingCompletedEvent
 * -------------------------
 * Published by the saga's Completed state as the response to the HTTP caller.
 *
 * WHO USES IT:
 *   AppointmentsEndpoints: extracts AppointmentId and returns 201 Created.
 */
/// <summary>Published by the saga on successful completion.</summary>
public sealed record V1_BookingCompletedEvent(
    Guid CorrelationId,
    Guid AppointmentId,
    DateTimeOffset CompletedAt);

/*
 * V1_BookingFailedEvent
 * ----------------------
 * Published by the saga's Failed state when any activity throws an exception
 * and compensation has been applied.
 *
 * WHO USES IT:
 *   AppointmentsEndpoints: extracts Reason and returns 409 Conflict.
 */
/// <summary>Published by the saga when any step fails (with compensation applied).</summary>
public sealed record V1_BookingFailedEvent(
    Guid CorrelationId,
    string Reason,
    DateTimeOffset FailedAt);
