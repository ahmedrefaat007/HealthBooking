using Microsoft.EntityFrameworkCore;
using PatientService.Application.Interfaces;
using PatientService.Domain.Entities;
using PatientService.Infrastructure.Persistence;

namespace PatientService.Infrastructure.Persistence.Repositories;

public sealed class PatientRepository(PatientDbContext db) : IPatientRepository
{
    public async Task<Patient?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await db.Patients.FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<Patient?> GetByEmailAsync(string email, CancellationToken ct = default)
        => await db.Patients.FirstOrDefaultAsync(
               p => p.ContactEmail == email.Trim().ToLowerInvariant(), ct);

    public async Task AddAsync(Patient patient, CancellationToken ct = default)
        => await db.Patients.AddAsync(patient, ct);

    public async Task<bool> ExistsByEmailAsync(string email, CancellationToken ct = default)
        => await db.Patients.AnyAsync(
               p => p.ContactEmail == email.Trim().ToLowerInvariant(), ct);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await db.SaveChangesAsync(ct);
}
