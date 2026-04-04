using MassTransit;

namespace AppointmentService.Application.Saga;

/// <summary>
/// Saga state persisted to SQL Server via MassTransit EF Core repository.
/// Tracks one booking attempt from initiation through to completion or failure.
/// </summary>
public sealed class BookingState : SagaStateMachineInstance
{
    public Guid   CorrelationId { get; set; }
    public string CurrentState  { get; set; } = null!;

    // Booking inputs
    public Guid   PatientId      { get; set; }
    public Guid   SlotId         { get; set; }
    public string IdempotencyKey { get; set; } = null!;

    // Set by activities
    public Guid?   AppointmentId { get; set; }
    public string? PatientName   { get; set; }

    // Compensation flag: true once the slot lock succeeds
    public bool    SlotWasLocked { get; set; }

    // Outcome
    public string? FailureReason { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
