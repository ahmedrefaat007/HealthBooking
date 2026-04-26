using HealthBooking.SharedKernel.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using PatientService.Application.Interfaces;

namespace PatientService.Infrastructure.Clients;

/*
 * IdentityProvisioningClient
 * --------------------------
 * HTTP client that calls IdentityServer's internal /provision-user endpoint
 * to create an OpenIddict user account after patient registration.
 *
 * WHO USES IT:
 *   RegisterPatientCommandHandler: called after the patient DB record is saved.
 *
 * WHY THIS APPROACH:
 *   Fire-and-forget pattern with swallowed HttpRequestException keeps patient
 *   registration atomic (patient saved regardless of identity service health).
 *   In production this should publish a retryable message to a DLQ instead of
 *   silently failing.  The named HttpClient ("identity-provisioning") picks up
 *   the Polly resilience pipeline registered in Program.cs.
 */
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
