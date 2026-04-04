using MediatR;
using ProviderService.Application.Commands.DefineAvailability;
using ProviderService.Application.Commands.RegisterProvider;
using ProviderService.Application.Queries.GetProviderById;
using ProviderService.Application.Queries.GetProviderSlots;

namespace ProviderService.API.Endpoints;

public static class ProvidersEndpoints
{
    public static IEndpointRouteBuilder MapProviderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/providers");

        // POST /api/providers/register
        group.MapPost("/register", async (
            RegisterProviderCommand command,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(command, ct);
            return Results.Created($"/api/providers/{result.ProviderId}", new { result.ProviderId });
        })
        .WithName("RegisterProvider")
        .RequireAuthorization();

        // GET /api/providers/{id}
        group.MapGet("/{id:guid}", async (
            Guid id,
            ISender sender,
            CancellationToken ct) =>
        {
            var provider = await sender.Send(new GetProviderByIdQuery(id), ct);
            return provider is null ? Results.NotFound() : Results.Ok(provider);
        })
        .WithName("GetProviderById")
        .RequireAuthorization();

        // GET /api/providers/{id}/slots
        group.MapGet("/{id:guid}/slots", async (
            Guid id,
            ISender sender,
            CancellationToken ct) =>
        {
            var slots = await sender.Send(new GetProviderSlotsQuery(id), ct);
            return Results.Ok(slots);
        })
        .WithName("GetProviderSlots")
        .RequireAuthorization();

        // POST /api/providers/{id}/availability
        group.MapPost("/{id:guid}/availability", async (
            Guid id,
            DefineAvailabilityRequest body,
            ISender sender,
            CancellationToken ct) =>
        {
            var command = new DefineAvailabilityCommand(
                id, body.Date, body.StartTime, body.EndTime);
            var slots = await sender.Send(command, ct);
            return Results.Ok(slots);
        })
        .WithName("DefineAvailability")
        .RequireAuthorization();

        return app;
    }
}

public sealed record DefineAvailabilityRequest(
    DateOnly Date, TimeOnly StartTime, TimeOnly EndTime);
