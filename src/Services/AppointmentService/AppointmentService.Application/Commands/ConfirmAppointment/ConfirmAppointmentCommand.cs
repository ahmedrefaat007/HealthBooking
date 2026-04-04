using AppointmentService.Application.Interfaces;
using FluentValidation;
using MediatR;

namespace AppointmentService.Application.Commands.ConfirmAppointment;

public sealed record ConfirmAppointmentCommand(
    Guid   AppointmentId,
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
