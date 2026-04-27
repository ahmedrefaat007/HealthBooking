using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProviderService.Domain.Entities;
using ProviderService.Domain.Enums;

namespace ProviderService.Infrastructure.Persistence.Configurations;

/*
 * ProviderConfiguration / AvailabilitySlotConfiguration
 * -------------------------------------------------------
 * EF Core Fluent API mappings for Provider and AvailabilitySlot.
 *
 * NOTABLE CONSTRAINTS:
 *   - Unique index on LicenseNumber (one record per licensed provider).
 *   - AvailabilitySlot: RowVersion for optimistic concurrency.
 *   - CHECK constraint: DurationMinutes = 30 (fixed 30-minute slots).
 *   - Partial unique index on (ProviderId, Date, StartTime) excluding Cancelled
 *     slots to allow re-booking a cancelled time slot.
 *
 * WHO USES IT:
 *   ProviderDbContext.OnModelCreating.
 */
public sealed class ProviderConfiguration : IEntityTypeConfiguration<Provider>
{
    public void Configure(EntityTypeBuilder<Provider> builder)
    {
        builder.ToTable("Providers");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .HasDefaultValueSql("NEWSEQUENTIALID()");

        builder.Property(p => p.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(p => p.LastName).HasMaxLength(100).IsRequired();
        builder.Property(p => p.Specialty).HasMaxLength(100).IsRequired();
        builder.Property(p => p.LicenseNumber).HasMaxLength(50).IsRequired();

        builder.HasIndex(p => p.LicenseNumber)
            .IsUnique()
            .HasDatabaseName("UQ_Providers_LicenseNumber");

        // Audit columns
        builder.Property<DateTimeOffset>("CreatedAt")
            .HasDefaultValueSql("SYSDATETIMEOFFSET()");
        builder.Property<string>("CreatedBy").HasMaxLength(256);
        builder.Property<DateTimeOffset?>("ModifiedAt");
        builder.Property<string?>("ModifiedBy").HasMaxLength(256);

        // Owned slots collection
        builder.HasMany(p => p.Slots)
            .WithOne()
            .HasForeignKey(s => s.ProviderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(p => p.Slots).AutoInclude(false);
    }
}

public sealed class AvailabilitySlotConfiguration : IEntityTypeConfiguration<AvailabilitySlot>
{
    public void Configure(EntityTypeBuilder<AvailabilitySlot> builder)
    {
        builder.ToTable("AvailabilitySlots",
            t => t.HasCheckConstraint("CK_Slots_Duration", "[DurationMinutes] = 30"));

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .HasDefaultValueSql("NEWSEQUENTIALID()");

        builder.Property(s => s.ProviderId).IsRequired();
        builder.Property(s => s.DurationMinutes).IsRequired();
        builder.Property(s => s.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        // Optimistic concurrency
        builder.Property(s => s.RowVersion)
            .IsRowVersion();

        // Unique index — one slot per (provider, date, startTime) where not cancelled
        builder.HasIndex(s => new { s.ProviderId, s.Date, s.StartTime })
            .IsUnique()
            .HasFilter("[Status] <> 'Cancelled'")
            .HasDatabaseName("UQ_Slots_Provider_Date_Start");

        // Audit columns
        builder.Property<DateTimeOffset>("CreatedAt")
            .HasDefaultValueSql("SYSDATETIMEOFFSET()");
        builder.Property<string>("CreatedBy").HasMaxLength(256);
        builder.Property<DateTimeOffset?>("ModifiedAt");
        builder.Property<string?>("ModifiedBy").HasMaxLength(256);
    }
}
