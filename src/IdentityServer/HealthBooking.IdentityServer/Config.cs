using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace HealthBooking.IdentityServer;

/*
 * Config
 * ------
 * Static configuration catalogue for all OpenIddict scopes and OAuth2 clients.
 *
 * WHO USES IT:
 *   SeedData.InitializeAsync() — iterates Scopes and Clients to upsert
 *   records into the OpenIddict tables on every startup.
 *
 * WHY THIS APPROACH:
 *   Declarative configuration in code (rather than a database admin UI) makes
 *   client and scope definitions reviewable in version control and reproducible
 *   across dev/staging/production environments via seed-on-start.
 */
public static class Config
{
    // ── Scopes → Audiences mapping ————————————————————————————————————————————
    // Each scope defines which API resource (audience) it grants access to.
    /*
     * Scopes
     * ------
     * Maps each named OAuth2 scope to the API resource (JWT audience) it unlocks.
     * Backend services validate JWTs against these audience values so a token
     * issued for patient:read cannot access provider-service endpoints.
     */
    public static IEnumerable<OpenIddictScopeDescriptor> Scopes =>
    [
        new OpenIddictScopeDescriptor
        {
            Name      = "healthbooking-api",
            Resources = { "healthbooking-api" }
        },
        new OpenIddictScopeDescriptor
        {
            Name      = "patient:read",
            Resources = { "patient-service" }
        },
        new OpenIddictScopeDescriptor
        {
            Name      = "patient:write",
            Resources = { "patient-service" }
        },
        new OpenIddictScopeDescriptor
        {
            Name      = "provider:read",
            Resources = { "provider-service" }
        },
        new OpenIddictScopeDescriptor
        {
            Name      = "provider:write",
            Resources = { "provider-service" }
        },
        new OpenIddictScopeDescriptor
        {
            Name      = "appointment:read",
            Resources = { "appointment-service" }
        },
        new OpenIddictScopeDescriptor
        {
            Name      = "appointment:write",
            Resources = { "appointment-service" }
        }
    ];

