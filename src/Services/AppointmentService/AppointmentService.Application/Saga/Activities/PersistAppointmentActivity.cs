using AppointmentService.Application.Interfaces;
using AppointmentService.Domain.Entities;
using HealthBooking.Contracts.Appointments.V1;
using MassTransit;

namespace AppointmentService.Application.Saga.Activities;

/// <summary>
/// Step 3: Persists the Appointment entity and the idempotency key atomically.
/// If an idempotency-key clash is detected (duplicate request), returns the
/// existing appointment ID instead of creating a new one.
///
/// Faulted: nothing to compensate here — the slot will be released by
/// LockSlotActivity.Faulted which runs after this in the rollback chain.
/// </summary>
public sealed class PersistAppointmentActivity(
    IAppointmentRepository  appointments,
    IIdempotencyRepository  idempotency,
    IProviderSlotGrpcClient slotClient)
    : IStateMachineActivity<BookingState, V1_InitiateBookingCommand>
{
    public async Task Execute(
        BehaviorContext<BookingState, V1_InitiateBookingCommand> context,
        IBehavior<BookingState, V1_InitiateBookingCommand>       next)
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
            Key           = context.Saga.IdempotencyKey,
            AppointmentId = appointment.Id
        };
        await idempotency.AddAsync(key, context.CancellationToken);
        await appointments.SaveChangesAsync(context.CancellationToken);

        context.Saga.AppointmentId = appointment.Id;

        await next.Execute(context);
    }

    public async Task Faulted<TException>(
        BehaviorExceptionContext<BookingState, V1_InitiateBookingCommand, TException> context,
        IBehavior<BookingState, V1_InitiateBookingCommand>                            next)
        where TException : Exception
    {
        await next.Faulted(context); // slot compensation handled by LockSlotActivity.Faulted
    }

    public void Accept(StateMachineVisitor visitor) => visitor.Visit(this);
    public void Probe(ProbeContext context)         => context.CreateScope("persist-appointment");
}
