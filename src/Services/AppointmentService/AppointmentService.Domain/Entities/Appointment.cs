using AppointmentService.Domain.Enums;
using AppointmentService.Domain.Events;
using HealthBooking.SharedKernel.Domain;

namespace AppointmentService.Domain.Entities;

public sealed class Appointment : AggregateRoot
{
    public Guid              Id            { get; private set; }
    public Guid              PatientId     { get; private set; }
    public Guid              SlotId        { get; private set; }
    public string            PatientName   { get; private set; } = default!;
    public AppointmentStatus Status        { get; private set; }
    public string?           CancelReason  { get; private set; }

    private Appointment() { }

    public static Appointment Book(Guid patientId, Guid slotId, string patientName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(patientName);

        var appointment = new Appointment
        {
            Id          = Guid.NewGuid(),
            PatientId   = patientId,
            SlotId      = slotId,
            PatientName = patientName.Trim(),
            Status      = AppointmentStatus.Booked
        };

        appointment.AddDomainEvent(new AppointmentBookedEvent(
            appointment.Id, patientId, slotId, DateTimeOffset.UtcNow));

        return appointment;
    }

    public void Cancel(string reason)
    {
        if (Status is AppointmentStatus.Completed or AppointmentStatus.Cancelled)
            throw new InvalidOperationException(
                $"Appointment {Id} cannot be cancelled (status: {Status}).");

        Status       = AppointmentStatus.Cancelled;
        CancelReason = reason;

        AddDomainEvent(new AppointmentCancelledEvent(Id, reason, DateTimeOffset.UtcNow));
    }

    public void Complete()
    {
        if (Status != AppointmentStatus.Confirmed && Status != AppointmentStatus.Booked)
            throw new InvalidOperationException(
                $"Appointment {Id} cannot be completed (status: {Status}).");

        Status = AppointmentStatus.Completed;
        AddDomainEvent(new AppointmentCompletedEvent(Id, DateTimeOffset.UtcNow));
    }
}
