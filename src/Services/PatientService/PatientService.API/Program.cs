using FluentValidation;
using HealthBooking.SharedKernel.Behaviors;
using HealthBooking.SharedKernel.Extensions;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PatientService.API.Endpoints;
using PatientService.API.Grpc;
using PatientService.Application.Commands.RegisterPatient;
using PatientService.Application.Interfaces;
using PatientService.Domain.Entities;
using PatientService.Infrastructure.Clients;
using PatientService.Infrastructure.Persistence;
using PatientService.Infrastructure.Persistence.Interceptors;
using PatientService.Infrastructure.Persistence.Repositories;
using PatientService.Infrastructure.Services;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) =>
    cfg.ReadFrom.Configuration(ctx.Configuration)
       .Enrich.FromLogContext()
       .Destructure.ByTransforming<Patient>(p =>
           new { p.Id, FirstName = "***", LastName = "***", ContactEmail = "***", PhoneNumber = "***" })
       .WriteTo.Console());

// ── Database ──────────────────────────────────────────────────────────────
builder.Services.AddDbContext<PatientDbContext>(opts =>
    opts.UseSqlServer(
        builder.Configuration.GetConnectionString("PatientDb"),
        sql => sql.MigrationsAssembly(typeof(PatientDbContext).Assembly.FullName)));

builder.Services.AddScoped<AuditInterceptor>();
builder.Services.AddScoped<OutboxPublishingInterceptor>();

// ── Repositories & services ───────────────────────────────────────────────
builder.Services.AddScoped<IPatientRepository, PatientRepository>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

// ── Identity provisioning HTTP client ────────────────────────────────────
builder.Services.AddHttpClient("identity-provisioning", c =>
{
    c.BaseAddress = new Uri(
        builder.Configuration["IdentityServer:BaseUrl"] ?? "http://identity-server:5005");
})
.AddHealthBookingResiliencePipeline("identity-provisioning");
builder.Services.AddScoped<IIdentityProvisioningService, IdentityProvisioningClient>();

// ── MediatR + validation behavior ────────────────────────────────────────
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(
        typeof(RegisterPatientCommand).Assembly);
    cfg.AddBehavior(typeof(IPipelineBehavior<,>),
                    typeof(ValidationBehavior<,>));
});
builder.Services.AddValidatorsFromAssembly(
    typeof(RegisterPatientCommand).Assembly);

// ── JWT Bearer ────────────────────────────────────────────────────────────
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        opts.Authority = builder.Configuration["IdentityServer:BaseUrl"];
        opts.RequireHttpsMetadata = false;
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateAudience = true,
            ValidAudiences = ["patient-service"]
        };
    });
builder.Services.AddAuthorization();

// ── gRPC ──────────────────────────────────────────────────────────────────
builder.Services.AddGrpc();

// ── OpenTelemetry ────────────────────────────────────────────────────────
builder.Services.AddHealthBookingTelemetry("patient-service", builder.Configuration);

// ── Health checks ─────────────────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddSqlServer(
        builder.Configuration.GetConnectionString("PatientDb")!,
        name: "sql-patient",
        tags: ["ready"]);

// ── Build ─────────────────────────────────────────────────────────────────
var app = builder.Build();

// Auto-migrate on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PatientDbContext>();
    await db.Database.MigrateAsync();
}

app.UseSerilogRequestLogging();
app.UseAuthentication();
app.UseAuthorization();

app.MapPatientEndpoints();
app.MapGrpcService<PatientGrpcService>();

app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = hc => hc.Tags.Contains("ready")
});

app.Run();
