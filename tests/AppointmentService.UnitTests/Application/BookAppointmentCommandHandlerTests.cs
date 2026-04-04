using AppointmentService.Application.Commands.BookAppointment;
using AppointmentService.Application.Interfaces;
using AppointmentService.Domain.Entities;
using Bogus;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace AppointmentService.UnitTests.Application;

public sealed class BookAppointmentCommandHandlerTests
{
    private readonly IAppointmentRepository  _appointments  = Substitute.For<IAppointmentRepository>();
    private readonly IIdempotencyRepository  _idempotency   = Substitute.For<IIdempotencyRepository>();
    private readonly IPatientGrpcClient      _patientClient = Substitute.For<IPatientGrpcClient>();
    private readonly IProviderSlotGrpcClient _slotClient    = Substitute.For<IProviderSlotGrpcClient>();

    private readonly Faker _faker = new();

    private BookAppointmentCommandHandler CreateHandler() =>
        new(_appointments, _idempotency, _patientClient, _slotClient);

    [Fact]
    public async Task Handle_NewBooking_ReturnsAppointmentDto()
    {
        // Arrange
        var patientId   = Guid.NewGuid();
        var slotId      = Guid.NewGuid();
        var idempKey    = _faker.Random.AlphaNumeric(20);
        var patientInfo = new PatientInfo(patientId, "Jane Doe", "jane@test.com");

        _idempotency.FindAsync(idempKey, Arg.Any<CancellationToken>())
                    .ReturnsNull();
        _patientClient.GetPatientByIdAsync(patientId, Arg.Any<CancellationToken>())
                      .Returns(patientInfo);
        _slotClient.LockSlotAsync(slotId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                   .Returns(true);

        var command = new BookAppointmentCommand(patientId, slotId, idempKey);

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.PatientId.Should().Be(patientId);
        result.SlotId.Should().Be(slotId);
        result.Status.Should().Be("Booked");
        await _appointments.Received(1).AddAsync(Arg.Any<Appointment>(), Arg.Any<CancellationToken>());
        await _appointments.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _idempotency.Received(1).AddAsync(Arg.Any<BookingIdempotencyKey>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DuplicateIdempotencyKey_ReturnsExistingAppointment()
    {
        // Arrange
        var appointmentId = Guid.NewGuid();
        var existingKey   = new BookingIdempotencyKey
        {
            Key           = "existing-key",
            AppointmentId = appointmentId
        };
        var existingAppt = Appointment.Book(Guid.NewGuid(), Guid.NewGuid(), "John Doe");

        _idempotency.FindAsync("existing-key", Arg.Any<CancellationToken>())
                    .Returns(existingKey);
        _appointments.GetByIdAsync(appointmentId, Arg.Any<CancellationToken>())
                     .Returns(existingAppt);

        var command = new BookAppointmentCommand(Guid.NewGuid(), Guid.NewGuid(), "existing-key");

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        // No new slot lock or patient lookup performed
        await _patientClient.DidNotReceive().GetPatientByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _slotClient.DidNotReceive().LockSlotAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PatientNotFound_ThrowsInvalidOperationException()
    {
        // Arrange
        var idempKey = _faker.Random.AlphaNumeric(20);

        _idempotency.FindAsync(idempKey, Arg.Any<CancellationToken>()).ReturnsNull();
        _patientClient.GetPatientByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                      .ReturnsNull();

        var command = new BookAppointmentCommand(Guid.NewGuid(), Guid.NewGuid(), idempKey);

        // Act
        var act = async () => await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
                 .WithMessage("*not found*");
    }

    [Fact]
    public async Task Handle_SlotNotAvailable_ThrowsSlotConflictException()
    {
        // Arrange
        var patientId   = Guid.NewGuid();
        var slotId      = Guid.NewGuid();
        var idempKey    = _faker.Random.AlphaNumeric(20);
        var patientInfo = new PatientInfo(patientId, "Jane Doe", "jane@test.com");

        _idempotency.FindAsync(idempKey, Arg.Any<CancellationToken>()).ReturnsNull();
        _patientClient.GetPatientByIdAsync(patientId, Arg.Any<CancellationToken>())
                      .Returns(patientInfo);
        _slotClient.LockSlotAsync(slotId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                   .Returns(false);

        var command = new BookAppointmentCommand(patientId, slotId, idempKey);

        // Act
        var act = async () => await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<SlotConflictException>();
    }
}
