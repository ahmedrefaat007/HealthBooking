using ProviderService.Domain.Entities;
using ProviderService.Domain.Enums;

namespace ProviderService.Application.Interfaces;

/*
 * IProviderRepository
 * -------------------
 * Persistence abstraction for Provider aggregates.
 *
 * WHO USES IT:
 *   RegisterProviderCommandHandler, DefineAvailabilityCommandHandler,
 *   GetProviderByIdQueryHandler.
 */
public interface IProviderRepository
{
    Task<Provider?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<bool> ExistsByLicenseAsync(string license, CancellationToken ct = default);
    Task AddAsync(Provider provider, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

/*
 * ISlotRepository
 * ---------------
 * Persistence abstraction for AvailabilitySlot queries and mutations.
 *
 * WHO USES IT:
 *   DefineAvailabilityCommandHandler, GetProviderSlotsQueryHandler,
 *   AppointmentBookedConsumer, SlotReleasedConsumer.
 */
public interface ISlotRepository
{
    Task<AvailabilitySlot?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<AvailabilitySlot>> GetByProviderAndDateAsync(
        Guid providerId, DateOnly date, CancellationToken ct = default);
    Task<IReadOnlyList<AvailabilitySlot>> GetAvailableByProviderAsync(
        Guid providerId, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<AvailabilitySlot> slots, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

/*
 * ICacheService
 * -------------
 * Abstraction over a distributed cache (Redis in production).
 *
 * WHO USES IT:
 *   DefineAvailabilityCommandHandler: invalidates cached slot list on change.
 *   ProviderGrpcService: invalidates slot cache after lock/release.
 *   AppointmentBookedConsumer, SlotReleasedConsumer: invalidate slot cache.
 *
 * WHY THIS APPROACH:
 *   Abstracting IDistributedCache allows swapping Redis for an in-memory cache
 *   in tests without changing consumer code.
 */
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class;
    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default) where T : class;
    Task RemoveAsync(string key, CancellationToken ct = default);
}

/*
 * ICurrentUserService
 * -------------------
 * Provides the authenticated user ID within the current HTTP request scope.
 * Used by AuditInterceptor for CreatedBy/ModifiedBy stamps.
 */
public interface ICurrentUserService
{
    string UserId { get; }
}
