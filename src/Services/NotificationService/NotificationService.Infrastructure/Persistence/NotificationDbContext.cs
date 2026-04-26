using Microsoft.EntityFrameworkCore;
using NotificationService.Domain.Entities;
using NotificationService.Infrastructure.Persistence.Configurations;
using NotificationService.Infrastructure.Persistence.Interceptors;

namespace NotificationService.Infrastructure.Persistence;

/*
 * NotificationDbContext
 * ---------------------
 * EF Core DbContext for the NotificationService bounded context.
 *
 * WHO USES IT:
 *   NotificationLogRepository: queries and persists NotificationLog entities.
 *   Program.cs: registered and used for migrations.
 *
 * INTERCEPTORS:
 *   AuditInterceptor stamps CreatedAt/CreatedBy/ModifiedAt/ModifiedBy
 *   on every SaveChanges call.
 */
public sealed class NotificationDbContext(
    DbContextOptions<NotificationDbContext> options,
    AuditInterceptor auditInterceptor)
    : DbContext(options)
{
    public DbSet<NotificationLog> NotificationLogs => Set<NotificationLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new NotificationLogConfiguration());
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.AddInterceptors(auditInterceptor);
    }
}
