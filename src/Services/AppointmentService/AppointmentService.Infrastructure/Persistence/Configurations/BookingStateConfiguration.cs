using AppointmentService.Application.Saga;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AppointmentService.Infrastructure.Persistence.Configurations;

/*
 * BookingStateConfiguration
 * -------------------------
 * EF Core Fluent API mapping for the MassTransit BookingState saga entity.
 * Indexes CurrentState for fast MassTransit message routing queries and
 * IdempotencyKey for duplicate-detection lookups.
 *
 * WHO USES IT:
 *   AppointmentDbContext.OnModelCreating; MassTransit saga EF Core repository.
 */
public sealed class BookingStateConfiguration : IEntityTypeConfiguration<BookingState>
{
    public void Configure(EntityTypeBuilder<BookingState> builder)
    {
        builder.ToTable("BookingSagaStates");

        builder.HasKey(x => x.CorrelationId);

        builder.Property(x => x.CurrentState)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.IdempotencyKey)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.PatientName)
            .HasMaxLength(200);

        builder.Property(x => x.FailureReason)
            .HasMaxLength(500);

        builder.HasIndex(x => x.CurrentState)
            .HasDatabaseName("IX_BookingSagaStates_CurrentState");

        builder.HasIndex(x => x.IdempotencyKey)
            .HasDatabaseName("IX_BookingSagaStates_IdempotencyKey");
    }
}
