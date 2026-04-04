using AppointmentService.Application.Interfaces;
using AppointmentService.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AppointmentService.Infrastructure.Persistence.Repositories;

public sealed class IdempotencyRepository(AppointmentDbContext context)
    : IIdempotencyRepository
{
    public Task<BookingIdempotencyKey?> FindAsync(string key, CancellationToken ct = default) =>
        context.BookingIdempotencyKeys.FirstOrDefaultAsync(k => k.Key == key, ct);

    public async Task AddAsync(BookingIdempotencyKey key, CancellationToken ct = default) =>
        await context.BookingIdempotencyKeys.AddAsync(key, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        context.SaveChangesAsync(ct);
}
