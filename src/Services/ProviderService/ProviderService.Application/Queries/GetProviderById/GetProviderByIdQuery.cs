using MediatR;
using ProviderService.Application.Dtos;
using ProviderService.Application.Interfaces;

namespace ProviderService.Application.Queries.GetProviderById;

/*
 * GetProviderByIdQuery
 * --------------------
 * MediatR read-only query returning a single ProviderDto by provider ID.
 *
 * WHO USES IT:
 *   ProvidersEndpoints: GET /api/providers/{id}.
 *
 * WHY THIS APPROACH:
 *   Returns null (instead of throwing) so the endpoint can return 404 cleanly.
 */
public sealed record GetProviderByIdQuery(Guid ProviderId) : IRequest<ProviderDto?>;

public sealed class GetProviderByIdQueryHandler(IProviderRepository repository)
    : IRequestHandler<GetProviderByIdQuery, ProviderDto?>
{
    public async Task<ProviderDto?> Handle(GetProviderByIdQuery request, CancellationToken ct)
    {
        var provider = await repository.GetByIdAsync(request.ProviderId, ct);
        if (provider is null) return null;

        return new ProviderDto(
            provider.Id,
            provider.FirstName,
            provider.LastName,
            provider.Specialty,
            provider.LicenseNumber);
    }
}
