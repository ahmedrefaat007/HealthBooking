using MediatR;
using PatientService.Application.Dtos;
using PatientService.Application.Interfaces;

namespace PatientService.Application.Queries.GetPatientByEmail;

/*
 * GetPatientByEmailQuery / GetPatientByEmailQueryHandler
 * -------------------------------------------------------
 * MediatR read-only query that returns a PatientDto for a given e-mail address.
 *
 * WHO USES IT:
 *   PatientsEndpoints: GET /api/patients/me — resolves the logged-in patient
 *   from the email claim in their JWT.
 *   NotificationService consumers: may look up patients by email for routing.
 *
 * WHY THIS APPROACH:
 *   Separate query keeps email-based lookup isolated and independently testable.
 */
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
