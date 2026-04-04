namespace PatientService.Application.Dtos;

public sealed record PatientDto(
    Guid PatientId,
    string FirstName,
    string LastName,
    string ContactEmail,
    string PhoneNumber,
    DateOnly DateOfBirth,
    DateTimeOffset RegistrationDate);
