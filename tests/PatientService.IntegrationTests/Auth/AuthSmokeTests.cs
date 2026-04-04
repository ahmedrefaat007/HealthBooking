using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace PatientService.IntegrationTests.Auth;

/// <summary>
/// Auth smoke tests validating JWT token issuance from IdentityServer
/// and bearer token enforcement on PatientService endpoints.
/// These tests require the full docker-compose stack to be running.
/// </summary>
[Trait("Category", "Integration")]
public class AuthSmokeTests
{
    private const string IdentityServerBase = "http://localhost:5005";
    private const string PatientServiceBase = "http://localhost:5001";

    private readonly HttpClient _http = new();

    [Fact(Skip = "Requires full docker-compose stack")]
    public async Task PostConnectToken_WithValidClientCredentials_ReturnsJwt()
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"]    = "client_credentials",
            ["client_id"]     = "api-gateway",
            ["client_secret"] = "api-gateway-secret",
            ["scope"]         = "healthbooking-api"
        };

        var response = await _http.PostAsync(
            $"{IdentityServerBase}/connect/token",
            new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.False(string.IsNullOrWhiteSpace(body?.AccessToken));
        Assert.Equal("Bearer", body?.TokenType);
    }

    [Fact(Skip = "Requires full docker-compose stack")]
    public async Task GetPatientMe_WithoutToken_Returns401()
    {
        var response = await _http.GetAsync($"{PatientServiceBase}/api/patients/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(Skip = "Requires full docker-compose stack")]
    public async Task GetPatientMe_WithValidToken_NoPatientRecord_Returns403Or404()
    {
        var token = await GetAccessTokenAsync();
        _http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await _http.GetAsync($"{PatientServiceBase}/api/patients/me");

        // No patient record exists yet for this client — expect 403 or 404
        Assert.True(
            response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound,
            $"Expected 403/404 but got {response.StatusCode}");
    }

    private async Task<string> GetAccessTokenAsync()
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"]    = "client_credentials",
            ["client_id"]     = "api-gateway",
            ["client_secret"] = "api-gateway-secret",
            ["scope"]         = "healthbooking-api"
        };

        var response = await _http.PostAsync(
            $"{IdentityServerBase}/connect/token",
            new FormUrlEncodedContent(form));

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<TokenResponse>();
        return body!.AccessToken!;
    }

    private sealed record TokenResponse(
        string? AccessToken,
        string? TokenType,
        int ExpiresIn);
}
