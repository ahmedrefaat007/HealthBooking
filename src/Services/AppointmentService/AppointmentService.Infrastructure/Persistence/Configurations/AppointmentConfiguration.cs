using AppointmentService.Domain.Entities;
using AppointmentService.Domain.Enums;
using HealthBooking.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AppointmentService.Infrastructure.Persistence.Configurations;

/*
 * AppointmentConfiguration
 * ------------------------
 * EF Core Fluent API mapping for the Appointment aggregate.
 * Enforces column lengths, a CHECK constraint on Status, a unique index on SlotId
 * (one slot per appointment), and shadow audit properties.
 *
 * WHO USES IT:
 *   AppointmentDbContext.OnModelCreating.
 */
public sealed class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.ToTable("Appointments",
            t => t.HasCheckConstraint(
                "CK_Appointments_Status",
                "[Status] IN (0,1,2,3,4)"));

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .HasDefaultValueSql("NEWSEQUENTIALID()");

        builder.Property(a => a.PatientId)
            .IsRequired();

        builder.Property(a => a.SlotId)
            .IsRequired();

        builder.Property(a => a.PatientName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(a => a.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(a => a.CancelReason)
            .HasMaxLength(500);

        builder.Property(a => a.ScheduledStartUtc)
            .IsRequired();

        // Audit shadow properties
        builder.Property<DateTimeOffset>("CreatedAt")
            .HasDefaultValueSql("SYSDATETIMEOFFSET()");
        builder.Property<string>("CreatedBy")
            .HasMaxLength(100);
        builder.Property<DateTimeOffset?>("ModifiedAt");
        builder.Property<string?>("ModifiedBy")
            .HasMaxLength(100);

        builder.HasIndex(a => a.PatientId)
            .HasDatabaseName("IX_Appointments_PatientId");

        builder.HasIndex(a => a.SlotId)
            .IsUnique()
            .HasDatabaseName("UQ_Appointments_SlotId");
    }
}

public sealed class BookingIdempotencyKeyConfiguration
    : IEntityTypeConfiguration<BookingIdempotencyKey>
{
    public void Configure(EntityTypeBuilder<BookingIdempotencyKey> builder)
    {
        builder.ToTable("BookingIdempotencyKeys");
        builder.HasKey(k => k.Id);

        builder.Property(k => k.Key)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(k => k.AppointmentId)
            .IsRequired();

        builder.Property(k => k.CreatedAt);

        builder.HasIndex(k => k.Key)
            .IsUnique()
            .HasDatabaseName("UQ_IdempotencyKeys_Key");
    }
}

public sealed class AppointmentOutboxMessageConfiguration
    : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
        => new OutboxMessageConfiguration().Configure(builder);
}
