using FluentValidation;
using MediatR;
using PatientService.Application.Interfaces;
using PatientService.Domain.Entities;

namespace PatientService.Application.Commands.RegisterPatient;

/*
 * RegisterPatientCommand / RegisterPatientResult
 * -----------------------------------------------
 * MediatR command that creates a new patient record and provisions an identity.
 *
 * WHO USES IT:
 *   PatientsEndpoints: POST /api/patients/register (anonymous, public endpoint).
 *
 * WHY THIS APPROACH:
 *   CQRS command encapsulates all inputs for a single write operation, enabling
 *   the MediatR pipeline (LoggingBehavior → ValidationBehavior → Handler) to
 *   validate, log, and execute the use-case without controller logic.
 */
public sealed record RegisterPatientCommand(
    string FirstName,
    string LastName,
    string Email,
    string PhoneNumber,
    DateOnly DateOfBirth) : IRequest<RegisterPatientResult>;

public sealed record RegisterPatientResult(Guid PatientId);

/*
 * RegisterPatientCommandValidator
 * --------------------------------
 * FluentValidation validator automatically discovered and run by ValidationBehavior.
 * Guards input length/format before the handler reaches the domain or database.
 */
public sealed class RegisterPatientCommandValidator : AbstractValidator<RegisterPatientCommand>
{
    public RegisterPatientCommandValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.PhoneNumber).NotEmpty().MaximumLength(30);
        RuleFor(x => x.DateOfBirth).Must(d => d < DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage("Date of birth must be in the past.");
    }
}

/*
 * RegisterPatientCommandHandler
 * ------------------------------
 * 1. Checks email uniqueness to prevent duplicate registrations.
 * 2. Delegates creation to Patient.Register() (enforces domain invariants).
 * 3. Persists the patient (AuditInterceptor + OutboxPublishingInterceptor run).
 * 4. Calls IdentityProvisioningService to create the user in IdentityServer
 *    (non-fatal — failure does not roll back the patient record).
 */
public sealed class RegisterPatientCommandHandler(
    IPatientRepository repository,
    IIdentityProvisioningService identityService)
    : IRequestHandler<RegisterPatientCommand, RegisterPatientResult>
{
    public async Task<RegisterPatientResult> Handle(
        RegisterPatientCommand request, CancellationToken ct)
    {
        if (await repository.ExistsByEmailAsync(request.Email, ct))
            throw new InvalidOperationException($"A patient with email '{request.Email}' already exists.");

        var patient = Patient.Register(
            request.FirstName,
            request.LastName,
            request.Email,
            request.PhoneNumber,
            request.DateOfBirth);

        await repository.AddAsync(patient, ct);
        await repository.SaveChangesAsync(ct);

        await identityService.ProvisionUserAsync(patient.Id, patient.ContactEmail, ct);

        return new RegisterPatientResult(patient.Id);
    }
}
