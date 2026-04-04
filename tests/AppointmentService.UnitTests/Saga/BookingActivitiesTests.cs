using AppointmentService.Application.Commands.BookAppointment;
using AppointmentService.Application.Interfaces;
using AppointmentService.Application.Saga;
using AppointmentService.Application.Saga.Activities;
using AppointmentService.Domain.Entities;
using FluentAssertions;
using HealthBooking.Contracts.Appointments.V1;
using MassTransit;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NSubstitute.ReturnsExtensions;

namespace AppointmentService.UnitTests.Saga;

/// <summary>
/// Unit tests for the three booking saga activities.
/// Each activity is tested in isolation; MassTransit's BehaviorContext
/// and IBehavior are mocked via NSubstitute.
/// </summary>
public sealed class BookingActivitiesTests
{
    // ── shared helpers ────────────────────────────────────────────────────

    private static BookingState NewSaga(Guid? correlationId = null) => new()
    {
        CorrelationId  = correlationId ?? Guid.NewGuid(),
        PatientId      = Guid.NewGuid(),
        SlotId         = Guid.NewGuid(),
        IdempotencyKey = Guid.NewGuid().ToString(),
        CreatedAt      = DateTimeOffset.UtcNow
    };

    private static (
        BehaviorContext<BookingState, V1_InitiateBookingCommand> ctx,
        IBehavior<BookingState, V1_InitiateBookingCommand>       next
    ) BuildContext(BookingState saga)
    {
        var ctx  = Substitute.For<BehaviorContext<BookingState, V1_InitiateBookingCommand>>();
        var next = Substitute.For<IBehavior<BookingState, V1_InitiateBookingCommand>>();

        ctx.Saga.Returns(saga);
        ctx.CancellationToken.Returns(CancellationToken.None);
        return (ctx, next);
    }

