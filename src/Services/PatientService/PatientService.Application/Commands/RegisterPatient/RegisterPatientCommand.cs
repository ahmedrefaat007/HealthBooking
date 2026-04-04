using FluentValidation;
using MediatR;
using PatientService.Application.Interfaces;
using PatientService.Domain.Entities;

namespace PatientService.Application.Commands.RegisterPatient;

public sealed record RegisterPatientCommand(
    string FirstName,
    string LastName,
    string Email,
    string PhoneNumber,
    DateOnly DateOfBirth) : IRequest<RegisterPatientResult>;

public sealed record RegisterPatientResult(Guid PatientId);

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
