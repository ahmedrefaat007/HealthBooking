using AppointmentService.Application.Interfaces;
using FluentValidation;
using MediatR;

namespace AppointmentService.Application.Commands.ConfirmAppointment;

/*
 * ConfirmAppointmentCommand
 * -------------------------
 * MediatR command that transitions an appointment from Booked → Confirmed.
 *
 * WHO USES IT:
 *   AppointmentsEndpoints: POST /api/appointments/{id}/confirm.
 *   Typically called by the provider or admin after reviewing the booking.
 *
 * WHY THIS APPROACH:
 *   Confirmation is a distinct lifecycle step so providers can review bookings
 *   before confirming.  Raises AppointmentConfirmedDomainEvent for
 *   notification consumers.
 */
public sealed record ConfirmAppointmentCommand(
    Guid AppointmentId,
    string CallerUserId) : IRequest;

public sealed class ConfirmAppointmentCommandValidator
    : AbstractValidator<ConfirmAppointmentCommand>
{
    public ConfirmAppointmentCommandValidator()
    {
        RuleFor(x => x.AppointmentId).NotEmpty();
        RuleFor(x => x.CallerUserId).NotEmpty();
    }
}

public sealed class ConfirmAppointmentCommandHandler(
    IAppointmentRepository appointments)
    : IRequestHandler<ConfirmAppointmentCommand>
{
    public async Task Handle(ConfirmAppointmentCommand request, CancellationToken ct)
    {
        var appointment = await appointments.GetByIdAsync(request.AppointmentId, ct)
            ?? throw new InvalidOperationException(
                $"Appointment {request.AppointmentId} not found.");

        appointment.Confirm();

        await appointments.SaveChangesAsync(ct);
    }
}
