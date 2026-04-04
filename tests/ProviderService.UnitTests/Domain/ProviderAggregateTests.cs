using FluentAssertions;
using ProviderService.Domain.Entities;
using ProviderService.Domain.Enums;
using ProviderService.Domain.Events;
using Xunit;

namespace ProviderService.UnitTests.Domain;

public sealed class ProviderAggregateTests
{
    private static Provider Build() =>
        Provider.Register("Jane", "Smith", "Cardiology", "LIC-00001");

    [Fact]
    public void Register_ValidArgs_SetsPropertiesAndRaisesEvent()
    {
        var provider = Build();

        provider.FirstName.Should().Be("Jane");
        provider.Specialty.Should().Be("Cardiology");
        provider.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<ProviderRegisteredEvent>();
    }

    [Fact]
    public void Register_EmptySpecialty_Throws()
    {
        var act = () => Provider.Register("Jane", "Smith", "", "LIC-X");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void DefineDailyAvailability_NoOverlap_CreatesExpectedSlots()
    {
        var provider = Build();
        provider.ClearDomainEvents();

        var slots = provider.DefineDailyAvailability(
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
            new TimeOnly(9, 0),
            new TimeOnly(11, 0));

        slots.Should().HaveCount(4); // 4 x 30-min slots
        slots.Should().AllSatisfy(s => s.Status.Should().Be(SlotStatus.Available));
        provider.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<AvailabilityDefinedEvent>();
    }

    [Fact]
    public void DefineDailyAvailability_EndBeforeStart_Throws()
    {
        var provider = Build();
        var act = () => provider.DefineDailyAvailability(
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
            new TimeOnly(11, 0),
            new TimeOnly(9, 0));

        act.Should().Throw<ArgumentException>();
    }
}

public sealed class AvailabilitySlotTests
{
    private static AvailabilitySlot CreateSlot()
    {
        var provider = Provider.Register("X", "Y", "Ortho", "LIC-99");
        var slots = provider.DefineDailyAvailability(
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
            new TimeOnly(8, 0),
            new TimeOnly(8, 30));
        return slots[0];
    }

    [Fact]
    public void Lock_AvailableSlot_ChangesStatusToLocked()
    {
        var slot = CreateSlot();
        var apptId = Guid.NewGuid();
        slot.Lock(apptId);

        slot.Status.Should().Be(SlotStatus.Locked);
        slot.AppointmentId.Should().Be(apptId);
    }

    [Fact]
    public void Lock_AlreadyLocked_Throws()
    {
        var slot = CreateSlot();
        slot.Lock(Guid.NewGuid());
        var act = () => slot.Lock(Guid.NewGuid());
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Release_LockedSlot_ChangesStatusToAvailable()
    {
        var slot = CreateSlot();
        slot.Lock(Guid.NewGuid());
        slot.Release();

        slot.Status.Should().Be(SlotStatus.Available);
        slot.AppointmentId.Should().BeNull();
    }
}
