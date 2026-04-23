using HealthBooking.SharedKernel.Domain;
using ProviderService.Domain.Enums;
using ProviderService.Domain.Events;
using ProviderService.Domain.ValueObjects;

namespace ProviderService.Domain.Entities;

public sealed class AvailabilitySlot : AuditableEntity
{
    public Guid Id { get; private set; }
    public Guid ProviderId { get; private set; }
    public DateOnly Date { get; private set; }
    public TimeOnly StartTime { get; private set; }
    public TimeOnly EndTime { get; private set; }
    public int DurationMinutes { get; private set; }
    public SlotStatus Status { get; private set; }
    public Guid? AppointmentId { get; private set; }

    // Optimistic concurrency token
    public byte[] RowVersion { get; private set; } = [];

    // EF constructor
    private AvailabilitySlot() { }

    internal static AvailabilitySlot Create(
        Guid providerId, DateOnly date, TimeOnly startTime)
    {
        const int duration = 30;
        return new AvailabilitySlot
        {
            Id = Guid.NewGuid(),
            ProviderId = providerId,
            Date = date,
            StartTime = startTime,
            EndTime = startTime.AddMinutes(duration),
            DurationMinutes = duration,
            Status = SlotStatus.Available
        };
    }

    public void Lock(Guid appointmentId)
    {
        if (Status != SlotStatus.Available)
            throw new InvalidOperationException($"Slot {Id} is not available (current status: {Status}).");

        Status = SlotStatus.Locked;
        AppointmentId = appointmentId;
    }

    public void Release()
    {
        if (Status is not (SlotStatus.Locked or SlotStatus.Booked))
            throw new InvalidOperationException($"Slot {Id} cannot be released (current status: {Status}).");

        Status = SlotStatus.Available;
        AppointmentId = null;
    }

    public void Book()
    {
        if (Status != SlotStatus.Locked)
            throw new InvalidOperationException($"Slot {Id} must be locked before booking.");

        Status = SlotStatus.Booked;
    }
}
