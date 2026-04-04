using HealthBooking.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;
using PatientService.Domain.Entities;
using PatientService.Infrastructure.Persistence.Configurations;
using PatientService.Infrastructure.Persistence.Interceptors;

namespace PatientService.Infrastructure.Persistence;

public sealed class PatientDbContext(
    DbContextOptions<PatientDbContext> options,
    AuditInterceptor auditInterceptor,
    OutboxPublishingInterceptor outboxInterceptor)
    : DbContext(options)
{
    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new PatientConfiguration());
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.AddInterceptors(auditInterceptor, outboxInterceptor);
    }
}
