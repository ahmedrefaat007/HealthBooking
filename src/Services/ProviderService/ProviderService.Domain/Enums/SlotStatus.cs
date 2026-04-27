namespace ProviderService.Domain.Enums;

/*
 * SlotStatus
 * ----------
 * Lifecycle states for an AvailabilitySlot.
 *
 * WHO USES IT:
 *   AvailabilitySlot state machine (Lock/Release/Book methods).
 *   AppointmentBookedConsumer and SlotReleasedConsumer: idempotency guards.
 *   SlotRepository: filters Available slots for provider availability queries.
 *
 * TRANSITIONS:
 *   Available → Locked   (LockSlot gRPC call from AppointmentService saga)
 *   Locked   → Booked    (AppointmentBookedConsumer after appointment confirmed)
 *   Locked   → Available (Release on saga failure or cancellation)
 *   Booked   → Available (Release on appointment cancellation or reschedule)
 */
public enum SlotStatus
{
    Available = 0,  /* Slot is open for booking. */
    Locked = 1,  /* Reserved by an in-flight booking saga (not yet confirmed). */
    Booked = 2,  /* Confirmed appointment assigned. */
    Cancelled = 3   /* Permanently removed from circulation. */
}
