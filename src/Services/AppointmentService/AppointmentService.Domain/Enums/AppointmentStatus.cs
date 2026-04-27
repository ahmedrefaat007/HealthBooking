namespace AppointmentService.Domain.Enums;

/*
 * AppointmentStatus
 * -----------------
 * Lifecycle states for an Appointment aggregate.
 *
 * WHO USES IT:
 *   Appointment domain methods enforce valid transition guards.
 *   AppointmentDto.Status: serialised as string for API responses.
 *   NotificationService consumers: trigger emails based on status changes.
 *
 * TRANSITIONS:
 *   Booked → Confirmed   (provider confirms the appointment)
 *   Booked | Confirmed → Cancelled  (patient or admin cancels)
 *   Confirmed → Completed (appointment is completed by provider)
 *   Confirmed → NoShow    (patient did not attend)
 */
public enum AppointmentStatus
{
    Booked = 0,  /* Initial state after booking saga completes. */
    Confirmed = 1,  /* Provider has confirmed the appointment. */
    Completed = 2,  /* Appointment successfully attended and closed. */
    Cancelled = 3,  /* Cancelled by patient or admin; slot released. */
    NoShow = 4   /* Patient did not show up for a Confirmed appointment. */
}
