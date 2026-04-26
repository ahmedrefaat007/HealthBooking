namespace AppointmentService.Domain.Entities;

/*
 * BookingIdempotencyKey
 * ---------------------
 * Persisted record mapping a caller-supplied idempotency key to an appointment.
 *
 * WHO USES IT:
 *   - BookAppointmentCommandHandler / PersistAppointmentActivity: creates a key
 *     atomically with the appointment so duplicate requests return the same result.
 *   - IdempotencyRepository: queries by Key string.
 *   - AppointmentsEndpoints: pre-checks the key before dispatching the saga.
 *
 * WHY THIS APPROACH:
 *   Writing the key in the same SaveChanges call as the appointment guarantees
 *   they are committed together.  On a duplicate request the endpoint returns the
 *   existing appointment without running the saga again, preventing double-bookings
 *   from network retries or accidental re-submits.
 */
public sealed class BookingIdempotencyKey
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Key { get; init; } = default!;
    public Guid AppointmentId { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
