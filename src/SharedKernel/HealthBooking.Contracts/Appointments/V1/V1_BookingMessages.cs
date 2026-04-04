namespace HealthBooking.Contracts.Appointments.V1;

/// <summary>Sent by the HTTP endpoint to initiate the booking saga.</summary>
public sealed record V1_InitiateBookingCommand(
    Guid   CorrelationId,
    Guid   PatientId,
    Guid   SlotId,
    string IdempotencyKey);

/// <summary>Published by the saga on successful completion.</summary>
public sealed record V1_BookingCompletedEvent(
    Guid           CorrelationId,
    Guid           AppointmentId,
    DateTimeOffset CompletedAt);

/// <summary>Published by the saga when any step fails (with compensation applied).</summary>
public sealed record V1_BookingFailedEvent(
    Guid           CorrelationId,
    string         Reason,
    DateTimeOffset FailedAt);
