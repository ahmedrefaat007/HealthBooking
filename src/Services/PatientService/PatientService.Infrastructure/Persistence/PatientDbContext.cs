using HealthBooking.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;
using PatientService.Domain.Entities;
using PatientService.Infrastructure.Persistence.Configurations;
using PatientService.Infrastructure.Persistence.Interceptors;

namespace PatientService.Infrastructure.Persistence;

/*
 * PatientDbContext
 * ----------------
 * EF Core DbContext that owns all PatientService database tables.
 *
 * WHO USES IT:
 *   PatientRepository, IIdentityProvisioningService, Program.cs (DI registration).
 *   AuditInterceptor and OutboxPublishingInterceptor are injected and registered
 *   via OnConfiguring so they fire automatically on every SaveChanges call.
 *
 * WHY THIS APPROACH:
 *   A dedicated DbContext per microservice enforces the bounded-context data
 *   isolation principle — PatientService never queries provider or appointment
 *   tables, and database migrations are independent per service.
 */
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
