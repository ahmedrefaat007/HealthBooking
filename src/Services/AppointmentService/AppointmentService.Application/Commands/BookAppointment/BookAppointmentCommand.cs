using AppointmentService.Application.Dtos;
using AppointmentService.Application.Interfaces;
using AppointmentService.Domain.Entities;
using FluentValidation;
using MediatR;

namespace AppointmentService.Application.Commands.BookAppointment;

public sealed record BookAppointmentCommand(
    Guid   PatientId,
    Guid   SlotId,
    string IdempotencyKey) : IRequest<AppointmentDto>;

public sealed class BookAppointmentCommandValidator : AbstractValidator<BookAppointmentCommand>
{
    public BookAppointmentCommandValidator()
    {
        RuleFor(x => x.PatientId).NotEmpty();
        RuleFor(x => x.SlotId).NotEmpty();
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(200);
    }
}

public sealed class BookAppointmentCommandHandler(
    IAppointmentRepository     appointments,
    IIdempotencyRepository     idempotency,
    IPatientGrpcClient         patientClient,
    IProviderSlotGrpcClient    slotClient)
    : IRequestHandler<BookAppointmentCommand, AppointmentDto>
{
    public async Task<AppointmentDto> Handle(
        BookAppointmentCommand request, CancellationToken ct)
    {
        // Step 1 — Idempotency: return existing result for duplicate keys
        var existing = await idempotency.FindAsync(request.IdempotencyKey, ct);
        if (existing is not null)
        {
            var existingAppt = await appointments.GetByIdAsync(existing.AppointmentId, ct)
                ?? throw new InvalidOperationException(
                    $"Appointment {existing.AppointmentId} referenced by idempotency key not found.");
            return ToDto(existingAppt);
        }

        // Step 2 — Verify patient exists via gRPC
        var patient = await patientClient.GetPatientByIdAsync(request.PatientId, ct)
            ?? throw new InvalidOperationException(
                $"Patient {request.PatientId} not found.");

        // Step 3 — Lock the slot via ProviderService gRPC
        var appointmentId = Guid.NewGuid();
        var locked = await slotClient.LockSlotAsync(request.SlotId, appointmentId, ct);
        if (!locked)
            throw new SlotConflictException(
                $"Slot {request.SlotId} is no longer available.");

        // Step 4 — Persist appointment + idempotency key atomically
        var appointment = Appointment.Book(
            request.PatientId,
            request.SlotId,
            patient.FullName);

        // Align the appointment ID with the one used for locking
        await appointments.AddAsync(appointment, ct);

        var key = new BookingIdempotencyKey
        {
            Key           = request.IdempotencyKey,
            AppointmentId = appointment.Id
        };
        await idempotency.AddAsync(key, ct);
        await appointments.SaveChangesAsync(ct);

        return ToDto(appointment);
    }

    private static AppointmentDto ToDto(Appointment a) =>
        new(a.Id, a.PatientId, a.SlotId, a.PatientName, a.Status.ToString(), a.CancelReason);
}

public sealed class SlotConflictException(string message) : Exception(message);
