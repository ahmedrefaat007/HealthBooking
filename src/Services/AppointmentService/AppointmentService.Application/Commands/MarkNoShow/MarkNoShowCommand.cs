using AppointmentService.Application.Interfaces;
using FluentValidation;
using MediatR;

namespace AppointmentService.Application.Commands.MarkNoShow;

public sealed record MarkNoShowCommand(
    Guid   AppointmentId,
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
