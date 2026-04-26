namespace ProviderService.Application.Dtos;

/*
 * ProviderDto
 * -----------
 * Read-projection of a Provider aggregate for HTTP API responses.
 *
 * WHO USES IT:
 *   GetProviderByIdQueryHandler — return value.
 *   ProvidersEndpoints — serialised as JSON.
 */
public sealed record ProviderDto(
    Guid ProviderId,
    string FirstName,
    string LastName,
    string Specialty,
    string LicenseNumber);

/*
 * SlotDto
 * -------
 * Read-projection of an AvailabilitySlot for HTTP and command return values.
 *
 * WHO USES IT:
 *   DefineAvailabilityCommandHandler — returns created slots.
 *   GetProviderSlotsQueryHandler — lists available slots.
 *   ProvidersEndpoints: GET /api/providers/{id}/slots response.
 */
public sealed record SlotDto(
    Guid SlotId,
    Guid ProviderId,
    DateOnly Date,
    TimeOnly StartTime,
    TimeOnly EndTime,
    int DurationMinutes,
    string Status);
