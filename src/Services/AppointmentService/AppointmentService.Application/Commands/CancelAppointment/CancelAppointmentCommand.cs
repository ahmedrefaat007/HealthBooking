using AppointmentService.Application.Interfaces;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Configuration;

namespace AppointmentService.Application.Commands.CancelAppointment;

/*
 * CancelAppointmentCommand
 * ------------------------
 * MediatR command to cancel an appointment within the allowed notice window.
 *
 * WHO USES IT:
 *   AppointmentsEndpoints: DELETE /api/appointments/{id}.
 *
 * WHY THIS APPROACH:
 *   Enforcing the cancellation notice window here (at the application layer)
 *   keeps the business rule configurable via appsettings without polluting the
 *   domain entity with infrastructure dependencies.  The slot is released via
 *   gRPC immediately so it becomes available for other patients.
 */
public sealed record CancelAppointmentCommand(
    Guid AppointmentId,
    string Reason,
    string CallerUserId) : IRequest;

public sealed class CancelAppointmentCommandValidator : AbstractValidator<CancelAppointmentCommand>
{
    public CancelAppointmentCommandValidator()
    {
        RuleFor(x => x.AppointmentId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.CallerUserId).NotEmpty();
    }
}

public sealed class CancelAppointmentCommandHandler(
    IAppointmentRepository appointments,
    IProviderSlotGrpcClient slotClient,
    IConfiguration configuration)
    : IRequestHandler<CancelAppointmentCommand>
{
    public async Task Handle(CancelAppointmentCommand request, CancellationToken ct)
    {
        var appointment = await appointments.GetByIdAsync(request.AppointmentId, ct)
            ?? throw new InvalidOperationException(
                $"Appointment {request.AppointmentId} not found.");

        if (appointment.PatientId.ToString() != request.CallerUserId)
            throw new UnauthorizedAccessException(
                "You are not authorised to cancel this appointment.");

        // Enforce cancellation notice window
        var noticeHours = configuration.GetValue<int>("Appointment:CancellationNoticeHours", 2);
        var noticeDeadline = appointment.ScheduledStartUtc.AddHours(-noticeHours);
        if (DateTimeOffset.UtcNow >= noticeDeadline)
            throw new InvalidOperationException(
                $"Appointment cannot be cancelled within {noticeHours} hour(s) of the scheduled time.");

        appointment.Cancel(request.Reason);

        // Release the slot so it becomes bookable again
        await slotClient.ReleaseSlotAsync(appointment.SlotId, ct);

        await appointments.SaveChangesAsync(ct);
    }
}

