using Grpc.Core;
using HealthBooking.Contracts.Grpc;
using NotificationService.Application.Interfaces;

namespace NotificationService.Infrastructure.Clients;

/*
 * NotificationPatientGrpcClient
 * -----------------------------
 * IPatientEmailClient implementation that calls PatientService via gRPC to
 * retrieve the patient's contact email for notification delivery.
 *
 * WHO USES IT:
 *   AppointmentBookedConsumer, AppointmentCancelledConsumer,
 *   AppointmentRescheduledConsumer.
 *
 * WHY THIS APPROACH:
 *   Returns null on NotFound so consumers can fall back to a placeholder email
 *   rather than crashing; non-critical service dependency handled gracefully.
 */
public sealed class NotificationPatientGrpcClient(PatientGrpc.PatientGrpcClient grpcClient)
    : IPatientEmailClient
{
    public async Task<string?> GetPatientEmailAsync(Guid patientId, CancellationToken ct = default)
    {
        try
        {
            var response = await grpcClient.GetPatientByIdAsync(
                new GetPatientByIdRequest { PatientId = patientId.ToString() },
                cancellationToken: ct);

            return string.IsNullOrWhiteSpace(response.ContactEmail) ? null : response.ContactEmail;
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return null;
        }
    }
}
