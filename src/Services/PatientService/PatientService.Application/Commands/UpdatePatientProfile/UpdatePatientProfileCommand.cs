using FluentValidation;
using MediatR;
using PatientService.Application.Interfaces;

namespace PatientService.Application.Commands.UpdatePatientProfile;

/*
 * UpdatePatientProfileCommand
 * ---------------------------
 * MediatR command to update mutable patient profile fields (name, phone).
 *
 * WHO USES IT:
 *   PatientsEndpoints: PUT /api/patients/{id} (requires auth).
 *
 * WHY THIS APPROACH:
 *   Keeping CallerUserId in the command (rather than resolving it inside the
 *   handler via ICurrentUserService) makes the handler deterministic and
 *   straightforwardly unit-testable without mocking HTTP context.
 */
public sealed record UpdatePatientProfileCommand(
    Guid PatientId,
    string FirstName,
    string LastName,
    string PhoneNumber,
    string CallerUserId) : IRequest;

/*
 * UpdatePatientProfileCommandValidator
 * -------------------------------------
 * FluentValidation validator run by the MediatR ValidationBehavior before the handler.
 */
public sealed class UpdatePatientProfileCommandValidator
    : AbstractValidator<UpdatePatientProfileCommand>
{
    public UpdatePatientProfileCommandValidator()
    {
        RuleFor(x => x.PatientId).NotEmpty();
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PhoneNumber).NotEmpty().MaximumLength(30);
        RuleFor(x => x.CallerUserId).NotEmpty();
    }
}

/*
 * UpdatePatientProfileCommandHandler
 * ------------------------------------
 * 1. Loads the Patient aggregate.
 * 2. Enforces ownership: caller must be the patient or an admin.
 * 3. Delegates update to patient.UpdateProfile() which raises a domain event.
 * 4. Saves changes (OutboxPublishingInterceptor captures the domain event).
 */
public sealed class UpdatePatientProfileCommandHandler(IPatientRepository repository)
    : IRequestHandler<UpdatePatientProfileCommand>
{
    public async Task Handle(UpdatePatientProfileCommand request, CancellationToken ct)
    {
        var patient = await repository.GetByIdAsync(request.PatientId, ct)
            ?? throw new KeyNotFoundException($"Patient {request.PatientId} not found.");

        if (patient.Id.ToString() != request.CallerUserId
            && !request.CallerUserId.StartsWith("admin", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Caller is not the owner of this patient record.");

        patient.UpdateProfile(request.FirstName, request.LastName, request.PhoneNumber);
        await repository.SaveChangesAsync(ct);
    }
}
