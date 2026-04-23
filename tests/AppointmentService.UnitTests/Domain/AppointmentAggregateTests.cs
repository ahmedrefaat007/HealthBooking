using AppointmentService.Domain.Entities;
using AppointmentService.Domain.Enums;
using AppointmentService.Domain.Events;
using FluentAssertions;

namespace AppointmentService.UnitTests.Domain;

public sealed class AppointmentAggregateTests
{
    private static readonly DateTimeOffset _start = DateTimeOffset.UtcNow.AddDays(1);

    // ── Book ─────────────────────────────────────────────────────────────

    [Fact]
    public void Book_ValidArgs_SetsPropertiesAndRaisesEvent()
    {
        var patientId = Guid.NewGuid();
        var slotId = Guid.NewGuid();

        var appt = Appointment.Book(patientId, slotId, "Alice Smith", _start);

        appt.Id.Should().NotBeEmpty();
        appt.PatientId.Should().Be(patientId);
        appt.SlotId.Should().Be(slotId);
        appt.PatientName.Should().Be("Alice Smith");
        appt.ScheduledStartUtc.Should().Be(_start);
        appt.Status.Should().Be(AppointmentStatus.Booked);
        appt.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<AppointmentBookedEvent>();
    }

    [Fact]
    public void Book_EmptyPatientName_ThrowsArgumentException()
    {
        var act = () => Appointment.Book(Guid.NewGuid(), Guid.NewGuid(), "  ", _start);
        act.Should().Throw<ArgumentException>();
    }

    // ── Cancel ───────────────────────────────────────────────────────────

    [Fact]
    public void Cancel_BookedAppointment_UpdatesStatusAndRaisesEvent()
    {
        var appt = Appointment.Book(Guid.NewGuid(), Guid.NewGuid(), "Bob", _start);
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
        var appt = Appointment.Book(Guid.NewGuid(), Guid.NewGuid(), "Bob", _start);
        appt.Cancel("reason");

        var act = () => appt.Cancel("reason 2");
        act.Should().Throw<InvalidOperationException>();
    }

    // ── Complete ─────────────────────────────────────────────────────────

    [Fact]
    public void Complete_BookedAppointment_UpdatesStatusAndRaisesEvent()
    {
        var appt = Appointment.Book(Guid.NewGuid(), Guid.NewGuid(), "Carol", _start);
        appt.ClearDomainEvents();

        appt.Complete();

        appt.Status.Should().Be(AppointmentStatus.Completed);
        appt.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<AppointmentCompletedEvent>();
    }

    [Fact]
    public void Complete_CancelledAppointment_ThrowsInvalidOperationException()
    {
        var appt = Appointment.Book(Guid.NewGuid(), Guid.NewGuid(), "Carol", _start);
        appt.Cancel("reason");

        var act = () => appt.Complete();
        act.Should().Throw<InvalidOperationException>();
    }

    // ── Reschedule ───────────────────────────────────────────────────────

    [Fact]
    public void Reschedule_BookedAppointment_UpdatesSlotAndRaisesEvent()
    {
        var appt = Appointment.Book(Guid.NewGuid(), Guid.NewGuid(), "Dave", _start);
        var newSlotId = Guid.NewGuid();
        var newStart = _start.AddDays(2);
        appt.ClearDomainEvents();

        appt.Reschedule(newSlotId, newStart);

        appt.SlotId.Should().Be(newSlotId);
        appt.ScheduledStartUtc.Should().Be(newStart);
        appt.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<AppointmentRescheduledDomainEvent>();
    }

    [Fact]
    public void Reschedule_CancelledAppointment_ThrowsInvalidOperationException()
    {
        var appt = Appointment.Book(Guid.NewGuid(), Guid.NewGuid(), "Dave", _start);
        appt.Cancel("reason");

        var act = () => appt.Reschedule(Guid.NewGuid(), _start.AddDays(1));
        act.Should().Throw<InvalidOperationException>();
    }

    // ── Confirm ──────────────────────────────────────────────────────────

    [Fact]
    public void Confirm_BookedAppointment_UpdatesStatusAndRaisesEvent()
    {
        var appt = Appointment.Book(Guid.NewGuid(), Guid.NewGuid(), "Eve", _start);
        appt.ClearDomainEvents();

        appt.Confirm();

        appt.Status.Should().Be(AppointmentStatus.Confirmed);
        appt.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<AppointmentConfirmedDomainEvent>();
    }

    [Fact]
    public void Confirm_AlreadyConfirmed_ThrowsInvalidOperationException()
    {
        var appt = Appointment.Book(Guid.NewGuid(), Guid.NewGuid(), "Eve", _start);
        appt.Confirm();

        var act = () => appt.Confirm();
        act.Should().Throw<InvalidOperationException>();
    }

    // ── MarkNoShow ───────────────────────────────────────────────────────

    [Fact]
    public void MarkNoShow_ConfirmedAppointment_UpdatesStatusAndRaisesEvent()
    {
        var appt = Appointment.Book(Guid.NewGuid(), Guid.NewGuid(), "Frank", _start);
        appt.Confirm();
        appt.ClearDomainEvents();

        appt.MarkNoShow();

        appt.Status.Should().Be(AppointmentStatus.NoShow);
        appt.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<AppointmentNoShowDomainEvent>();
    }

    [Fact]
    public void MarkNoShow_BookedAppointment_ThrowsInvalidOperationException()
    {
        var appt = Appointment.Book(Guid.NewGuid(), Guid.NewGuid(), "Frank", _start);

        var act = () => appt.MarkNoShow();
        act.Should().Throw<InvalidOperationException>();
    }
}
