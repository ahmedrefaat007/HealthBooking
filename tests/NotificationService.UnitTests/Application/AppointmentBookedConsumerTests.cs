using FluentAssertions;
using HealthBooking.Contracts.Appointments.V1;
using MassTransit;
using Microsoft.Extensions.Logging;
using NotificationService.Application.Consumers;
using NotificationService.Application.Interfaces;
using NotificationService.Domain.Entities;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace NotificationService.UnitTests.Application;

public class AppointmentBookedConsumerTests
{
    private readonly INotificationLogRepository _repository;
    private readonly IEmailService _emailService;
    private readonly IPatientEmailClient _patientEmailClient;
    private readonly AppointmentBookedConsumer _sut;

    public AppointmentBookedConsumerTests()
    {
        _repository = Substitute.For<INotificationLogRepository>();
        _emailService = Substitute.For<IEmailService>();
        _patientEmailClient = Substitute.For<IPatientEmailClient>();
        var logger = Substitute.For<ILogger<AppointmentBookedConsumer>>();

        _patientEmailClient.GetPatientEmailAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        _sut = new AppointmentBookedConsumer(_repository, _emailService, _patientEmailClient, logger);
    }

    // ── helper ───────────────────────────────────────────────────────────
    private static (ConsumeContext<V1_AppointmentBookedEvent> ctx, V1_AppointmentBookedEvent msg) BuildContext()
    {
        var msg = new V1_AppointmentBookedEvent(
            AppointmentId: Guid.NewGuid(),
            PatientId: Guid.NewGuid(),
            ProviderId: Guid.NewGuid(),
            SlotId: Guid.NewGuid(),
            ScheduledStartUtc: DateTimeOffset.UtcNow.AddDays(1),
            ScheduledEndUtc: DateTimeOffset.UtcNow.AddDays(1).AddHours(1),
            SagaCorrelationId: Guid.NewGuid(),
            OccurredAt: DateTimeOffset.UtcNow);

        var ctx = Substitute.For<ConsumeContext<V1_AppointmentBookedEvent>>();
        ctx.Message.Returns(msg);
        ctx.CancellationToken.Returns(CancellationToken.None);
        return (ctx, msg);
    }

    // ── happy path ───────────────────────────────────────────────────────
    [Fact]
    public async Task Consume_NewEvent_SendsEmailAndPersistsSentLog()
    {
        var (ctx, msg) = BuildContext();

        _repository
            .ExistsByCorrelationAndTypeAsync(msg.AppointmentId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);

        await _sut.Consume(ctx);

        await _emailService.Received(1)
            .SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        await _repository.Received(1)
            .AddAsync(
                Arg.Is<NotificationLog>(l =>
                    l.Status == NotificationStatus.Sent &&
                    l.CorrelationId == msg.AppointmentId),
                Arg.Any<CancellationToken>());

        await _repository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── idempotency ───────────────────────────────────────────────────────
    [Fact]
    public async Task Consume_DuplicateEvent_SkipsEmailAndPersist()
    {
        var (ctx, msg) = BuildContext();

        _repository
            .ExistsByCorrelationAndTypeAsync(msg.AppointmentId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        await _sut.Consume(ctx);

        await _emailService
            .DidNotReceive()
            .SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        await _repository
            .DidNotReceive()
            .AddAsync(Arg.Any<NotificationLog>(), Arg.Any<CancellationToken>());
    }

    // ── email failure ─────────────────────────────────────────────────────
    [Fact]
    public async Task Consume_EmailThrows_PersistsFailedLogAndDoesNotRethrow()
    {
        var (ctx, msg) = BuildContext();

        _repository
            .ExistsByCorrelationAndTypeAsync(msg.AppointmentId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);

        _emailService
            .SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("SMTP unavailable"));

        // consumer catches the exception internally — should not rethrow
        var act = async () => await _sut.Consume(ctx);
        await act.Should().NotThrowAsync();

        await _repository.Received(1)
            .AddAsync(
                Arg.Is<NotificationLog>(l =>
                    l.Status == NotificationStatus.Failed &&
                    l.CorrelationId == msg.AppointmentId),
                Arg.Any<CancellationToken>());

        await _repository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
