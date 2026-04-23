using HealthBooking.ApiGateway.Middleware;
using HealthBooking.SharedKernel.Extensions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Serilog;
using System.Threading.RateLimiting;

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

var identityUrl = builder.Configuration["IdentityServer:Authority"]
    ?? "http://identity-server:5005";

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

// CORS — allow Angular development server
builder.Services.AddCors(opts =>
    opts.AddPolicy("angular-dev", policy =>
        policy
            .WithOrigins("http://localhost:4200")
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials()));

// Rate limiter — sliding window 300 req/min
builder.Services.AddRateLimiter(opts =>
    opts.AddSlidingWindowLimiter("gateway", limiterOpts =>
    {
        limiterOpts.PermitLimit = 300;
        limiterOpts.Window = TimeSpan.FromMinutes(1);
        limiterOpts.SegmentsPerWindow = 6;
        limiterOpts.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiterOpts.QueueLimit = 0;
    }));

builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

builder.Services.AddHealthBookingTelemetry("api-gateway", builder.Configuration);

builder.Services
    .AddHealthChecks()
    .AddUrlGroup(new Uri($"{identityUrl}/health/ready"), name: "identity-server");

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseCors("angular-dev");
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapReverseProxy();

app.MapHealthChecks("/health/ready");
app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));

app.Run();
