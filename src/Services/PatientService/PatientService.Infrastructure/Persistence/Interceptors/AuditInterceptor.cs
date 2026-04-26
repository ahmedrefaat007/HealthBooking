using HealthBooking.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PatientService.Application.Interfaces;

namespace PatientService.Infrastructure.Persistence.Interceptors;

/*
 * AuditInterceptor
 * ----------------
 * EF Core SaveChangesInterceptor that automatically stamps CreatedAt/CreatedBy
 * on new entities and ModifiedAt/ModifiedBy on updated entities.
 *
 * WHO USES IT:
 *   PatientDbContext registers it via OnConfiguring so it fires on every
 *   SaveChangesAsync call automatically.
 *
 * WHY THIS APPROACH:
 *   Cross-cutting concern handled at the infrastructure layer keeps domain
 *   entities clean.  IsModified = false guards prevent accidental overwriting
 *   of CreatedAt/CreatedBy on updates, which would corrupt the audit trail.
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
