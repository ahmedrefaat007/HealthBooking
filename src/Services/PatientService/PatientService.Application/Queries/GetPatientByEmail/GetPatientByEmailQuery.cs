using MediatR;
using PatientService.Application.Dtos;
using PatientService.Application.Interfaces;

namespace PatientService.Application.Queries.GetPatientByEmail;

public sealed record GetPatientByEmailQuery(string Email) : IRequest<PatientDto?>;

public sealed class GetPatientByEmailQueryHandler(IPatientRepository repository)
    : IRequestHandler<GetPatientByEmailQuery, PatientDto?>
{
    public async Task<PatientDto?> Handle(GetPatientByEmailQuery request, CancellationToken ct)
    {
        var patient = await repository.GetByEmailAsync(request.Email, ct);

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