    private static BehaviorExceptionContext<BookingState, V1_InitiateBookingCommand, TException>
        BuildFaultContext<TException>(BookingState saga, TException ex)
        where TException : Exception
    {
        var ctx = Substitute.For<BehaviorExceptionContext<BookingState, V1_InitiateBookingCommand, TException>>();
        ctx.Saga.Returns(saga);
        ctx.Exception.Returns(ex);
        ctx.CancellationToken.Returns(CancellationToken.None);
        return ctx;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // VerifyPatientActivity
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task VerifyPatient_Execute_SetsSagaPatientNameAndCallsNext()
    {
        var saga          = NewSaga();
        var (ctx, next)   = BuildContext(saga);
        var patientClient = Substitute.For<IPatientGrpcClient>();

        patientClient
            .GetPatientByIdAsync(saga.PatientId, Arg.Any<CancellationToken>())
            .Returns(new PatientInfo(saga.PatientId, "Jane Doe", "jane@test.com"));

        var sut = new VerifyPatientActivity(patientClient);
        await sut.Execute(ctx, next);

        saga.PatientName.Should().Be("Jane Doe");
        await next.Received(1).Execute(ctx);
    }

    [Fact]
    public async Task VerifyPatient_Execute_PatientNotFound_Throws()
    {
        var saga          = NewSaga();
        var (ctx, next)   = BuildContext(saga);
        var patientClient = Substitute.For<IPatientGrpcClient>();

        patientClient
            .GetPatientByIdAsync(saga.PatientId, Arg.Any<CancellationToken>())
            .ReturnsNull();

        var sut = new VerifyPatientActivity(patientClient);
        var act = () => sut.Execute(ctx, next);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{saga.PatientId}*");
        await next.DidNotReceive().Execute(Arg.Any<BehaviorContext<BookingState, V1_InitiateBookingCommand>>());
    }

    [Fact]
    public async Task VerifyPatient_Faulted_JustDelegatesDown()
    {
        var saga          = NewSaga();
        var patientClient = Substitute.For<IPatientGrpcClient>();
        var faultCtx      = BuildFaultContext(saga, new Exception("upstream"));
        var next          = Substitute.For<IBehavior<BookingState, V1_InitiateBookingCommand>>();

        var sut = new VerifyPatientActivity(patientClient);
        await sut.Faulted(faultCtx, next);

        await next.Received(1).Faulted(faultCtx);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // LockSlotActivity
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task LockSlot_Execute_Success_SetsSlotWasLockedAndCallsNext()
    {
        var saga        = NewSaga();
        var (ctx, next) = BuildContext(saga);
        var slotClient  = Substitute.For<IProviderSlotGrpcClient>();

        slotClient
            .LockSlotAsync(saga.SlotId, saga.CorrelationId, Arg.Any<CancellationToken>())
            .Returns(true);

        var sut = new LockSlotActivity(slotClient);
        await sut.Execute(ctx, next);

        saga.SlotWasLocked.Should().BeTrue();
        await next.Received(1).Execute(ctx);
    }

    [Fact]
    public async Task LockSlot_Execute_SlotUnavailable_ThrowsSlotConflictException()
    {
        var saga        = NewSaga();
        var (ctx, next) = BuildContext(saga);
        var slotClient  = Substitute.For<IProviderSlotGrpcClient>();

        slotClient
            .LockSlotAsync(saga.SlotId, saga.CorrelationId, Arg.Any<CancellationToken>())
            .Returns(false);

        var sut = new LockSlotActivity(slotClient);
        var act = () => sut.Execute(ctx, next);

        await act.Should().ThrowAsync<SlotConflictException>();
        saga.SlotWasLocked.Should().BeFalse();
        await next.DidNotReceive().Execute(Arg.Any<BehaviorContext<BookingState, V1_InitiateBookingCommand>>());
    }

    [Fact]
    public async Task LockSlot_Faulted_WhenSlotWasLocked_ReleasesSlot()
    {
        var saga       = NewSaga();
        saga.SlotWasLocked = true;
        var slotClient = Substitute.For<IProviderSlotGrpcClient>();
        var faultCtx   = BuildFaultContext(saga, new Exception("downstream fail"));
        var next       = Substitute.For<IBehavior<BookingState, V1_InitiateBookingCommand>>();

        var sut = new LockSlotActivity(slotClient);
        await sut.Faulted(faultCtx, next);

        await slotClient.Received(1).ReleaseSlotAsync(saga.SlotId, Arg.Any<CancellationToken>());
        saga.SlotWasLocked.Should().BeFalse();
        await next.Received(1).Faulted(faultCtx);
    }

    [Fact]
    public async Task LockSlot_Faulted_WhenSlotWasNotLocked_DoesNotRelease()
    {
        var saga       = NewSaga();
        saga.SlotWasLocked = false;
        var slotClient = Substitute.For<IProviderSlotGrpcClient>();
        var faultCtx   = BuildFaultContext(saga, new Exception("early fail"));
        var next       = Substitute.For<IBehavior<BookingState, V1_InitiateBookingCommand>>();

        var sut = new LockSlotActivity(slotClient);
        await sut.Faulted(faultCtx, next);

        await slotClient.DidNotReceive().ReleaseSlotAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await next.Received(1).Faulted(faultCtx);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // PersistAppointmentActivity
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PersistAppointment_Execute_NewBooking_PersistsAndSetsAppointmentId()
    {
        var saga         = NewSaga();
        saga.PatientName = "Jane Doe";
        var (ctx, next)  = BuildContext(saga);
        var appointments = Substitute.For<IAppointmentRepository>();
        var idempotency  = Substitute.For<IIdempotencyRepository>();

        idempotency.FindAsync(saga.IdempotencyKey, Arg.Any<CancellationToken>())
            .ReturnsNull();

        var slotClient = Substitute.For<IProviderSlotGrpcClient>();
        slotClient.GetSlotByIdAsync(saga.SlotId, Arg.Any<CancellationToken>())
            .Returns(new SlotInfo(saga.SlotId, Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(1).AddMinutes(30), "Locked"));

        var sut = new PersistAppointmentActivity(appointments, idempotency, slotClient);
        await sut.Execute(ctx, next);

        saga.AppointmentId.Should().NotBeNull();
        await appointments.Received(1).AddAsync(Arg.Any<Appointment>(), Arg.Any<CancellationToken>());
        await idempotency.Received(1).AddAsync(Arg.Any<BookingIdempotencyKey>(), Arg.Any<CancellationToken>());
        await appointments.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await next.Received(1).Execute(ctx);
    }

    [Fact]
    public async Task PersistAppointment_Execute_DuplicateKey_ReusesExistingAppointmentId()
    {
        var saga             = NewSaga();
        saga.PatientName     = "Jane Doe";
        var existingApptId   = Guid.NewGuid();
        var (ctx, next)      = BuildContext(saga);
        var appointments     = Substitute.For<IAppointmentRepository>();
        var idempotency      = Substitute.For<IIdempotencyRepository>();

        idempotency.FindAsync(saga.IdempotencyKey, Arg.Any<CancellationToken>())
            .Returns(new BookingIdempotencyKey
            {
                Key           = saga.IdempotencyKey,
                AppointmentId = existingApptId
            });

        var slotClient = Substitute.For<IProviderSlotGrpcClient>();
        var sut = new PersistAppointmentActivity(appointments, idempotency, slotClient);
        await sut.Execute(ctx, next);

        saga.AppointmentId.Should().Be(existingApptId);
        await appointments.DidNotReceive().AddAsync(Arg.Any<Appointment>(), Arg.Any<CancellationToken>());
        await next.Received(1).Execute(ctx);
    }

    [Fact]
    public async Task PersistAppointment_Faulted_JustDelegatesDown()
    {
        var appointments = Substitute.For<IAppointmentRepository>();
        var idempotency  = Substitute.For<IIdempotencyRepository>();
        var faultCtx     = BuildFaultContext(NewSaga(), new Exception("upstream"));
        var next         = Substitute.For<IBehavior<BookingState, V1_InitiateBookingCommand>>();

        var sut = new PersistAppointmentActivity(appointments, idempotency, Substitute.For<IProviderSlotGrpcClient>());
        await sut.Faulted(faultCtx, next);

        await next.Received(1).Faulted(faultCtx);
    }
}
