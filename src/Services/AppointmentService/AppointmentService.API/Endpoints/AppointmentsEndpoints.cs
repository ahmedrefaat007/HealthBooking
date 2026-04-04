using AppointmentService.Application.Commands.CancelAppointment;
using AppointmentService.Application.Interfaces;
using AppointmentService.Application.Queries.GetAppointmentById;
using AppointmentService.Application.Queries.GetPatientAppointments;
using HealthBooking.Contracts.Appointments.V1;
using MassTransit;
using MediatR;
using System.Security.Claims;

namespace AppointmentService.API.Endpoints;

public static class AppointmentsEndpoints
{
    public static IEndpointRouteBuilder MapAppointmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/appointments");

        // POST /api/appointments  — publish booking saga via request/response
        group.MapPost("/", async (
            BookAppointmentRequest                                          body,
            IRequestClient<V1_InitiateBookingCommand>                      requestClient,
            IIdempotencyRepository                                         idempotency,
            IAppointmentRepository                                         appointments,
            HttpContext                                                     http,
            CancellationToken                                              ct) =>
        {
            var patientId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(patientId))
                return Results.Unauthorized();

            var idempotencyKey = http.Request.Headers["Idempotency-Key"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(idempotencyKey))
                return Results.BadRequest("Idempotency-Key header is required.");

            // Fast idempotency check before hitting the saga
            var existingKey = await idempotency.FindAsync(idempotencyKey, ct);
            if (existingKey is not null)
            {
                var existingAppt = await appointments.GetByIdAsync(existingKey.AppointmentId, ct);
                if (existingAppt is not null)
                    return Results.Ok(new
                    {
                        appointmentId = existingAppt.Id,
                        patientId     = existingAppt.PatientId,
                        slotId        = existingAppt.SlotId,
                        patientName   = existingAppt.PatientName,
                        status        = existingAppt.Status.ToString()
                    });
            }

            var correlationId = Guid.NewGuid();
            var command = new V1_InitiateBookingCommand(
                correlationId,
                Guid.Parse(patientId),
                body.SlotId,
                idempotencyKey);

            try
            {
                var result = await requestClient.GetResponse<V1_BookingCompletedEvent, V1_BookingFailedEvent>(
                    command, ct, RequestTimeout.After(s: 30));

                if (result.Is(out Response<V1_BookingCompletedEvent>? ok))
                    return Results.Created(
                        $"/api/appointments/{ok!.Message.AppointmentId}",
                        new { appointmentId = ok.Message.AppointmentId });

                if (result.Is(out Response<V1_BookingFailedEvent>? fail))
                    return Results.Conflict(new { error = fail!.Message.Reason });

                return Results.StatusCode(500);
            }
            catch (RequestTimeoutException)
            {
                return Results.StatusCode(503); // Service unavailable / saga timed out
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
