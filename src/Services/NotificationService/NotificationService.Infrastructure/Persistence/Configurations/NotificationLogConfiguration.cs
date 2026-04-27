using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotificationService.Domain.Entities;

namespace NotificationService.Infrastructure.Persistence.Configurations;

/*
 * NotificationLogConfiguration
 * ----------------------------
 * EF Core Fluent API mapping for the NotificationLog entity.
 * Enforces the unique composite index on (CorrelationId, EventType) that
 * backs the idempotency guard in ExistsByCorrelationAndTypeAsync.
 *
 * WHO USES IT:
 *   NotificationDbContext.OnModelCreating.
 */
public sealed class NotificationLogConfiguration : IEntityTypeConfiguration<NotificationLog>
{
    public void Configure(EntityTypeBuilder<NotificationLog> builder)
    {
        builder.ToTable("NotificationLogs");
        builder.HasKey(n => n.Id);

        builder.Property(n => n.Id)
            .HasDefaultValueSql("NEWSEQUENTIALID()");

        builder.Property(n => n.CorrelationId).IsRequired();
        builder.Property(n => n.EventType).HasMaxLength(256).IsRequired();
        builder.Property(n => n.RecipientEmail).HasMaxLength(256).IsRequired();
        builder.Property(n => n.Subject).HasMaxLength(500).IsRequired();
        builder.Property(n => n.Body).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(n => n.Status).HasConversion<int>().IsRequired();
        builder.Property(n => n.FailureReason).HasMaxLength(1000);
        builder.Property(n => n.RetryCount).HasDefaultValue(0);

        // Audit shadow properties
        builder.Property<DateTimeOffset>("CreatedAt")
            .HasDefaultValueSql("SYSDATETIMEOFFSET()");
        builder.Property<string>("CreatedBy").HasMaxLength(100);
        builder.Property<DateTimeOffset?>("ModifiedAt");
        builder.Property<string?>("ModifiedBy").HasMaxLength(100);

        // Idempotency: only one notification per (CorrelationId, EventType)
        builder.HasIndex(n => new { n.CorrelationId, n.EventType })
            .IsUnique()
            .HasDatabaseName("UQ_NotificationLogs_Correlation_EventType");

        builder.HasIndex(n => n.RecipientEmail)
            .HasDatabaseName("IX_NotificationLogs_RecipientEmail");
    }
}
