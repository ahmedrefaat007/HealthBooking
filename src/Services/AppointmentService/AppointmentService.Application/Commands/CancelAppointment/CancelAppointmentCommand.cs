using AppointmentService.Application.Interfaces;
using FluentValidation;
using MediatR;

namespace AppointmentService.Application.Commands.CancelAppointment;

public sealed record CancelAppointmentCommand(
    Guid   AppointmentId,
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
    IAppointmentRepository  appointments,
    IProviderSlotGrpcClient slotClient)
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

        appointment.Cancel(request.Reason);

        // Release the slot so it becomes bookable again
        await slotClient.ReleaseSlotAsync(appointment.SlotId, ct);

        await appointments.SaveChangesAsync(ct);
    }
}
