using MediatR;
using PatientService.Application.Dtos;
using PatientService.Application.Interfaces;

namespace PatientService.Application.Queries.GetPatientById;

/*
 * GetPatientByIdQuery / GetPatientByIdQueryHandler
 * -------------------------------------------------
 * MediatR read-only query that returns a PatientDto for a given GUID.
 *
 * WHO USES IT:
 *   PatientsEndpoints: GET /api/patients/{id}.
 *   PatientGrpcService: resolves patients by ID for AppointmentService calls.
 *
 * WHY THIS APPROACH:
 *   CQRS query keeps the read path thin (no domain logic, no writes).
 *   Returns null rather than throwing so the caller can produce 404 responses.
 */
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
