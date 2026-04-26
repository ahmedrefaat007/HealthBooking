using AppointmentService.Application.Saga.Activities;
using HealthBooking.Contracts.Appointments.V1;
using MassTransit;

namespace AppointmentService.Application.Saga;

/*
 * BookingStateMachine
 * -------------------
 * MassTransit saga state machine orchestrating the three-step booking flow.
 *
 * FLOW (on V1_InitiateBookingCommand):
 *   1. VerifyPatientActivity  — confirms patient exists via gRPC (no compensation).
 *   2. LockSlotActivity       — locks the slot in ProviderService via gRPC;
 *                                Faulted releases the slot if it was locked.
 *   3. PersistAppointmentActivity — saves Appointment + idempotency key;
 *                                    Faulted delegates slot release to LockSlotActivity.
 *
 *   Success → responds V1_BookingCompletedEvent, transitions to Completed.
 *   Failure → responds V1_BookingFailedEvent,    transitions to Failed.
 *
 * WHO USES IT:
 *   AppointmentsEndpoints: sends V1_InitiateBookingCommand via IRequestClient
 *   and awaits V1_BookingCompletedEvent or V1_BookingFailedEvent.
 *
 * WHY SAGA PATTERN:
 *   Distributed transactions across PatientService, ProviderService, and
 *   AppointmentService cannot use a 2PC.  The saga provides compensatable
 *   choreography with a single coordinator (this machine) and built-in
 *   persistence for crash recovery.
 */
public sealed class BookingStateMachine : MassTransitStateMachine<BookingState>
{
    public State Submitted { get; private set; } = null!;
    public State Completed { get; private set; } = null!;
    public State Failed { get; private set; } = null!;

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
                    ctx.Saga.PatientId = ctx.Message.PatientId;
                    ctx.Saga.SlotId = ctx.Message.SlotId;
                    ctx.Saga.IdempotencyKey = ctx.Message.IdempotencyKey;
                    ctx.Saga.CreatedAt = DateTimeOffset.UtcNow;
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
