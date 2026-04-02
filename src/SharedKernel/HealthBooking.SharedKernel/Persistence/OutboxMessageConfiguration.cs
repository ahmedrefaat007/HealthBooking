using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HealthBooking.SharedKernel.Persistence;

// ReSharper disable once UnusedType.Global
// ReSharper disable once UnusedMember.Global

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
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
        builder.HasIndex(x => new { x.Status, x.CreatedAt })
               .HasDatabaseName("IX_OutboxMessages_Status_CreatedAt");
    }
}
