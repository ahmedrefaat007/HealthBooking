using ProviderService.Domain.Entities;
using ProviderService.Domain.Enums;

namespace ProviderService.Application.Interfaces;

public interface IProviderRepository
{
    Task<Provider?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<bool> ExistsByLicenseAsync(string license, CancellationToken ct = default);
    Task AddAsync(Provider provider, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

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

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class;
    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default) where T : class;
    Task RemoveAsync(string key, CancellationToken ct = default);
}

public interface ICurrentUserService
{
    string UserId { get; }
}
