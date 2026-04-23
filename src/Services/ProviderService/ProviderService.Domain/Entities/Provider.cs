using HealthBooking.SharedKernel.Domain;
using ProviderService.Domain.Enums;
using ProviderService.Domain.Events;
using ProviderService.Domain.ValueObjects;

namespace ProviderService.Domain.Entities;

public sealed class Provider : AggregateRoot
{
    public Guid Id { get; private set; }
    public string FirstName { get; private set; } = default!;
    public string LastName { get; private set; } = default!;
    public string Specialty { get; private set; } = default!;
    public string LicenseNumber { get; private set; } = default!;

    private readonly List<AvailabilitySlot> _slots = [];
    public IReadOnlyList<AvailabilitySlot> Slots => _slots.AsReadOnly();

    private Provider() { }

    public static Provider Register(
        string firstName, string lastName, string specialty, string licenseNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(firstName);
        ArgumentException.ThrowIfNullOrWhiteSpace(lastName);
        _ = new SpecialtyName(specialty); // validates
        ArgumentException.ThrowIfNullOrWhiteSpace(licenseNumber);

        var provider = new Provider
        {
            Id = Guid.NewGuid(),
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            Specialty = specialty.Trim(),
            LicenseNumber = licenseNumber.Trim()
        };

        provider.AddDomainEvent(new ProviderRegisteredEvent(
            provider.Id,
            $"{provider.FirstName} {provider.LastName}",
            provider.Specialty,
            DateTimeOffset.UtcNow));

        return provider;
    }

    /// <summary>
    /// Generates 30-minute slots from startTime inclusive up to endTime exclusive.
    /// Guards against overlapping slots on the same date.
    /// </summary>
    public IReadOnlyList<AvailabilitySlot> DefineDailyAvailability(
        DateOnly date, TimeOnly startTime, TimeOnly endTime)
    {
        if (endTime <= startTime)
            throw new ArgumentException("End time must be after start time.");

        var existing = _slots
            .Where(s => s.Date == date && s.Status != SlotStatus.Cancelled)
            .Select(s => (s.StartTime, s.EndTime))
            .ToList();

        var created = new List<AvailabilitySlot>();
        var cursor = startTime;

        while (cursor.AddMinutes(30) <= endTime)
        {
            var slotEnd = cursor.AddMinutes(30);

            bool overlaps = existing.Any(e =>
                cursor < e.EndTime && slotEnd > e.StartTime);

            if (!overlaps)
            {
                var slot = AvailabilitySlot.Create(Id, date, cursor);
                _slots.Add(slot);
                created.Add(slot);
            }
            cursor = cursor.AddMinutes(30);
        }

        if (created.Count > 0)
            AddDomainEvent(new AvailabilityDefinedEvent(Id, date, created.Count, DateTimeOffset.UtcNow));

        return created.AsReadOnly();
    }
}
