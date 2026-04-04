using FluentValidation;
using MediatR;
using ProviderService.Application.Interfaces;
using ProviderService.Domain.Entities;

namespace ProviderService.Application.Commands.RegisterProvider;

public sealed record RegisterProviderCommand(
    string FirstName,
    string LastName,
    string Specialty,
    string LicenseNumber) : IRequest<RegisterProviderResult>;

public sealed record RegisterProviderResult(Guid ProviderId);

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
