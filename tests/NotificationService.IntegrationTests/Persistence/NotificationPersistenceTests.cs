using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NotificationService.Domain.Entities;
using NotificationService.Infrastructure.Persistence;
using Testcontainers.MsSql;

namespace NotificationService.IntegrationTests.Persistence;

/// <summary>
/// Integration tests for NotificationLog persistence using a real SQL Server instance
/// spun up via Testcontainers.
/// </summary>
public sealed class NotificationPersistenceTests : IAsyncLifetime
{
    private readonly MsSqlContainer _sqlContainer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
    private NotificationDbContext   _db            = default!;

    // ── Lifecycle ────────────────────────────────────────────────────────
    public async Task InitializeAsync()
    {
        await _sqlContainer.StartAsync();

        var opts = new DbContextOptionsBuilder<NotificationDbContext>()
            .UseSqlServer(
                _sqlContainer.GetConnectionString(),
                sql => sql.MigrationsAssembly(typeof(NotificationDbContext).Assembly.FullName))
            .Options;

        _db = new NotificationDbContext(opts);
        await _db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _sqlContainer.DisposeAsync();
    }

    // ── Tests ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddAsync_CanPersistAndReload_NotificationLog()
    {
        var correlationId = Guid.NewGuid();
        var log = NotificationLog.Create(
            correlationId  : correlationId,
            eventType      : "V1_AppointmentBookedEvent",
            recipientEmail : "patient-test@example.com",
            subject        : "Appointment Confirmed",
            body           : "<p>Your appointment is booked.</p>");

        log.MarkSent();

        _db.NotificationLogs.Add(log);
        await _db.SaveChangesAsync();

        _db.ChangeTracker.Clear();

        var loaded = await _db.NotificationLogs
            .FirstOrDefaultAsync(n => n.CorrelationId == correlationId);

        loaded.Should().NotBeNull();
        loaded!.Status.Should().Be(NotificationStatus.Sent);
        loaded.RecipientEmail.Should().Be("patient-test@example.com");
    }

    [Fact]
    public async Task UniqueIndex_PreventsDuplicateCorrelationAndEventType()
    {
        var correlationId = Guid.NewGuid();
        const string eventType = "V1_AppointmentBookedEvent";

        var first = NotificationLog.Create(correlationId, eventType, "a@b.com", "Subject", "Body");
        first.MarkSent();

        _db.NotificationLogs.Add(first);
        await _db.SaveChangesAsync();

        _db.ChangeTracker.Clear();

        var duplicate = NotificationLog.Create(correlationId, eventType, "a@b.com", "Subject 2", "Body 2");
        _db.NotificationLogs.Add(duplicate);

        var act = async () => await _db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task ExistsByCorrelationAndTypeAsync_ReturnsTrueWhenRecordExists()
    {
        var correlationId = Guid.NewGuid();
        const string eventType = "V1_AppointmentCancelledEvent";

        var log = NotificationLog.Create(correlationId, eventType, "c@d.com", "Subject", "Body");
        log.MarkSent();

        _db.NotificationLogs.Add(log);
        await _db.SaveChangesAsync();

        _db.ChangeTracker.Clear();

        var exists = await _db.NotificationLogs
            .AnyAsync(n => n.CorrelationId == correlationId && n.EventType == eventType);

        exists.Should().BeTrue();
    }
}
