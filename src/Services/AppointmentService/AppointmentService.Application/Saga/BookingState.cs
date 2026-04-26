using MassTransit;

namespace AppointmentService.Application.Saga;

/*
 * BookingState
 * ------------
 * Saga state entity persisted to SQL Server via the MassTransit EF Core saga repository.
 * One row per booking attempt; tracks inputs, activity results, and compensation flags.
 *
 * WHO USES IT:
 *   BookingStateMachine: reads/writes state properties via saga instance.
 *   MassTransit: serialises/deserialises state between saga bus invocations.
 *   AppointmentDbContext: maps to the BookingSagaStates table.
 *
 * WHY THIS APPROACH:
 *   Persistent saga state survives process restarts and scales horizontally.
 *   SlotWasLocked is a compensation flag so LockSlotActivity.Faulted knows
 *   whether to call ReleaseSlot on failure without querying ProviderService.
 */
public sealed class BookingState : SagaStateMachineInstance
{
    public Guid CorrelationId { get; set; }
    public string CurrentState { get; set; } = null!;

    // Booking inputs
    public Guid PatientId { get; set; }
    public Guid SlotId { get; set; }
    public string IdempotencyKey { get; set; } = null!;

    // Set by activities
    public Guid? AppointmentId { get; set; }
    public string? PatientName { get; set; }

    // Compensation flag: true once the slot lock succeeds
    public bool SlotWasLocked { get; set; }

    // Outcome
    public string? FailureReason { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
