/*
 * ApiGateway \u2014 Program.cs
 * ------------------------
 * Entry point for the YARP reverse-proxy gateway that sits in front of all
 * internal microservices.
 *
 * RESPONSIBILITIES:
 *   1. JWT Bearer authentication \u2014 validates tokens issued by IdentityServer.
 *   2. CORS \u2014 allows the Angular SPA on localhost:4200 during development.
 *   3. Rate limiting \u2014 sliding-window 300 req/min to protect downstream services.
 *   4. Reverse proxy (YARP) \u2014 routes /api/patients/**, /api/providers/**,
 *      /api/appointments/** to their respective microservices.
 *   5. Telemetry \u2014 OpenTelemetry distributed traces via OTLP.
 *   6. Health checks \u2014 /health/ready pings IdentityServer; /health/live is instant.
 *
 * WHO DEPENDS ON THIS:
 *   The Angular frontend and any external API consumer routes ALL requests
 *   through this gateway.  Internal gRPC traffic bypasses the gateway.
 *
 * WHY YARP:
 *   YARP (Yet Another Reverse Proxy) is the Microsoft-backed, production-grade
 *   reverse proxy library for ASP.NET Core.  Config-driven routing avoids
 *   maintaining hand-written proxy code and integrates natively with the
 *   ASP.NET Core middleware pipeline.
 */
using HealthBooking.ApiGateway.Middleware;
using HealthBooking.SharedKernel.Extensions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Serilog;
using System.Threading.RateLimiting;

/* Bootstrap logger captures startup errors before full Serilog config is applied. */
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

/* Full Serilog setup reading from appsettings.json + enriching log context. */
var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, services, config) =>
    config
        .ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console());

var identityUrl = builder.Configuration["IdentityServer:Authority"]
    ?? "http://identity-server:5005";

/*
 * JWT Bearer Authentication
 * -------------------------
 * Validates Bearer tokens issued by IdentityServer.
 * RequireHttpsMetadata = false is safe here because the gateway runs inside
 * a Docker network where TLS is terminated at the load balancer.
 */
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = identityUrl;
        options.Audience = "healthbooking-api";
        options.RequireHttpsMetadata = false;
    });

builder.Services.AddAuthorization(opts =>
{
    opts.AddPolicy("JwtBearer", policy =>
        policy.RequireAuthenticatedUser());
});

/*
 * CORS \u2014 allow Angular development server
 * -----------------------------------------
 * Allows the Angular SPA (localhost:4200) to make credentialled requests.
 * AllowCredentials() is needed so the browser sends cookies / auth headers.
 */
builder.Services.AddCors(opts =>
    opts.AddPolicy("angular-dev", policy =>
        policy
            .WithOrigins("http://localhost:4200")
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials()));

/*
 * Rate Limiter \u2014 sliding window 300 req/min
 * -------------------------------------------
 * Protects the gateway (and downstream services) from sudden traffic spikes.
 * 300 req/min split across 6 segments = 50 req/10 s, smoothing bursts.
 * QueueLimit = 0 means excess requests are immediately rejected (429).
 */
builder.Services.AddRateLimiter(opts =>
    opts.AddSlidingWindowLimiter("gateway", limiterOpts =>
    {
        limiterOpts.PermitLimit = 300;
        limiterOpts.Window = TimeSpan.FromMinutes(1);
        limiterOpts.SegmentsPerWindow = 6;
        limiterOpts.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiterOpts.QueueLimit = 0;
    }));

/*
 * YARP Reverse Proxy
 * ------------------
 * Loads route-to-cluster configuration from appsettings.json (ReverseProxy section).
 * YARP forwards requests to the correct microservice, preserving headers and
 * providing load balancing, health checks, and request transformations.
 */
builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

/* OpenTelemetry: instruments ASP.NET Core + YARP + HttpClient with OTLP export. */
builder.Services.AddHealthBookingTelemetry("api-gateway", builder.Configuration);

/*
 * Health Checks
 * -------------
 * /health/ready: probes IdentityServer to confirm the gateway can serve traffic.
 * /health/live:  instant 200 OK — used by Docker/Kubernetes liveness probes.
 */
builder.Services
    .AddHealthChecks()
    .AddUrlGroup(new Uri($"{identityUrl}/health/ready"), name: "identity-server");

var app = builder.Build();

/* Middleware pipeline — ORDER IS SIGNIFICANT. */
app.UseSerilogRequestLogging();          /* Log HTTP method + path + status + elapsed. */
app.UseMiddleware<CorrelationIdMiddleware>(); /* Attach / generate X-Correlation-Id. */
app.UseCors("angular-dev");              /* Must be before auth middleware. */
app.UseRateLimiter();                    /* Reject excess requests before auth work. */

app.UseAuthentication();
app.UseAuthorization();

/* Route all matched requests to the YARP proxy. */
app.MapReverseProxy();

app.MapHealthChecks("/health/ready");
app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));

app.Run();
