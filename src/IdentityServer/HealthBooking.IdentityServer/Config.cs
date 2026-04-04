using Duende.IdentityServer.Models;

namespace HealthBooking.IdentityServer;

public static class Config
{
    public static IEnumerable<IdentityResource> IdentityResources =>
    [
        new IdentityResources.OpenId(),
        new IdentityResources.Profile(),
        new IdentityResources.Email()
    ];

    public static IEnumerable<ApiScope> ApiScopes =>
    [
        new ApiScope("healthbooking-api", "HealthBooking API"),
        new ApiScope("patient:read",  "Read patient data"),
        new ApiScope("patient:write", "Write patient data"),
        new ApiScope("provider:read",  "Read provider data"),
        new ApiScope("provider:write", "Write provider data"),
        new ApiScope("appointment:read",  "Read appointment data"),
        new ApiScope("appointment:write", "Write appointment data")
    ];

    public static IEnumerable<ApiResource> ApiResources =>
    [
        new ApiResource("healthbooking-api", "HealthBooking API")
        {
            Scopes =
            {
                "healthbooking-api",
                "patient:read",  "patient:write",
                "provider:read", "provider:write",
                "appointment:read", "appointment:write"
            }
        }
    ];

    public static IEnumerable<Client> Clients =>
    [
        new Client
        {
            ClientId     = "api-gateway",
            ClientName   = "API Gateway Client",
            ClientSecrets = { new Secret("api-gateway-secret".Sha256()) },
            AllowedGrantTypes  = GrantTypes.ClientCredentials,
            AllowedScopes      = { "healthbooking-api" }
        },
        new Client
        {
            ClientId     = "patient-spa",
            ClientName   = "Patient SPA",
            ClientSecrets = { new Secret("patient-spa-secret".Sha256()) },
            AllowedGrantTypes = GrantTypes.ResourceOwnerPasswordAndClientCredentials,
            AllowedScopes     =
            {
                "openid", "profile", "email",
                "healthbooking-api",
                "patient:read", "patient:write",
                "appointment:read", "appointment:write"
            },
            AllowOfflineAccess = true
        },
        new Client
        {
            ClientId     = "admin-client",
            ClientName   = "Admin Client",
            ClientSecrets = { new Secret("admin-client-secret".Sha256()) },
            AllowedGrantTypes = GrantTypes.ResourceOwnerPasswordAndClientCredentials,
            AllowedScopes     =
            {
                "openid", "profile", "email",
                "healthbooking-api",
                "patient:read", "patient:write",
                "provider:read", "provider:write",
                "appointment:read", "appointment:write"
            },
            AllowOfflineAccess = true
        }
    ];
}
