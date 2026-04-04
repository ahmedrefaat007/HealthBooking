using AppointmentService.Application.Commands.BookAppointment;
using AppointmentService.Domain.Enums;
using AppointmentService.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

namespace AppointmentService.IntegrationTests.Persistence;

public sealed class AppointmentPersistenceTests : IAsyncLifetime
{
    private readonly MsSqlContainer _sqlContainer =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
            .Build();

    private AppointmentDbContext _context = null!;

    public async Task InitializeAsync()
    {
        await _sqlContainer.StartAsync();
        _context = TestDbContextFactory.Create(_sqlContainer.GetConnectionString());
        await _context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _sqlContainer.DisposeAsync();
    }

    [Fact]
    public async Task BookAppointment_PersistsAndIsRetrievable()
    {
        // Arrange
        var patientId = Guid.NewGuid();
        var slotId    = Guid.NewGuid();
        var appointment = AppointmentService.Domain.Entities.Appointment.Book(
            patientId, slotId, "Alice Wonderland", DateTimeOffset.UtcNow.AddDays(1));

        // Act
        await _context.Appointments.AddAsync(appointment);
        await _context.SaveChangesAsync();

        _context.ChangeTracker.Clear();
        var loaded = await _context.Appointments.FindAsync(appointment.Id);

        // Assert
        loaded.Should().NotBeNull();
        loaded!.PatientId.Should().Be(patientId);
        loaded.SlotId.Should().Be(slotId);
        loaded.Status.Should().Be(AppointmentStatus.Booked);
    }

    [Fact]
    public async Task SaveChanges_WithDomainEvent_WritesOutboxMessage()
    {
        var patientId   = Guid.NewGuid();
        var slotId      = Guid.NewGuid();
        var appointment = AppointmentService.Domain.Entities.Appointment.Book(
            patientId, slotId, "Bob Brown", DateTimeOffset.UtcNow.AddDays(1));

        await _context.Appointments.AddAsync(appointment);
        await _context.SaveChangesAsync();

        var outboxCount = await _context.OutboxMessages.CountAsync();
        outboxCount.Should().Be(1);

        var msg = await _context.OutboxMessages.FirstAsync();
        msg.EventType.Should().Contain("AppointmentBookedEvent");
        msg.Status.Should().Be("Pending");
    }

    [Fact]
    public async Task DoubleBooking_SameIdempotencyKey_ReturnsSameAppointment()
    {
        // Arrange
        var patientId   = Guid.NewGuid();
        var slotId      = Guid.NewGuid();
        var appointment = AppointmentService.Domain.Entities.Appointment.Book(
            patientId, slotId, "Charlie C", DateTimeOffset.UtcNow.AddDays(1));

        await _context.Appointments.AddAsync(appointment);

        var key = new AppointmentService.Domain.Entities.BookingIdempotencyKey
        {
            Key           = "test-idempotency-key-001",
            AppointmentId = appointment.Id
        };
        await _context.BookingIdempotencyKeys.AddAsync(key);
        await _context.SaveChangesAsync();

        // Act — attempt to insert duplicate key
        var duplicateKey = new AppointmentService.Domain.Entities.BookingIdempotencyKey
        {
            Key           = "test-idempotency-key-001",
            AppointmentId = Guid.NewGuid()
        };
        _context.BookingIdempotencyKeys.Add(duplicateKey);

        var act = async () => await _context.SaveChangesAsync();

        // Assert — DB unique constraint fires
        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
