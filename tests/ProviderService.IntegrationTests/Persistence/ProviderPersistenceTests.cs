using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ProviderService.Domain.Entities;
using ProviderService.Infrastructure.Persistence;
using Testcontainers.MsSql;

namespace ProviderService.IntegrationTests.Persistence;

public sealed class ProviderPersistenceTests : IAsyncLifetime
{
    private readonly MsSqlContainer _sqlContainer =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
            .Build();

    private ProviderDbContext _context = null!;

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
    public async Task RegisterProvider_PersistsAndIsRetrievable()
    {
        // Arrange
        var provider = Provider.Register("Jane", "Smith", "Cardiology", "LIC-001");

        // Act
        await _context.Providers.AddAsync(provider);
        await _context.SaveChangesAsync();

        _context.ChangeTracker.Clear();
        var loaded = await _context.Providers.FindAsync(provider.Id);

        // Assert
        loaded.Should().NotBeNull();
        loaded!.FirstName.Should().Be("Jane");
        loaded.LastName.Should().Be("Smith");
        loaded.Specialty.Should().Be("Cardiology");
        loaded.LicenseNumber.Should().Be("LIC-001");
    }

    [Fact]
    public async Task SaveChanges_WithDomainEvent_WritesOutboxMessage()
    {
        // Arrange
        var provider = Provider.Register("John", "Doe", "Neurology", "LIC-002");

        // Act
        await _context.Providers.AddAsync(provider);
        await _context.SaveChangesAsync();

        // Assert
        var outboxCount = await _context.OutboxMessages.CountAsync();
        outboxCount.Should().Be(1);

        var msg = await _context.OutboxMessages.FirstAsync();
        msg.EventType.Should().Contain("ProviderRegisteredEvent");
        msg.Status.Should().Be("Pending");
    }

    [Fact]
    public async Task DefineDailyAvailability_PersistsSlotsWithProvider()
    {
        // Arrange
        var provider = Provider.Register("Alice", "Brown", "Orthopedics", "LIC-003");
        await _context.Providers.AddAsync(provider);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        // Act – define 2 slots (08:00–09:00 = two 30-min slots)
        var loaded = await _context.Providers
            .Include(p => p.Slots)
            .FirstAsync(p => p.Id == provider.Id);

        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        var startTime = new TimeOnly(8, 0);
        var endTime = new TimeOnly(9, 0);

        loaded.DefineDailyAvailability(date, startTime, endTime);
        _context.AvailabilitySlots.AddRange(loaded.Slots);
        await _context.SaveChangesAsync();

        _context.ChangeTracker.Clear();

        // Assert
        var slots = await _context.AvailabilitySlots
            .Where(s => s.ProviderId == provider.Id)
            .ToListAsync();

        slots.Should().HaveCount(2);
        slots.Should().AllSatisfy(s =>
        {
            s.Date.Should().Be(date);
            s.ProviderId.Should().Be(provider.Id);
        });
    }
}
