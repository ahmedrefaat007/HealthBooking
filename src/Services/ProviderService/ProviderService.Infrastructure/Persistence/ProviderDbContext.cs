using HealthBooking.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;
using ProviderService.Domain.Entities;
using ProviderService.Infrastructure.Persistence.Configurations;
using ProviderService.Infrastructure.Persistence.Interceptors;

namespace ProviderService.Infrastructure.Persistence;

public sealed class ProviderDbContext(
    DbContextOptions<ProviderDbContext> options,
    AuditInterceptor auditInterceptor,
    OutboxPublishingInterceptor outboxInterceptor)
    : DbContext(options)
{
    public DbSet<Provider>          Providers        => Set<Provider>();
    public DbSet<AvailabilitySlot>  AvailabilitySlots => Set<AvailabilitySlot>();
    public DbSet<OutboxMessage>     OutboxMessages   => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ProviderConfiguration());
        modelBuilder.ApplyConfiguration(new AvailabilitySlotConfiguration());
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.AddInterceptors(auditInterceptor, outboxInterceptor);
    }
}
