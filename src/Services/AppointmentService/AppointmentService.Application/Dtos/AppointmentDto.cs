namespace AppointmentService.Application.Dtos;

/*
 * AppointmentDto
 * --------------
 * Immutable read-projection of an Appointment aggregate for API responses.
 *
 * WHO USES IT:
 *   GetAppointmentByIdQueryHandler, GetPatientAppointmentsQueryHandler — return type.
 *   BookAppointmentCommandHandler — confirmation response.
 *   AppointmentsEndpoints — serialised as JSON.
 *
 * WHY THIS APPROACH:
 *   Decouples API contract from the Appointment domain entity, allowing
 *   the entity to evolve (add private fields, change value objects) without
 *   breaking external consumers.
 */
public sealed record AppointmentDto(
    Guid AppointmentId,
    Guid PatientId,
    Guid SlotId,
    string PatientName,
    string Status,
    string? CancelReason);
