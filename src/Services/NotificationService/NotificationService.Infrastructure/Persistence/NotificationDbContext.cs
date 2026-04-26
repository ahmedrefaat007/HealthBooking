using Microsoft.EntityFrameworkCore;
using NotificationService.Domain.Entities;
using NotificationService.Infrastructure.Persistence.Configurations;
using NotificationService.Infrastructure.Persistence.Interceptors;

namespace NotificationService.Infrastructure.Persistence;

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
