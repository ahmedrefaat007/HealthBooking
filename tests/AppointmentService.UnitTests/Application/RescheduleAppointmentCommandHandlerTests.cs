using AppointmentService.Application.Commands.BookAppointment;
using AppointmentService.Application.Commands.RescheduleAppointment;
using AppointmentService.Application.Interfaces;
using AppointmentService.Domain.Entities;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace AppointmentService.UnitTests.Application;

public sealed class RescheduleAppointmentCommandHandlerTests
{
    private readonly IAppointmentRepository  _appointments = Substitute.For<IAppointmentRepository>();
    private readonly IProviderSlotGrpcClient _slotClient   = Substitute.For<IProviderSlotGrpcClient>();

    private static readonly DateTimeOffset _originalStart = DateTimeOffset.UtcNow.AddDays(1);
    private static readonly DateTimeOffset _newStart      = DateTimeOffset.UtcNow.AddDays(3);

    private RescheduleAppointmentCommandHandler CreateHandler() =>
        new(_appointments, _slotClient);

    [Fact]
    public async Task Handle_ValidReschedule_UpdatesSlotAndReleasesOld()
    {
        // Arrange
        var patientId  = Guid.NewGuid();
        var oldSlotId  = Guid.NewGuid();
        var newSlotId  = Guid.NewGuid();

        var appt = Appointment.Book(patientId, oldSlotId, "Alice", _originalStart);
        var newSlotInfo = new SlotInfo(newSlotId, Guid.NewGuid(), _newStart, _newStart.AddMinutes(30), "Available");

        _appointments.GetByIdAsync(appt.Id, Arg.Any<CancellationToken>()).Returns(appt);
        _slotClient.GetSlotByIdAsync(newSlotId, Arg.Any<CancellationToken>()).Returns(newSlotInfo);
        _slotClient.LockSlotAsync(newSlotId, appt.Id, Arg.Any<CancellationToken>()).Returns(true);

        var command = new RescheduleAppointmentCommand(appt.Id, newSlotId, patientId.ToString());

        // Act
        await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        appt.SlotId.Should().Be(newSlotId);
        appt.ScheduledStartUtc.Should().Be(_newStart);
        await _appointments.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _slotClient.Received(1).ReleaseSlotAsync(oldSlotId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AppointmentNotFound_ThrowsInvalidOperationException()
    {
        _appointments.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).ReturnsNull();

        var command = new RescheduleAppointmentCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid().ToString());
        var act     = async () => await CreateHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Handle_WrongCaller_ThrowsUnauthorizedAccessException()
    {
        var appt = Appointment.Book(Guid.NewGuid(), Guid.NewGuid(), "Bob", _originalStart);
        _appointments.GetByIdAsync(appt.Id, Arg.Any<CancellationToken>()).Returns(appt);

        var command = new RescheduleAppointmentCommand(appt.Id, Guid.NewGuid(), Guid.NewGuid().ToString());
        var act     = async () => await CreateHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Handle_SlotNotAvailable_ThrowsSlotConflictException()
    {
        var patientId = Guid.NewGuid();
        var newSlotId = Guid.NewGuid();
        var appt      = Appointment.Book(patientId, Guid.NewGuid(), "Carol", _originalStart);
        var bookedSlot = new SlotInfo(newSlotId, Guid.NewGuid(), _newStart, _newStart.AddMinutes(30), "Booked");

        _appointments.GetByIdAsync(appt.Id, Arg.Any<CancellationToken>()).Returns(appt);
        _slotClient.GetSlotByIdAsync(newSlotId, Arg.Any<CancellationToken>()).Returns(bookedSlot);

        var command = new RescheduleAppointmentCommand(appt.Id, newSlotId, patientId.ToString());
        var act     = async () => await CreateHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<SlotConflictException>();
    }

    [Fact]
    public async Task Handle_LockFails_ThrowsSlotConflictExceptionAndDoesNotSave()
    {
        var patientId = Guid.NewGuid();
        var newSlotId = Guid.NewGuid();
        var appt      = Appointment.Book(patientId, Guid.NewGuid(), "Dave", _originalStart);
        var slotInfo  = new SlotInfo(newSlotId, Guid.NewGuid(), _newStart, _newStart.AddMinutes(30), "Available");

        _appointments.GetByIdAsync(appt.Id, Arg.Any<CancellationToken>()).Returns(appt);
        _slotClient.GetSlotByIdAsync(newSlotId, Arg.Any<CancellationToken>()).Returns(slotInfo);
        _slotClient.LockSlotAsync(newSlotId, appt.Id, Arg.Any<CancellationToken>()).Returns(false);

        var command = new RescheduleAppointmentCommand(appt.Id, newSlotId, patientId.ToString());
        var act     = async () => await CreateHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<SlotConflictException>();
        await _appointments.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
