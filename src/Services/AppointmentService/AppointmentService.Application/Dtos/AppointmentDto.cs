namespace AppointmentService.Application.Dtos;

public sealed record AppointmentDto(
    Guid AppointmentId,
    Guid PatientId,
    Guid SlotId,
    string PatientName,
    string Status,
    string? CancelReason);
