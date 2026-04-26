using Grpc.Core;
using HealthBooking.Contracts.Grpc;
using MediatR;
using PatientService.Application.Queries.GetPatientById;

namespace PatientService.API.Grpc;

/*
 * PatientGrpcService
 * ------------------
 * gRPC service implementation for inter-service patient lookups.
 *
 * WHO USES IT:
 *   AppointmentService: VerifyPatientActivity calls GetPatientById to confirm
 *     a patient exists and fetch their display name before booking.
 *   NotificationService: NotificationPatientGrpcClient calls GetPatientById to
 *     resolve the patient's contact email for sending appointment notifications.
 *
 * WHY THIS APPROACH:
 *   gRPC is used for synchronous internal service-to-service communication
 *   (strongly-typed, binary-efficient protobuf) while the public REST API handles
 *   browser traffic.  MediatR integration re-uses the same query handlers and
 *   caching layer as the REST endpoints.
 */
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
            PatientId = patient.PatientId.ToString(),
            FullName = $"{patient.FirstName} {patient.LastName}",
            ContactEmail = patient.ContactEmail
        };
    }
}

