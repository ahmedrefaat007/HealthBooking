using FluentValidation;
using MediatR;
using ProviderService.Application.Interfaces;
using ProviderService.Domain.Entities;

namespace ProviderService.Application.Commands.RegisterProvider;

/*
 * RegisterProviderCommand / RegisterProviderResult
 * -------------------------------------------------
 * MediatR command that creates a new healthcare provider record.
 *
 * WHO USES IT:
 *   ProvidersEndpoints: POST /api/providers/register (auth required).
 *
 * WHY THIS APPROACH:
 *   License uniqueness is enforced before delegating to the domain factory,
 *   keeping duplicate-check logic at the application layer (where DB queries
 *   are appropriate) rather than inside the domain model.
 */
public sealed record RegisterProviderCommand(
    string FirstName,
    string LastName,
    string Specialty,
    string LicenseNumber) : IRequest<RegisterProviderResult>;

public sealed record RegisterProviderResult(Guid ProviderId);

/*
 * RegisterProviderCommandValidator
 * ---------------------------------
 * FluentValidation validator run by MediatR ValidationBehavior.
 */
public sealed class RegisterProviderCommandValidator : AbstractValidator<RegisterProviderCommand>
{
    public RegisterProviderCommandValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Specialty).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LicenseNumber).NotEmpty().MaximumLength(50);
    }
}

/*
 * RegisterProviderCommandHandler
 * --------------------------------
 * 1. Checks license uniqueness.
 * 2. Calls Provider.Register() (raises ProviderRegisteredEvent).
 * 3. Persists via repository (outbox captures event in same transaction).
 */
public sealed class RegisterProviderCommandHandler(IProviderRepository repository)
    : IRequestHandler<RegisterProviderCommand, RegisterProviderResult>
{
    public async Task<RegisterProviderResult> Handle(
        RegisterProviderCommand request, CancellationToken ct)
    {
        if (await repository.ExistsByLicenseAsync(request.LicenseNumber, ct))
            throw new InvalidOperationException(
                $"A provider with license '{request.LicenseNumber}' already exists.");

        var provider = Provider.Register(
            request.FirstName,
            request.LastName,
            request.Specialty,
            request.LicenseNumber);

        await repository.AddAsync(provider, ct);
        await repository.SaveChangesAsync(ct);

        return new RegisterProviderResult(provider.Id);
    }
}
