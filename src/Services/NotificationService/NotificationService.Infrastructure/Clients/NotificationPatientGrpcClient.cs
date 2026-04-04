using Grpc.Core;
using HealthBooking.Contracts.Grpc;
using NotificationService.Application.Interfaces;

namespace NotificationService.Infrastructure.Clients;

/// <summary>
/// Fetches the patient's contact e-mail from PatientService via gRPC.
/// Used by notification consumers to replace the placeholder e-mail address.
/// </summary>
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
