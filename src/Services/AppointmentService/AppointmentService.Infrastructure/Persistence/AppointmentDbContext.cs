using AppointmentService.Application.Saga;
using AppointmentService.Domain.Entities;
using AppointmentService.Infrastructure.Persistence.Configurations;
using AppointmentService.Infrastructure.Persistence.Interceptors;
using HealthBooking.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AppointmentService.Infrastructure.Persistence;

/*
 * AppointmentDbContext
 * --------------------
 * EF Core DbContext for the AppointmentService bounded context.
 *
 * WHO USES IT:
 *   AppointmentRepository, IdempotencyRepository, OutboxProcessor, Program.cs.
 *
 * NOTABLE:
 *   BookingSagaStates DbSet persists the MassTransit saga state machine
 *   instances.  This keeps all AppointmentService data in a single SQL database,
 *   simplifying migrations and removing a separate saga DB.
 */
public sealed class AppointmentDbContext(
    DbContextOptions<AppointmentDbContext> options,
    AuditInterceptor auditInterceptor,
    OutboxPublishingInterceptor outboxInterceptor)
    : DbContext(options)
{
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<BookingIdempotencyKey> BookingIdempotencyKeys => Set<BookingIdempotencyKey>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<BookingState> BookingSagaStates => Set<BookingState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new AppointmentConfiguration());
        modelBuilder.ApplyConfiguration(new BookingIdempotencyKeyConfiguration());
        modelBuilder.ApplyConfiguration(new AppointmentOutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new BookingStateConfiguration());
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.AddInterceptors(auditInterceptor, outboxInterceptor);
    }
}
