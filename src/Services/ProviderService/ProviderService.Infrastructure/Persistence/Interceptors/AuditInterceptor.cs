using HealthBooking.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ProviderService.Application.Interfaces;

namespace ProviderService.Infrastructure.Persistence.Interceptors;

/*
 * AuditInterceptor (ProviderService)
 * -----------------------------------
 * EF Core SaveChangesInterceptor that stamps audit fields on every write.
 *
 * WHO USES IT:
 *   ProviderDbContext: registered via OnConfiguring.
 *
 * BEHAVIOUR:
 *   Added entities  → sets CreatedAt + CreatedBy.
 *   Modified entities → sets ModifiedAt + ModifiedBy; protects CreatedAt/CreatedBy.
 */
public sealed class AuditInterceptor(ICurrentUserService currentUser)
    : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken ct = default)
    {
        var context = eventData.Context!;
        var now = DateTimeOffset.UtcNow;
        var userId = currentUser.UserId ?? "system";

        foreach (var entry in context.ChangeTracker.Entries<AuditableEntity>())
        {
            if (entry.State == Microsoft.EntityFrameworkCore.EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.CreatedBy = userId;
            }

            if (entry.State == Microsoft.EntityFrameworkCore.EntityState.Modified)
            {
                entry.Entity.ModifiedAt = now;
                entry.Entity.ModifiedBy = userId;
                entry.Property(nameof(AuditableEntity.CreatedAt)).IsModified = false;
                entry.Property(nameof(AuditableEntity.CreatedBy)).IsModified = false;
            }
        }

        return base.SavingChangesAsync(eventData, result, ct);
    }
}
