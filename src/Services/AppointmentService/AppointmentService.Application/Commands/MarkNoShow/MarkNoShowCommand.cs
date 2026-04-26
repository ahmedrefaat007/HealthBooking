using AppointmentService.Application.Interfaces;
using FluentValidation;
using MediatR;

namespace AppointmentService.Application.Commands.MarkNoShow;

/*
 * MarkNoShowCommand
 * -----------------
 * MediatR command that transitions a Confirmed appointment to NoShow.
 *
 * WHO USES IT:
 *   AppointmentsEndpoints: POST /api/appointments/{id}/no-show.
 *   Called by the provider after the appointment time passes with no patient attendance.
 *
 * WHY THIS APPROACH:
 *   NoShow is a distinct final state (separate from Cancelled) so it can be
 *   tracked for analytics and potential patient penalty policies.
 */
public sealed record MarkNoShowCommand(
    Guid AppointmentId,
    string CallerUserId) : IRequest;

public sealed class MarkNoShowCommandValidator
    : AbstractValidator<MarkNoShowCommand>
{
    public MarkNoShowCommandValidator()
    {
        RuleFor(x => x.AppointmentId).NotEmpty();
        RuleFor(x => x.CallerUserId).NotEmpty();
    }
}

public sealed class MarkNoShowCommandHandler(
    IAppointmentRepository appointments)
    : IRequestHandler<MarkNoShowCommand>
{
    public async Task Handle(MarkNoShowCommand request, CancellationToken ct)
    {
        var appointment = await appointments.GetByIdAsync(request.AppointmentId, ct)
            ?? throw new InvalidOperationException(
                $"Appointment {request.AppointmentId} not found.");

        appointment.MarkNoShow();

        await appointments.SaveChangesAsync(ct);
    }
}
