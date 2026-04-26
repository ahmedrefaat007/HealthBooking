using PatientService.Domain.Entities;

namespace PatientService.Application.Interfaces;

/*
 * IPatientRepository
 * ------------------
 * Persistence abstraction for Patient aggregates.
 *
 * WHO USES IT:
 *   RegisterPatientCommandHandler, UpdatePatientProfileCommandHandler,
 *   GetPatientByIdQueryHandler, GetPatientByEmailQueryHandler.
 *
 * WHY THIS APPROACH:
 *   The repository pattern keeps application-layer handlers free from EF Core
 *   dependencies, making them independently testable with in-memory fakes.
 */
public interface IPatientRepository
{
    Task<Patient?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Patient?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task AddAsync(Patient patient, CancellationToken ct = default);
    Task<bool> ExistsByEmailAsync(string email, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

/*
 * IIdentityProvisioningService
 * ----------------------------
 * Abstraction for registering a patient as a user in IdentityServer.
 *
 * WHO USES IT:
 *   RegisterPatientCommandHandler: called after the patient record is saved.
 *
 * WHY THIS APPROACH:
 *   Decouples the domain from the HTTP call to IdentityServer.
 *   The implementation swallows failures so a transient identity error does not
 *   roll back an already-saved patient record.
 */
public interface IIdentityProvisioningService
{
    Task ProvisionUserAsync(Guid patientId, string email, CancellationToken ct = default);
}

/*
 * ICurrentUserService
 * -------------------
 * Provides the authenticated user ID within the current HTTP request scope.
 *
 * WHO USES IT:
 *   UpdatePatientProfileCommandHandler: owner-check before allowing profile edits.
 *   AuditInterceptor: stamps CreatedBy/ModifiedBy on EF entity changes.
 *
 * WHY THIS APPROACH:
 *   Abstracting IHttpContextAccessor behind this interface means handlers
 *   do not reference ASP.NET Core types, preserving application-layer portability.
 */
public interface ICurrentUserService
{
    string? UserId { get; }
}
