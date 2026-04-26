using MediatR;
using Microsoft.AspNetCore.Mvc;
using PatientService.Application.Commands.RegisterPatient;
using PatientService.Application.Commands.UpdatePatientProfile;
using PatientService.Application.Queries.GetPatientByEmail;
using PatientService.Application.Queries.GetPatientById;

namespace PatientService.API.Endpoints;

/*
 * PatientsEndpoints
 * -----------------
 * Maps all HTTP endpoints for the PatientService REST API using Minimal APIs.
 *
 * ENDPOINTS:
 *   POST   /api/patients/register   — Create a new patient (anonymous).
 *   GET    /api/patients/{id}       — Retrieve patient by ID (auth required).
 *   PUT    /api/patients/{id}       — Update profile, owner or admin only (auth).
 *   GET    /api/patients/me         — Retrieve the caller's own patient record (auth).
 *
 * WHO USES IT:
 *   Program.cs: app.MapPatientEndpoints().
 *   Angular SPA: consumes all four endpoints.
 *   ApiGateway: proxies /api/patients/** to this service.
 *
 * WHY MINIMAL APIS:
 *   Minimal API groups are lightweight, co-locate routing with handler logic,
 *   and avoid controller boilerplate, fitting the thin-API microservice style.
 */
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
