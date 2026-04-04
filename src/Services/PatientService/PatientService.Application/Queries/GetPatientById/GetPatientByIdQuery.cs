using MediatR;
using PatientService.Application.Dtos;
using PatientService.Application.Interfaces;

namespace PatientService.Application.Queries.GetPatientById;

public sealed record GetPatientByIdQuery(Guid PatientId) : IRequest<PatientDto?>;

public sealed class GetPatientByIdQueryHandler(IPatientRepository repository)
    : IRequestHandler<GetPatientByIdQuery, PatientDto?>
{
    public async Task<PatientDto?> Handle(GetPatientByIdQuery request, CancellationToken ct)
    {
        var patient = await repository.GetByIdAsync(request.PatientId, ct);

        return patient is null ? null : new PatientDto(
            patient.Id,
            patient.FirstName,
            patient.LastName,
            patient.ContactEmail,
            patient.PhoneNumber,
            patient.DateOfBirth,
            patient.RegistrationDate);
    }
}
