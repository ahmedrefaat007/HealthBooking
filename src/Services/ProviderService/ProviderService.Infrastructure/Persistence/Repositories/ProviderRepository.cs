using Microsoft.EntityFrameworkCore;
using ProviderService.Application.Interfaces;
using ProviderService.Domain.Entities;

namespace ProviderService.Infrastructure.Persistence.Repositories;

public sealed class ProviderRepository(ProviderDbContext db) : IProviderRepository
{
    public Task<Provider?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Providers.FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<bool> ExistsByLicenseAsync(string license, CancellationToken ct = default) =>
        db.Providers.AnyAsync(
            p => p.LicenseNumber == license.Trim(), ct);

    public async Task AddAsync(Provider provider, CancellationToken ct = default) =>
        await db.Providers.AddAsync(provider, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        db.SaveChangesAsync(ct);
}
