using AppointmentService.Application.Dtos;
using AppointmentService.Application.Interfaces;
using MediatR;

namespace AppointmentService.Application.Queries.GetAppointmentById;

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
