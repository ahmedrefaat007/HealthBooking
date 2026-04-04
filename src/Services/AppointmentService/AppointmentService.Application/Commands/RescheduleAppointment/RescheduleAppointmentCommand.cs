using AppointmentService.Application.Commands.BookAppointment;
using AppointmentService.Application.Interfaces;
using FluentValidation;
using MediatR;

namespace AppointmentService.Application.Commands.RescheduleAppointment;

public sealed record RescheduleAppointmentCommand(
    Guid   AppointmentId,
    Guid   NewSlotId,
    string CallerUserId) : IRequest;

public sealed class RescheduleAppointmentCommandValidator
    : AbstractValidator<RescheduleAppointmentCommand>
{
    public RescheduleAppointmentCommandValidator()
    {
        RuleFor(x => x.AppointmentId).NotEmpty();
        RuleFor(x => x.NewSlotId).NotEmpty();
        RuleFor(x => x.CallerUserId).NotEmpty();
    }
}

public sealed class RescheduleAppointmentCommandHandler(
    IAppointmentRepository  appointments,
    IProviderSlotGrpcClient slotClient)
    : IRequestHandler<RescheduleAppointmentCommand>
{
    public async Task Handle(RescheduleAppointmentCommand request, CancellationToken ct)
    {
        var appointment = await appointments.GetByIdAsync(request.AppointmentId, ct)
            ?? throw new InvalidOperationException(
                $"Appointment {request.AppointmentId} not found.");

        if (appointment.PatientId.ToString() != request.CallerUserId)
            throw new UnauthorizedAccessException(
                "You are not authorised to reschedule this appointment.");

        // Step 1 — Verify the new slot is available and fetch its start time
        var newSlot = await slotClient.GetSlotByIdAsync(request.NewSlotId, ct)
            ?? throw new InvalidOperationException(
                $"Slot {request.NewSlotId} not found.");

        if (newSlot.Status != "Available")
            throw new SlotConflictException(
                $"Slot {request.NewSlotId} is not available.");

        // Step 2 — Lock the new slot
        var locked = await slotClient.LockSlotAsync(request.NewSlotId, appointment.Id, ct);
        if (!locked)
            throw new SlotConflictException(
                $"Slot {request.NewSlotId} could not be locked (concurrent booking).");

        var oldSlotId = appointment.SlotId;

        try
        {
            // Step 3 — Apply domain method (raises AppointmentRescheduledDomainEvent)
            appointment.Reschedule(request.NewSlotId, newSlot.StartTimeUtc);

            // Step 4 — Persist (OutboxPublishingInterceptor captures domain event)
            await appointments.SaveChangesAsync(ct);
        }
        catch
        {
            // Compensate: release the new slot if persist fails
            await slotClient.ReleaseSlotAsync(request.NewSlotId, ct);
            throw;
        }

        // Step 5 — Release the old slot (best-effort; event will also trigger ProviderService consumer)
        await slotClient.ReleaseSlotAsync(oldSlotId, ct);
    }
}
