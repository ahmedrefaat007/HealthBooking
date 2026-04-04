using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PatientService.Infrastructure.Persistence;
using System.Net;
using System.Net.Http.Json;
using Testcontainers.MsSql;
using Xunit;

namespace PatientService.IntegrationTests.Persistence;

/// <summary>
/// Integration tests using a real SQL Server container via Testcontainers.
/// Verifies EF Core persistence round-trip, duplicate email enforcement, and outbox atomicity.
/// </summary>
public sealed class PatientPersistenceTests : IAsyncLifetime
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    private PatientDbContext _db = default!;

    public async Task InitializeAsync()
    {
        await _sql.StartAsync();

        var opts = new DbContextOptionsBuilder<PatientDbContext>()
            .UseSqlServer(_sql.GetConnectionString())
            .Options;

        // We need interceptors — provide no-op substitutes via DI scope
        var services = new ServiceCollection();
        services.AddDbContext<PatientDbContext>(o =>
            o.UseSqlServer(_sql.GetConnectionString()));
        services.AddScoped<PatientService.Infrastructure.Persistence.Interceptors.AuditInterceptor>();
        services.AddScoped<PatientService.Infrastructure.Persistence.Interceptors.OutboxPublishingInterceptor>();
        services.AddScoped<PatientService.Application.Interfaces.ICurrentUserService,
            PatientService.Infrastructure.Services.CurrentUserService>();
        services.AddHttpContextAccessor();

        var sp = services.BuildServiceProvider();
        _db = sp.GetRequiredService<PatientDbContext>();
        await _db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _sql.DisposeAsync();
    }

    [Fact]
    public async Task AddAsync_NewPatient_PersistsAndRetrievable()
    {
        var patient = PatientService.Domain.Entities.Patient.Register(
            "Integration", "User", "int@test.com", "+447911111111",
            DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-30)));

        _db.Patients.Add(patient);
        await _db.SaveChangesAsync();

        var loaded = await _db.Patients.FindAsync(patient.Id);
        loaded.Should().NotBeNull();
        loaded!.ContactEmail.Should().Be("int@test.com");
    }

    [Fact]
    public async Task AddAsync_DuplicateEmail_ThrowsDueToUniqueConstraint()
    {
        const string email = "dup@test.com";
        var p1 = PatientService.Domain.Entities.Patient.Register(
            "Dup", "One", email, "+447922222222",
            DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-30)));
        var p2 = PatientService.Domain.Entities.Patient.Register(
            "Dup", "Two", email, "+447933333333",
            DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-30)));

        _db.Patients.Add(p1);
        await _db.SaveChangesAsync();

        _db.Patients.Add(p2);
        var act = () => _db.SaveChangesAsync();

        await act.Should().ThrowAsync<Exception>(); // unique constraint violation
    }

    [Fact]
    public async Task SaveChanges_WithDomainEvents_WritesOutboxMessage()
    {
        var patient = PatientService.Domain.Entities.Patient.Register(
            "Outbox", "Test", "outbox@test.com", "+447944444444",
            DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-30)));

        _db.Patients.Add(patient);
        await _db.SaveChangesAsync();

        var outbox = await _db.OutboxMessages.ToListAsync();
        outbox.Should().HaveCountGreaterOrEqualTo(1);
        outbox[0].EventType.Should().Contain("PatientRegisteredEvent");
    }
}
