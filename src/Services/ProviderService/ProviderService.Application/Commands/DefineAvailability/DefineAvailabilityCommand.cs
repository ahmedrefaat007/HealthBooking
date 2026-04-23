using FluentValidation;
using MediatR;
using ProviderService.Application.Dtos;
using ProviderService.Application.Interfaces;

namespace ProviderService.Application.Commands.DefineAvailability;

public sealed record DefineAvailabilityCommand(
    Guid ProviderId,
    DateOnly Date,
    TimeOnly StartTime,
    TimeOnly EndTime) : IRequest<IReadOnlyList<SlotDto>>;

public sealed class DefineAvailabilityCommandValidator : AbstractValidator<DefineAvailabilityCommand>
{
    public DefineAvailabilityCommandValidator()
    {
        RuleFor(x => x.ProviderId).NotEmpty();
        RuleFor(x => x.Date)
            .Must(d => d >= DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage("Availability date must be today or in the future.");
        RuleFor(x => x.EndTime)
            .GreaterThan(x => x.StartTime)
            .WithMessage("End time must be after start time.");
    }
}

public sealed class DefineAvailabilityCommandHandler(
    IProviderRepository providers,
    ISlotRepository slots,
    ICacheService cache)
    : IRequestHandler<DefineAvailabilityCommand, IReadOnlyList<SlotDto>>
{
    public async Task<IReadOnlyList<SlotDto>> Handle(
        DefineAvailabilityCommand request, CancellationToken ct)
    {
        var provider = await providers.GetByIdAsync(request.ProviderId, ct)
            ?? throw new InvalidOperationException($"Provider {request.ProviderId} not found.");

        var created = provider.DefineDailyAvailability(
            request.Date, request.StartTime, request.EndTime);

        await slots.AddRangeAsync(created, ct);
        await providers.SaveChangesAsync(ct);

        // Invalidate cached slot list for this provider
        await cache.RemoveAsync($"slots:{request.ProviderId}", ct);

        return created.Select(s => new SlotDto(
            s.Id, s.ProviderId, s.Date, s.StartTime, s.EndTime,
            s.DurationMinutes, s.Status.ToString())).ToList();
    }
}
