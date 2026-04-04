using HealthBooking.SharedKernel.Domain;
using HealthBooking.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatientService.Domain.Entities;

namespace PatientService.Infrastructure.Persistence.Configurations;

public sealed class PatientConfiguration : IEntityTypeConfiguration<Patient>
{
    public void Configure(EntityTypeBuilder<Patient> builder)
    {
        builder.ToTable("Patients");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
        builder.Property(p => p.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(p => p.LastName).HasMaxLength(100).IsRequired();
        builder.Property(p => p.DateOfBirth).HasColumnType("date").IsRequired();
        builder.Property(p => p.ContactEmail).HasMaxLength(256).IsRequired();
        builder.Property(p => p.PhoneNumber).HasMaxLength(30).IsRequired();
        builder.Property(p => p.RegistrationDate)
               .HasDefaultValueSql("SYSDATETIMEOFFSET()");

        // Audit columns
        builder.Property(p => p.CreatedAt).IsRequired();
        builder.Property(p => p.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(p => p.ModifiedAt);
        builder.Property(p => p.ModifiedBy).HasMaxLength(256);

        builder.HasIndex(p => p.ContactEmail)
               .IsUnique()
               .HasDatabaseName("UQ_Patients_Email");
    }
}
