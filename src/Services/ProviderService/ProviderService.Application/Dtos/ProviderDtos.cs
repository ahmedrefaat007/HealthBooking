namespace ProviderService.Application.Dtos;

public sealed record ProviderDto(
    Guid ProviderId,
    string FirstName,
    string LastName,
    string Specialty,
    string LicenseNumber);

public sealed record SlotDto(
    Guid SlotId,
    Guid ProviderId,
    DateOnly Date,
    TimeOnly StartTime,
    TimeOnly EndTime,
    int DurationMinutes,
    string Status);
