using AppointmentService.Application.Commands.BookAppointment;
using AppointmentService.Application.Interfaces;
using HealthBooking.Contracts.Appointments.V1;
using MassTransit;

namespace AppointmentService.Application.Saga.Activities;

/// <summary>
/// Step 2: Locks the availability slot in ProviderService via gRPC.
/// Sets saga.SlotWasLocked = true on success so the Faulted handler knows to compensate.
///
/// Faulted: if the slot was already locked (i.e., a LATER activity threw),
/// releases it via gRPC to roll back the side-effect.
/// </summary>
public sealed class LockSlotActivity(IProviderSlotGrpcClient slotClient)
    : IStateMachineActivity<BookingState, V1_InitiateBookingCommand>
{
    public async Task Execute(
        BehaviorContext<BookingState, V1_InitiateBookingCommand> context,
        IBehavior<BookingState, V1_InitiateBookingCommand>       next)
    {
        // Use the saga CorrelationId as the reference appointment ID for the lock
        var locked = await slotClient.LockSlotAsync(
            context.Saga.SlotId, context.Saga.CorrelationId, context.CancellationToken);

        if (!locked)
            throw new SlotConflictException(
                $"Slot {context.Saga.SlotId} is no longer available.");

        context.Saga.SlotWasLocked = true;

        await next.Execute(context);
    }

    public async Task Faulted<TException>(
        BehaviorExceptionContext<BookingState, V1_InitiateBookingCommand, TException> context,
        IBehavior<BookingState, V1_InitiateBookingCommand>                            next)
        where TException : Exception
    {
        // Compensation: release the slot if we locked it before the failure occurred
        if (context.Saga.SlotWasLocked)
        {
            try
            {
                await slotClient.ReleaseSlotAsync(context.Saga.SlotId, context.CancellationToken);
                context.Saga.SlotWasLocked = false;
            }
            catch
            {
                // Swallow release errors here; a background reconciler can handle orphaned locks
            }
        }

        await next.Faulted(context);
    }

    public void Accept(StateMachineVisitor visitor) => visitor.Visit(this);
    public void Probe(ProbeContext context)         => context.CreateScope("lock-slot");
}
