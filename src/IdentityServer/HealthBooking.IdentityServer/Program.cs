using HealthBooking.IdentityServer;
using HealthBooking.IdentityServer.Data;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using Serilog;
using System.Security.Claims;
using static OpenIddict.Abstractions.OpenIddictConstants;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, services, config) =>
    config
        .ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console());

var connString = builder.Configuration.GetConnectionString("IdentityDb")
    ?? throw new InvalidOperationException("ConnectionStrings:IdentityDb is missing.");

builder.Services.AddDbContext<IdentityDbContext>(opts =>
    opts.UseSqlServer(connString)
        .UseOpenIddict());

// Inline scope → audience mapping used at token issuance for password flow
var scopeToResource = new Dictionary<string, string>(StringComparer.Ordinal)
{
    ["healthbooking-api"]  = "healthbooking-api",
    ["patient:read"]       = "patient-service",
    ["patient:write"]      = "patient-service",
    ["provider:read"]      = "provider-service",
    ["provider:write"]     = "provider-service",
    ["appointment:read"]   = "appointment-service",
    ["appointment:write"]  = "appointment-service"
};

// Dev-only user store — replace with a real user service in production
var devUsers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    ["admin@healthbooking.com"]   = "Admin123!",
    ["patient@healthbooking.com"] = "Patient123!"
};

builder.Services.AddOpenIddict()
    .AddCore(options =>
    {
        options.UseEntityFrameworkCore()
               .UseDbContext<IdentityDbContext>();
    })
    .AddServer(options =>
    {
        options.SetTokenEndpointUris("/connect/token");

        options.AllowClientCredentialsFlow();
        options.AllowPasswordFlow();
        options.AllowRefreshTokenFlow();

        options.RegisterScopes(
            "openid", "profile", "email",
            "healthbooking-api",
            "patient:read",  "patient:write",
            "provider:read", "provider:write",
            "appointment:read", "appointment:write");

        // Custom event handler: handle ALL grant types
        // (registering a custom handler replaces the built-in one for every flow)
        options.AddEventHandler<OpenIddictServerEvents.HandleTokenRequestContext>(builder =>
        {
            builder.UseInlineHandler(context =>
            {
                ClaimsIdentity identity;

                if (context.Request.IsClientCredentialsGrantType())
                {
                    // Service-to-service: subject = client_id
                    identity = new ClaimsIdentity("Bearer");
                    identity.SetClaim(Claims.Subject, context.Request.ClientId!);
                    identity.SetClaim(Claims.Name,    context.Request.ClientId!);
                }
                else if (context.Request.IsPasswordGrantType())
                {
                    if (!devUsers.TryGetValue(context.Request.Username!, out var pwd)
                        || pwd != context.Request.Password)
                    {
                        context.Reject(
                            error: Errors.InvalidGrant,
                            description: "Invalid username or password.");
                        return default;
                    }

                    // If caller supplies patient_id, use it as the subject (so booking saga can Guid.Parse it).
                    var subjectId = context.Request["patient_id"]?.ToString()
                                   ?? context.Request.Username!;

                    identity = new ClaimsIdentity("Bearer");
                    identity.SetClaim(Claims.Subject, subjectId);
                    identity.SetClaim(Claims.Name,    context.Request.Username!);
                    identity.SetClaim(Claims.Email,   context.Request.Username!);
                }
                else
                {
                    context.Reject(
                        error: Errors.UnsupportedGrantType,
                        description: "The specified grant type is not supported.");
                    return default;
                }

                var principal = new ClaimsPrincipal(identity);
                principal.SetScopes(context.Request.GetScopes());

                var resources = context.Request.GetScopes()
                    .Select(s => scopeToResource.TryGetValue(s, out var r) ? r : null)
                    .Where(r => r is not null)
                    .Distinct()
                    .Cast<string>()
                    .ToList();
                principal.SetResources(resources);

                context.SignIn(principal);
                return default;
            });
        });

        options.AddDevelopmentEncryptionCertificate()
               .AddDevelopmentSigningCertificate();

        // Disable token encryption so backend services can validate with standard JWT Bearer
        options.DisableAccessTokenEncryption();

        // Allow HTTP in development (no TLS termination locally)
        options.UseAspNetCore()
               .DisableTransportSecurityRequirement();
    })
    .AddValidation(options =>
    {
        options.UseLocalServer();
        options.UseAspNetCore();
    });

builder.Services.AddAuthentication();
builder.Services.AddAuthorization();

builder.Services
    .AddHealthChecks()
    .AddSqlServer(connString, name: "identity-db");

var app = builder.Build();

await SeedData.InitializeAsync(app.Services);

app.UseSerilogRequestLogging();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health/ready");
app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));

app.Run();
