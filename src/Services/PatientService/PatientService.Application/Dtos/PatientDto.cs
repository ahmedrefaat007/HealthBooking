namespace PatientService.Application.Dtos;

/*
 * PatientDto
 * ----------
 * Immutable read-projection of a Patient aggregate returned by query handlers
 * and served to API consumers.
 *
 * WHO USES IT:
 *   GetPatientByIdQueryHandler, GetPatientByEmailQueryHandler — return value.
 *   PatientsEndpoints — serialised as HTTP response JSON.
 *   PatientGrpcService — mapped to PatientResponse protobuf message.
 *
 * WHY THIS APPROACH:
 *   A dedicated DTO prevents leaking domain entity internals (private setters,
 *   navigation properties, value objects) across layer boundaries, and ensures
 *   the API contract is stable regardless of domain refactoring.
 */
public sealed record PatientDto(
    Guid PatientId,
    string FirstName,
    string LastName,
    string ContactEmail,
    string PhoneNumber,
    DateOnly DateOfBirth,
    DateTimeOffset RegistrationDate);
