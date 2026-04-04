using AppointmentService.Domain.Entities;
using AppointmentService.Domain.Enums;
using AppointmentService.Domain.Events;
using FluentAssertions;

namespace AppointmentService.UnitTests.Domain;

public sealed class AppointmentAggregateTests
{
    [Fact]
    public void Book_ValidArgs_SetsPropertiesAndRaisesEvent()
    {
        var patientId  = Guid.NewGuid();
        var slotId     = Guid.NewGuid();

        var appt = Appointment.Book(patientId, slotId, "Alice Smith");

        appt.Id.Should().NotBeEmpty();
        appt.PatientId.Should().Be(patientId);
        appt.SlotId.Should().Be(slotId);
        appt.PatientName.Should().Be("Alice Smith");
        appt.Status.Should().Be(AppointmentStatus.Booked);
        appt.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<AppointmentBookedEvent>();
    }

    [Fact]
    public void Book_EmptyPatientName_ThrowsArgumentException()
    {
        var act = () => Appointment.Book(Guid.NewGuid(), Guid.NewGuid(), "  ");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Cancel_BookedAppointment_UpdatesStatusAndRaisesEvent()
    {
        var appt = Appointment.Book(Guid.NewGuid(), Guid.NewGuid(), "Bob");
        appt.ClearDomainEvents();

        appt.Cancel("Doctor unavailable");

        appt.Status.Should().Be(AppointmentStatus.Cancelled);
        appt.CancelReason.Should().Be("Doctor unavailable");
        appt.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<AppointmentCancelledEvent>();
    }

    [Fact]
    public void Cancel_AlreadyCancelled_ThrowsInvalidOperationException()
    {
        var appt = Appointment.Book(Guid.NewGuid(), Guid.NewGuid(), "Bob");
        appt.Cancel("reason");

        var act = () => appt.Cancel("reason 2");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Complete_BookedAppointment_UpdatesStatusAndRaisesEvent()
    {
        var appt = Appointment.Book(Guid.NewGuid(), Guid.NewGuid(), "Carol");
        appt.ClearDomainEvents();

        appt.Complete();

        appt.Status.Should().Be(AppointmentStatus.Completed);
        appt.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<AppointmentCompletedEvent>();
    }

    [Fact]
    public void Complete_CancelledAppointment_ThrowsInvalidOperationException()
    {
        var appt = Appointment.Book(Guid.NewGuid(), Guid.NewGuid(), "Carol");
        appt.Cancel("reason");

        var act = () => appt.Complete();
        act.Should().Throw<InvalidOperationException>();
    }
}
