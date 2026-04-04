using PatientService.Domain.Entities;

namespace PatientService.Application.Interfaces;

public interface IPatientRepository
{
    Task<Patient?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Patient?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task AddAsync(Patient patient, CancellationToken ct = default);
    Task<bool> ExistsByEmailAsync(string email, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

public interface IIdentityProvisioningService
{
    Task ProvisionUserAsync(Guid patientId, string email, CancellationToken ct = default);
}

public interface ICurrentUserService
{
    string? UserId { get; }
}
