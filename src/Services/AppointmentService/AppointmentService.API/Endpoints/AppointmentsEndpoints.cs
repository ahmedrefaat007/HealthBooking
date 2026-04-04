using AppointmentService.Application.Commands.BookAppointment;
using AppointmentService.Application.Commands.CancelAppointment;
using AppointmentService.Application.Queries.GetAppointmentById;
using AppointmentService.Application.Queries.GetPatientAppointments;
using MediatR;
using System.Security.Claims;

namespace AppointmentService.API.Endpoints;

public static class AppointmentsEndpoints
{
    public static IEndpointRouteBuilder MapAppointmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/appointments");

        // POST /api/appointments
        group.MapPost("/", async (
            BookAppointmentRequest      body,
            ISender                     sender,
            HttpContext                 http,
            CancellationToken           ct) =>
        {
            var patientId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(patientId))
                return Results.Unauthorized();

            var idempotencyKey = http.Request.Headers["Idempotency-Key"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(idempotencyKey))
                return Results.BadRequest("Idempotency-Key header is required.");

            var command = new BookAppointmentCommand(
                Guid.Parse(patientId),
                body.SlotId,
                idempotencyKey);

            try
            {
                var result = await sender.Send(command, ct);
                return Results.Created($"/api/appointments/{result.AppointmentId}", result);
            }
            catch (SlotConflictException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        })
        .WithName("BookAppointment")
        .RequireAuthorization();

        // GET /api/appointments/{id}
        group.MapGet("/{id:guid}", async (
            Guid            id,
            ISender         sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(new GetAppointmentByIdQuery(id), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("GetAppointmentById")
        .RequireAuthorization();

        // DELETE /api/appointments/{id}   (cancel)
        group.MapDelete("/{id:guid}", async (
            Guid            id,
            CancelRequest   body,
            ISender         sender,
            HttpContext      http,
            CancellationToken ct) =>
        {
            var callerId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(callerId))
                return Results.Unauthorized();

            await sender.Send(
                new CancelAppointmentCommand(id, body.Reason, callerId), ct);

            return Results.NoContent();
        })
        .WithName("CancelAppointment")
        .RequireAuthorization();

        // GET /api/appointments/patient/{patientId}
        group.MapGet("/patient/{patientId:guid}", async (
            Guid            patientId,
            ISender         sender,
            CancellationToken ct) =>
        {
            var list = await sender.Send(new GetPatientAppointmentsQuery(patientId), ct);
            return Results.Ok(list);
        })
        .WithName("GetPatientAppointments")
        .RequireAuthorization();

        return app;
    }
}

public sealed record BookAppointmentRequest(Guid SlotId);
public sealed record CancelRequest(string Reason);
