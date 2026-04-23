using AppointmentService.Application.Commands.CancelAppointment;
using AppointmentService.Application.Interfaces;
using AppointmentService.Domain.Entities;
using AppointmentService.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace AppointmentService.UnitTests.Application;

public sealed class CancelAppointmentCommandHandlerTests
{
    private readonly IAppointmentRepository _appointments = Substitute.For<IAppointmentRepository>();
    private readonly IProviderSlotGrpcClient _slotClient = Substitute.For<IProviderSlotGrpcClient>();

    private IConfiguration BuildConfig(int noticeHours = 2)
    {
        var dict = new Dictionary<string, string?> { ["Appointment:CancellationNoticeHours"] = noticeHours.ToString() };
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private CancelAppointmentCommandHandler CreateHandler(int noticeHours = 2) =>
        new(_appointments, _slotClient, BuildConfig(noticeHours));

    [Fact]
    public async Task Handle_ValidCancellation_UpdatesStatusAndReleasesSlot()
    {
        var patientId = Guid.NewGuid();
        // Schedule 3 hours from now, cancellation window is 2 hours → within policy
        var appt = Appointment.Book(patientId, Guid.NewGuid(), "Alice", DateTimeOffset.UtcNow.AddHours(3));
        _appointments.GetByIdAsync(appt.Id, Arg.Any<CancellationToken>()).Returns(appt);

        var command = new CancelAppointmentCommand(appt.Id, "Change of plans", patientId.ToString());

        await CreateHandler().Handle(command, CancellationToken.None);

        appt.Status.Should().Be(AppointmentStatus.Cancelled);
        appt.CancelReason.Should().Be("Change of plans");
        await _slotClient.Received(1).ReleaseSlotAsync(appt.SlotId, Arg.Any<CancellationToken>());
        await _appointments.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithinNoticeWindow_ThrowsInvalidOperationException()
    {
        var patientId = Guid.NewGuid();
        // Schedule 1 hour from now, cancellation window is 2 hours → violation
        var appt = Appointment.Book(patientId, Guid.NewGuid(), "Bob", DateTimeOffset.UtcNow.AddHours(1));
        _appointments.GetByIdAsync(appt.Id, Arg.Any<CancellationToken>()).Returns(appt);

        var command = new CancelAppointmentCommand(appt.Id, "Late cancel", patientId.ToString());
        var act = async () => await CreateHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*2 hour*");
    }

    [Fact]
    public async Task Handle_AppointmentNotFound_ThrowsInvalidOperationException()
    {
        _appointments.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).ReturnsNull();

        var command = new CancelAppointmentCommand(Guid.NewGuid(), "reason", Guid.NewGuid().ToString());
        var act = async () => await CreateHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Handle_WrongCaller_ThrowsUnauthorizedAccessException()
    {
        var appt = Appointment.Book(Guid.NewGuid(), Guid.NewGuid(), "Carol", DateTimeOffset.UtcNow.AddHours(5));
        _appointments.GetByIdAsync(appt.Id, Arg.Any<CancellationToken>()).Returns(appt);

        var command = new CancelAppointmentCommand(appt.Id, "reason", Guid.NewGuid().ToString());
        var act = async () => await CreateHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }
}
