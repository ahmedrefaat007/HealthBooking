using AppointmentService.Application.Dtos;
using AppointmentService.Application.Interfaces;
using MediatR;

namespace AppointmentService.Application.Queries.GetAppointmentById;

/*
 * GetAppointmentByIdQuery
 * -----------------------
 * MediatR read-only query returning a single AppointmentDto by ID.
 *
 * WHO USES IT:
 *   AppointmentsEndpoints: GET /api/appointments/{id}.
 *
 * WHY THIS APPROACH:
 *   Returns null rather than throwing so the endpoint can produce a 404 response.
 */
public sealed record GetAppointmentByIdQuery(Guid AppointmentId) : IRequest<AppointmentDto?>;

public sealed class GetAppointmentByIdQueryHandler(IAppointmentRepository appointments)
    : IRequestHandler<GetAppointmentByIdQuery, AppointmentDto?>
{
    public async Task<AppointmentDto?> Handle(
        GetAppointmentByIdQuery request, CancellationToken ct)
    {
        var a = await appointments.GetByIdAsync(request.AppointmentId, ct);
        return a is null ? null
            : new AppointmentDto(a.Id, a.PatientId, a.SlotId, a.PatientName,
                                 a.Status.ToString(), a.CancelReason);
    }
}
