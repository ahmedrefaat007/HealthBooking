using MediatR;
using ProviderService.Application.Dtos;
using ProviderService.Application.Interfaces;

namespace ProviderService.Application.Queries.GetProviderSlots;

/*
 * GetProviderSlotsQuery
 * ---------------------
 * MediatR read-only query returning all available slots for a provider.
 *
 * WHO USES IT:
 *   ProvidersEndpoints: GET /api/providers/{id}/slots.
 *   AppointmentsEndpoints (indirectly): patients browse slots before booking.
 *
 * CACHING:
 *   Results are cached in Redis for 60 seconds (TTL) under the key "slots:{providerId}".
 *   Cache is invalidated by AppointmentBookedConsumer and SlotReleasedConsumer
 *   when slot status changes.
 */
public sealed record GetProviderSlotsQuery(Guid ProviderId) : IRequest<IReadOnlyList<SlotDto>>;

public sealed class GetProviderSlotsQueryHandler(
    ISlotRepository slots,
    ICacheService cache)
    : IRequestHandler<GetProviderSlotsQuery, IReadOnlyList<SlotDto>>
{
    private static string CacheKey(Guid id) => $"slots:{id}";
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

    public async Task<IReadOnlyList<SlotDto>> Handle(
        GetProviderSlotsQuery request, CancellationToken ct)
    {
        var key = CacheKey(request.ProviderId);

        var cached = await cache.GetAsync<List<SlotDto>>(key, ct);
        if (cached is not null) return cached;

        var available = await slots.GetAvailableByProviderAsync(request.ProviderId, ct);
        var dtos = available.Select(s => new SlotDto(
            s.Id, s.ProviderId, s.Date, s.StartTime, s.EndTime,
            s.DurationMinutes, s.Status.ToString())).ToList();

        await cache.SetAsync(key, dtos, Ttl, ct);
        return dtos;
    }
}
