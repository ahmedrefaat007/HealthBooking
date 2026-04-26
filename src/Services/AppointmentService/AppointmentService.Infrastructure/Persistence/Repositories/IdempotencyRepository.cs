using AppointmentService.Application.Interfaces;
using AppointmentService.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AppointmentService.Infrastructure.Persistence.Repositories;

/*
 * IdempotencyRepository
 * ---------------------
 * EF Core implementation of IIdempotencyRepository.
 *
 * WHO USES IT:
 *   PersistAppointmentActivity (saga), BookAppointmentCommandHandler,
 *   AppointmentsEndpoints pre-check.
 *
 * WHY THIS APPROACH:
 *   Thin single-purpose repository; FindAsync uses exact string match on Key
 *   which has a unique index in the database, making duplicate detection O(1).
 */
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