    // ── Clients ——————————————————————————————————————————————————————
    /*
     * Clients
     * -------
     * Three registered OAuth2 clients with distinct grant types and scope sets:
     *  - api-gateway : client_credentials (M2M, for reverse proxy token introspection).
     *  - patient-spa : password + refresh_token (end-user Angular SPA).
     *  - admin-client: password + client_credentials + refresh_token (full access).
     *
     * NOTE: Secrets are plain-text here for local development only.
     *       In production these should come from a secret manager (Key Vault, etc.).
     */
    public static IEnumerable<OpenIddictApplicationDescriptor> Clients =>
    [
        // API Gateway — machine-to-machine
        new OpenIddictApplicationDescriptor
        {
            ClientId     = "api-gateway",
            ClientSecret = "api-gateway-secret",
            DisplayName  = "API Gateway",
            Permissions  =
            {
                Permissions.Endpoints.Token,
                Permissions.GrantTypes.ClientCredentials,
                Permissions.Prefixes.Scope + "healthbooking-api"
            }
        },

        // Patient SPA — password flow for end-users
        new OpenIddictApplicationDescriptor
        {
            ClientId     = "patient-spa",
            ClientSecret = "patient-spa-secret",
            DisplayName  = "Patient SPA",
            Permissions  =
            {
                Permissions.Endpoints.Token,
                Permissions.GrantTypes.Password,
                Permissions.GrantTypes.RefreshToken,
                Permissions.Prefixes.Scope + "openid",
                Permissions.Prefixes.Scope + "profile",
                Permissions.Prefixes.Scope + "email",
                Permissions.Prefixes.Scope + "offline_access",
                Permissions.Prefixes.Scope + "healthbooking-api",
                Permissions.Prefixes.Scope + "patient:read",
                Permissions.Prefixes.Scope + "patient:write",
                Permissions.Prefixes.Scope + "appointment:read",
                Permissions.Prefixes.Scope + "appointment:write"
            }
        },

        // Admin client — full access password flow
        new OpenIddictApplicationDescriptor
        {
            ClientId     = "admin-client",
            ClientSecret = "admin-client-secret",
            DisplayName  = "Admin Client",
            Permissions  =
            {
                Permissions.Endpoints.Token,
                Permissions.GrantTypes.Password,
                Permissions.GrantTypes.ClientCredentials,
                Permissions.GrantTypes.RefreshToken,
                Permissions.Prefixes.Scope + "openid",
                Permissions.Prefixes.Scope + "profile",
                Permissions.Prefixes.Scope + "email",
                Permissions.Prefixes.Scope + "offline_access",
                Permissions.Prefixes.Scope + "healthbooking-api",
                Permissions.Prefixes.Scope + "patient:read",
                Permissions.Prefixes.Scope + "patient:write",
                Permissions.Prefixes.Scope + "provider:read",
                Permissions.Prefixes.Scope + "provider:write",
                Permissions.Prefixes.Scope + "appointment:read",
                Permissions.Prefixes.Scope + "appointment:write"
            }
        }
    ];
}
    [
        new OpenIddictScopeDescriptor
        {
            Name      = "healthbooking-api",
            Resources = { "healthbooking-api" }
        },
        new OpenIddictScopeDescriptor
        {
            Name      = "patient:read",
            Resources = { "patient-service" }
        },
        new OpenIddictScopeDescriptor
        {
            Name      = "patient:write",
            Resources = { "patient-service" }
        },
        new OpenIddictScopeDescriptor
        {
            Name      = "provider:read",
            Resources = { "provider-service" }
        },
        new OpenIddictScopeDescriptor
        {
            Name      = "provider:write",
            Resources = { "provider-service" }
        },
        new OpenIddictScopeDescriptor
        {
            Name      = "appointment:read",
            Resources = { "appointment-service" }
        },
        new OpenIddictScopeDescriptor
        {
            Name      = "appointment:write",
            Resources = { "appointment-service" }
        }
    ];

    // ── Clients ───────────────────────────────────────────────────────────────
    public static IEnumerable<OpenIddictApplicationDescriptor> Clients =>
    [
        // API Gateway — machine-to-machine
        new OpenIddictApplicationDescriptor
        {
            ClientId     = "api-gateway",
            ClientSecret = "api-gateway-secret",
            DisplayName  = "API Gateway",
            Permissions  =
            {
                Permissions.Endpoints.Token,
                Permissions.GrantTypes.ClientCredentials,
                Permissions.Prefixes.Scope + "healthbooking-api"
            }
        },

        // Patient SPA — password flow for end-users
        new OpenIddictApplicationDescriptor
        {
            ClientId     = "patient-spa",
            ClientSecret = "patient-spa-secret",
            DisplayName  = "Patient SPA",
            Permissions  =
            {
                Permissions.Endpoints.Token,
                Permissions.GrantTypes.Password,
                Permissions.GrantTypes.RefreshToken,
                Permissions.Prefixes.Scope + "openid",
                Permissions.Prefixes.Scope + "profile",
                Permissions.Prefixes.Scope + "email",
                Permissions.Prefixes.Scope + "offline_access",
                Permissions.Prefixes.Scope + "healthbooking-api",
                Permissions.Prefixes.Scope + "patient:read",
                Permissions.Prefixes.Scope + "patient:write",
                Permissions.Prefixes.Scope + "appointment:read",
                Permissions.Prefixes.Scope + "appointment:write"
            }
        },

        // Admin client — full access password flow
        new OpenIddictApplicationDescriptor
        {
            ClientId     = "admin-client",
            ClientSecret = "admin-client-secret",
            DisplayName  = "Admin Client",
            Permissions  =
            {
                Permissions.Endpoints.Token,
                Permissions.GrantTypes.Password,
                Permissions.GrantTypes.ClientCredentials,
                Permissions.GrantTypes.RefreshToken,
                Permissions.Prefixes.Scope + "openid",
                Permissions.Prefixes.Scope + "profile",
                Permissions.Prefixes.Scope + "email",
                Permissions.Prefixes.Scope + "offline_access",
                Permissions.Prefixes.Scope + "healthbooking-api",
                Permissions.Prefixes.Scope + "patient:read",
                Permissions.Prefixes.Scope + "patient:write",
                Permissions.Prefixes.Scope + "provider:read",
                Permissions.Prefixes.Scope + "provider:write",
                Permissions.Prefixes.Scope + "appointment:read",
                Permissions.Prefixes.Scope + "appointment:write"
            }
        }
    ];
}
