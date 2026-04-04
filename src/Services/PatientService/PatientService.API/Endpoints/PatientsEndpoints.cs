using MediatR;
using Microsoft.AspNetCore.Mvc;
using PatientService.Application.Commands.RegisterPatient;
using PatientService.Application.Commands.UpdatePatientProfile;
using PatientService.Application.Queries.GetPatientByEmail;
using PatientService.Application.Queries.GetPatientById;

namespace PatientService.API.Endpoints;

public static class PatientsEndpoints
{
    public static IEndpointRouteBuilder MapPatientEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/patients");

        // POST /api/patients/register — anonymous
        group.MapPost("/register", async (
            RegisterPatientCommand command,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(command, ct);
            return Results.Created($"/api/patients/{result.PatientId}", new { result.PatientId });
        })
        .WithName("RegisterPatient")
        .AllowAnonymous();

        // GET /api/patients/{id}
        group.MapGet("/{id:guid}", async (
            Guid id,
            ISender sender,
            CancellationToken ct) =>
        {
            var patient = await sender.Send(new GetPatientByIdQuery(id), ct);
            return patient is null ? Results.NotFound() : Results.Ok(patient);
        })
        .WithName("GetPatientById")
        .RequireAuthorization();

        // PUT /api/patients/{id}
        group.MapPut("/{id:guid}", async (
            Guid id,
            [FromBody] UpdatePatientProfileRequest body,
            HttpContext ctx,
            ISender sender,
            CancellationToken ct) =>
        {
            var callerUserId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
            var command = new UpdatePatientProfileCommand(
                id, body.FirstName, body.LastName, body.PhoneNumber, callerUserId);
            await sender.Send(command, ct);
            return Results.NoContent();
        })
        .WithName("UpdatePatient")
        .RequireAuthorization();

        // GET /api/patients/me
        group.MapGet("/me", async (
            HttpContext ctx,
            ISender sender,
            CancellationToken ct) =>
        {
            var email = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                        ?? ctx.User.FindFirst("email")?.Value;

            if (string.IsNullOrEmpty(email))
                return Results.Unauthorized();

            var patient = await sender.Send(new GetPatientByEmailQuery(email), ct);
            return patient is null ? Results.NotFound() : Results.Ok(patient);
        })
        .WithName("GetMe")
        .RequireAuthorization();

        return app;
    }
}

public sealed record UpdatePatientProfileRequest(
    string FirstName, string LastName, string PhoneNumber);
