using Grpc.Core;
using HealthBooking.Contracts.Grpc;
using MediatR;
using PatientService.Application.Queries.GetPatientById;

namespace PatientService.API.Grpc;

public sealed class PatientGrpcService(ISender sender) : PatientGrpc.PatientGrpcBase
{
    public override async Task<PatientResponse> GetPatientById(
        GetPatientByIdRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.PatientId, out var id))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid patient id"));

        var patient = await sender.Send(new GetPatientByIdQuery(id), context.CancellationToken);

        if (patient is null)
            throw new RpcException(new Status(StatusCode.NotFound, "Patient not found"));

        return new PatientResponse
        {
            PatientId    = patient.PatientId.ToString(),
            FullName     = $"{patient.FirstName} {patient.LastName}",
            ContactEmail = patient.ContactEmail
        };
    }
}

