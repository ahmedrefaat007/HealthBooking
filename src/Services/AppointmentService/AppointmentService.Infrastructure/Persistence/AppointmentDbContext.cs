using AppointmentService.Application.Saga;
using AppointmentService.Domain.Entities;
using AppointmentService.Infrastructure.Persistence.Configurations;
using AppointmentService.Infrastructure.Persistence.Interceptors;
using HealthBooking.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AppointmentService.Infrastructure.Persistence;

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
