using Microsoft.EntityFrameworkCore;
using ProviderService.Application.Interfaces;
using ProviderService.Domain.Entities;
using ProviderService.Domain.Enums;

namespace ProviderService.Infrastructure.Persistence.Repositories;

public sealed class SlotRepository(ProviderDbContext db) : ISlotRepository
{
    public Task<AvailabilitySlot?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.AvailabilitySlots.FirstOrDefaultAsync(s => s.Id == id, ct);

    public Task<IReadOnlyList<AvailabilitySlot>> GetByProviderAndDateAsync(
        Guid providerId, DateOnly date, CancellationToken ct = default) =>
        db.AvailabilitySlots
            .Where(s => s.ProviderId == providerId && s.Date == date)
            .OrderBy(s => s.StartTime)
            .ToListAsync(ct)
            .ContinueWith<IReadOnlyList<AvailabilitySlot>>(t => t.Result, ct);

    public Task<IReadOnlyList<AvailabilitySlot>> GetAvailableByProviderAsync(
        Guid providerId, CancellationToken ct = default) =>
        db.AvailabilitySlots
            .Where(s => s.ProviderId == providerId && s.Status == SlotStatus.Available)
            .OrderBy(s => s.Date).ThenBy(s => s.StartTime)
            .ToListAsync(ct)
            .ContinueWith<IReadOnlyList<AvailabilitySlot>>(t => t.Result, ct);

    public async Task AddRangeAsync(IEnumerable<AvailabilitySlot> slots, CancellationToken ct = default) =>
        await db.AvailabilitySlots.AddRangeAsync(slots, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        db.SaveChangesAsync(ct);
}
