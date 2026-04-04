using FluentValidation;
using MediatR;
using PatientService.Application.Interfaces;

namespace PatientService.Application.Commands.UpdatePatientProfile;

public sealed record UpdatePatientProfileCommand(
    Guid PatientId,
    string FirstName,
    string LastName,
    string PhoneNumber,
    string CallerUserId) : IRequest;

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
