using AppointmentService.Application.Saga.Activities;
using HealthBooking.Contracts.Appointments.V1;
using MassTransit;

namespace AppointmentService.Application.Saga;

/// <summary>
/// Orchestrates the three-step booking flow:
///   1. Verify patient identity via gRPC (VerifyPatientActivity)
///   2. Lock the availability slot via gRPC (LockSlotActivity)
///   3. Persist the Appointment entity + idempotency key (PersistAppointmentActivity)
///
/// On success → responds with V1_BookingCompletedEvent.
/// On any failure → LockSlotActivity.Faulted compensates by releasing the slot,
///                  then the machine transitions to Failed and responds with V1_BookingFailedEvent.
/// </summary>
public sealed class BookingStateMachine : MassTransitStateMachine<BookingState>
{
    public State Submitted { get; private set; } = null!;
    public State Completed { get; private set; } = null!;
    public State Failed    { get; private set; } = null!;

    public Event<V1_InitiateBookingCommand> BookingInitiated { get; private set; } = null!;

    public BookingStateMachine()
    {
        InstanceState(x => x.CurrentState);

        Event(() => BookingInitiated,
            x => x.CorrelateById(ctx => ctx.Message.CorrelationId));

        Initially(
            When(BookingInitiated)
                .Then(ctx =>
                {
                    ctx.Saga.PatientId      = ctx.Message.PatientId;
                    ctx.Saga.SlotId         = ctx.Message.SlotId;
                    ctx.Saga.IdempotencyKey = ctx.Message.IdempotencyKey;
                    ctx.Saga.CreatedAt      = DateTimeOffset.UtcNow;
                })
                .TransitionTo(Submitted)
                .Activity(x => x.OfType<VerifyPatientActivity>())
                .Activity(x => x.OfType<LockSlotActivity>())
                .Activity(x => x.OfType<PersistAppointmentActivity>())
                .RespondAsync(ctx => Task.FromResult(new V1_BookingCompletedEvent(
                    ctx.Saga.CorrelationId,
                    ctx.Saga.AppointmentId!.Value,
                    DateTimeOffset.UtcNow)))
                .TransitionTo(Completed)
                .Catch<Exception>(ex => ex
                    .Then(ctx => ctx.Saga.FailureReason = ctx.Exception.Message)
                    .RespondAsync(ctx => Task.FromResult(new V1_BookingFailedEvent(
                        ctx.Saga.CorrelationId,
                        ctx.Saga.FailureReason ?? "Booking failed",
                        DateTimeOffset.UtcNow
                    )))
                    .TransitionTo(Failed)
                )
        );

        SetCompletedWhenFinalized();
    }
}
