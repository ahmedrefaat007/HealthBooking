using AppointmentService.Application.Interfaces;
using HealthBooking.Contracts.Appointments.V1;
using MassTransit;

namespace AppointmentService.Application.Saga.Activities;

/*
 * VerifyPatientActivity
 * ---------------------
 * Saga step 1: Calls PatientService via gRPC to confirm the patient exists
 * and fetches the patient's full name for storage on the Appointment entity.
 *
 * WHO USES IT:
 *   BookingStateMachine: first activity in the Initially handler chain.
 *
 * WHY THIS APPROACH:
 *   Validating the patient before locking any slot prevents wasting a slot lock
 *   on a non-existent patient.  Being read-only, this activity has no compensation.
 */
public sealed class VerifyPatientActivity(IPatientGrpcClient patientClient)
    : IStateMachineActivity<BookingState, V1_InitiateBookingCommand>
{
    public async Task Execute(
        BehaviorContext<BookingState, V1_InitiateBookingCommand> context,
        IBehavior<BookingState, V1_InitiateBookingCommand> next)
    {
        var patient = await patientClient.GetPatientByIdAsync(
            context.Saga.PatientId, context.CancellationToken)
            ?? throw new InvalidOperationException(
                $"Patient {context.Saga.PatientId} not found.");

        context.Saga.PatientName = patient.FullName;

        await next.Execute(context);
    }

    public async Task Faulted<TException>(
        BehaviorExceptionContext<BookingState, V1_InitiateBookingCommand, TException> context,
        IBehavior<BookingState, V1_InitiateBookingCommand> next)
        where TException : Exception
    {
        await next.Faulted(context); // nothing to compensate
    }

    public void Accept(StateMachineVisitor visitor) => visitor.Visit(this);
    public void Probe(ProbeContext context) => context.CreateScope("verify-patient");
}
