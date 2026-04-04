using AppointmentService.Application.Dtos;
using AppointmentService.Application.Interfaces;
using MediatR;

namespace AppointmentService.Application.Queries.GetPatientAppointments;

public sealed record GetPatientAppointmentsQuery(Guid PatientId) : IRequest<IReadOnlyList<AppointmentDto>>;

public sealed class GetPatientAppointmentsQueryHandler(IAppointmentRepository appointments)
    : IRequestHandler<GetPatientAppointmentsQuery, IReadOnlyList<AppointmentDto>>
{
    public async Task<IReadOnlyList<AppointmentDto>> Handle(
        GetPatientAppointmentsQuery request, CancellationToken ct)
    {
        var list = await appointments.GetByPatientIdAsync(request.PatientId, ct);
        return list.Select(a =>
            new AppointmentDto(a.Id, a.PatientId, a.SlotId, a.PatientName,
                               a.Status.ToString(), a.CancelReason))
                   .ToList();
    }
}
