using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HealthBooking.SharedKernel.Persistence;

// ReSharper disable once UnusedType.Global
// ReSharper disable once UnusedMember.Global

/*
 * OutboxMessageConfiguration
 * --------------------------
 * EF Core Fluent-API mapping for the OutboxMessage entity.
 *
 * WHO USES IT:
 *   Applied inside each service's DbContext.OnModelCreating:
 *   AppointmentDbContext, PatientDbContext, ProviderDbContext.
 *
 * WHY THIS APPROACH:
 *   A shared configuration class ensures identical column definitions across
 *   all three databases that maintain an outbox table.  The composite index on
 *   (Status, CreatedAt) is critical for OutboxProcessor performance because it
 *   queries WHERE Status = 'Pending' ORDER BY CreatedAt TAKE 20.
 */
public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    /*
     * Maps OutboxMessage to the OutboxMessages table with explicit column
     * lengths, defaults, and a covering index for the polling query.
     */
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EventType).HasMaxLength(256).IsRequired();
        builder.Property(x => x.SchemaVersion).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Payload).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.DestinationExchange).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired().HasDefaultValue("Pending");
        builder.Property(x => x.RetryCount).HasDefaultValue(0);

        /*
         * Covering index for the OutboxProcessor SELECT query:
         * WHERE Status = 'Pending' ORDER BY CreatedAt.
         * Avoids a full table scan on high-volume services.
         */
        builder.HasIndex(x => new { x.Status, x.CreatedAt })
               .HasDatabaseName("IX_OutboxMessages_Status_CreatedAt");
    }
}
