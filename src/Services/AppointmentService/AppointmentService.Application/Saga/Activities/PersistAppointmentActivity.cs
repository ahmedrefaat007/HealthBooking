using AppointmentService.Application.Interfaces;
using AppointmentService.Domain.Entities;
using HealthBooking.Contracts.Appointments.V1;
using MassTransit;

namespace AppointmentService.Application.Saga.Activities;

/*
 * PersistAppointmentActivity
 * --------------------------
 * Saga step 3: Persists the Appointment entity and idempotency key atomically.
 *
 * WHO USES IT:
 *   BookingStateMachine: third and final activity in the Initially handler chain.
 *
 * IDEMPOTENCY:
 *   Checks IIdempotencyRepository first.  If the key already exists, the existing
 *   AppointmentId is re-used and no new entity is created.
 *
 * COMPENSATION (Faulted):
 *   No compensation here.  If persist fails before the entity is written, there is
 *   nothing to roll back.  Slot release is handled by LockSlotActivity.Faulted.
 */
public sealed class PersistAppointmentActivity(
    IAppointmentRepository appointments,
    IIdempotencyRepository idempotency,
    IProviderSlotGrpcClient slotClient)
    : IStateMachineActivity<BookingState, V1_InitiateBookingCommand>
{
    public async Task Execute(
        BehaviorContext<BookingState, V1_InitiateBookingCommand> context,
        IBehavior<BookingState, V1_InitiateBookingCommand> next)
    {
        // Idempotency: return existing appointment if key already used
        var existing = await idempotency.FindAsync(
            context.Saga.IdempotencyKey, context.CancellationToken);

        if (existing is not null)
        {
            context.Saga.AppointmentId = existing.AppointmentId;
            await next.Execute(context);
            return;
        }

        // Fetch slot start time so we can store ScheduledStartUtc on the appointment
        var slotInfo = await slotClient.GetSlotByIdAsync(context.Saga.SlotId, context.CancellationToken);
        var scheduledStart = slotInfo?.StartTimeUtc ?? DateTimeOffset.UtcNow;

        var appointment = Appointment.Book(
            context.Saga.PatientId,
            context.Saga.SlotId,
            context.Saga.PatientName!,
            scheduledStart);

        await appointments.AddAsync(appointment, context.CancellationToken);

        var key = new BookingIdempotencyKey
        {
            Key = context.Saga.IdempotencyKey,
            AppointmentId = appointment.Id
        };
        await idempotency.AddAsync(key, context.CancellationToken);
        await appointments.SaveChangesAsync(context.CancellationToken);

        context.Saga.AppointmentId = appointment.Id;

        await next.Execute(context);
    }

    public async Task Faulted<TException>(
        BehaviorExceptionContext<BookingState, V1_InitiateBookingCommand, TException> context,
        IBehavior<BookingState, V1_InitiateBookingCommand> next)
        where TException : Exception
    {
        await next.Faulted(context); // slot compensation handled by LockSlotActivity.Faulted
    }

    public void Accept(StateMachineVisitor visitor) => visitor.Visit(this);
    public void Probe(ProbeContext context) => context.CreateScope("persist-appointment");
}
