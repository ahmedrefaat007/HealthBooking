using HealthBooking.SharedKernel.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using PatientService.Application.Interfaces;

namespace PatientService.Infrastructure.Clients;

public sealed class IdentityProvisioningClient(IHttpClientFactory factory)
    : IIdentityProvisioningService
{
    private readonly HttpClient _http = factory.CreateClient("identity-provisioning");

    public async Task ProvisionUserAsync(
        Guid patientId, string email, CancellationToken ct = default)
    {
        // Fire-and-forget provisioning request to IdentityServer
        // In production this would call a dedicated provisioning endpoint
        var payload = new { UserId = patientId, Email = email, Role = "Patient" };
        var json = System.Text.Json.JsonSerializer.Serialize(payload);

        using var content = new StringContent(
            json, System.Text.Encoding.UTF8, "application/json");

        // Swallow non-critical failures — patient record is already saved
        try
        {
            await _http.PostAsync("/internal/provision-user", content, ct);
        }
        catch (HttpRequestException)
        {
            // Identity provisioning failure is non-fatal in dev
            // Production: publish to DLQ for retry
        }
    }
}
