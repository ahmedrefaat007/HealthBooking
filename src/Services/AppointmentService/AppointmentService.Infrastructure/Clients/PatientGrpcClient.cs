using AppointmentService.Application.Interfaces;
using Grpc.Core;
using HealthBooking.Contracts.Grpc;

namespace AppointmentService.Infrastructure.Clients;

public sealed class PatientGrpcClient(PatientGrpc.PatientGrpcClient grpcClient)
    : IPatientGrpcClient
{
    public async Task<PatientInfo?> GetPatientByIdAsync(
        Guid patientId, CancellationToken ct = default)
    {
        try
        {
            var response = await grpcClient.GetPatientByIdAsync(
                new GetPatientByIdRequest { PatientId = patientId.ToString() },
                cancellationToken: ct);

            return new PatientInfo(
                Guid.Parse(response.PatientId),
                response.FullName,
                response.ContactEmail);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return null;
        }
    }
}
