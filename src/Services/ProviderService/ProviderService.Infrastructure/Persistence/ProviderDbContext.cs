using HealthBooking.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;
using ProviderService.Domain.Entities;
using ProviderService.Infrastructure.Persistence.Configurations;
using ProviderService.Infrastructure.Persistence.Interceptors;

namespace ProviderService.Infrastructure.Persistence;

/*
 * ProviderDbContext
 * -----------------
 * EF Core DbContext for the ProviderService bounded context.
 *
 * WHO USES IT:
 *   ProviderRepository, SlotRepository, ProviderGrpcService (direct DbContext
 *   access for optimistic-concurrency slot operations).
 *
 * WHY THIS APPROACH:
 *   Separate DbContext per service maintains bounded-context isolation.
 *   Both interceptors (audit stamps + outbox domain events) are registered
 *   in OnConfiguring so they automatically fire on every SaveChanges.
 */
public sealed class ProviderDbContext(
    DbContextOptions<ProviderDbContext> options,
    AuditInterceptor auditInterceptor,
    OutboxPublishingInterceptor outboxInterceptor)
    : DbContext(options)
{
    public DbSet<Provider> Providers => Set<Provider>();
    public DbSet<AvailabilitySlot> AvailabilitySlots => Set<AvailabilitySlot>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

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
