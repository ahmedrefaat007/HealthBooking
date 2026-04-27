using MassTransit;
using Microsoft.EntityFrameworkCore;
using NotificationService.Application.Consumers;
using NotificationService.Application.Interfaces;
using NotificationService.Domain.Entities;
using NotificationService.Infrastructure.Clients;
using NotificationService.Infrastructure.Email;
using NotificationService.Infrastructure.Persistence;
using NotificationService.Infrastructure.Persistence.Interceptors;
using NotificationService.Infrastructure.Persistence.Repositories;
using NotificationService.Infrastructure.Services;
using HealthBooking.Contracts.Grpc;
using HealthBooking.SharedKernel.Extensions;
using Serilog;

/*
 * NotificationService / Program.cs
 * ---------------------------------
 * Bootstraps the NotificationService microservice.
 *
 * WHO USES IT:
 *   .NET runtime entry point; called by Docker Compose or the dotnet CLI.
 *
 * WHAT IS REGISTERED:
 *   - SQL Server DbContext (NotificationDbContext) with AuditInterceptor.
 *   - NotificationLogRepository, LoggingEmailService (dev stub).
 *   - gRPC client for PatientService to resolve patient emails (with Polly resilience).
 *   - MassTransit consumers: AppointmentBooked, AppointmentCancelled,
 *     AppointmentRescheduled — all idempotent.
 *   - OpenTelemetry tracing exported to Jaeger via OTLP.
 *   - Health checks: SQL Server + RabbitMQ.
 *   - PII redaction: RecipientEmail masked in Serilog destructuring.
 */
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) =>
    cfg.ReadFrom.Configuration(ctx.Configuration)
       .Enrich.FromLogContext()
       .Destructure.ByTransforming<NotificationLog>(n =>
           new { n.Id, n.CorrelationId, n.EventType, RecipientEmail = "***" })
       .WriteTo.Console());

// ── Database ──────────────────────────────────────────────────────────────
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<AuditInterceptor>();
builder.Services.AddDbContext<NotificationDbContext>(opts =>
    opts.UseSqlServer(
        builder.Configuration.GetConnectionString("NotificationDb"),
        sql => sql.MigrationsAssembly(typeof(NotificationDbContext).Assembly.FullName)));

// ── Repository & email service ────────────────────────────────────────────
builder.Services.AddScoped<INotificationLogRepository, NotificationLogRepository>();
builder.Services.AddSingleton<IEmailService, LoggingEmailService>();

// ── gRPC client → PatientService ──────────────────────────────────────────
builder.Services.AddGrpcClient<PatientGrpc.PatientGrpcClient>(opts =>
{
    opts.Address = new Uri(builder.Configuration["GrpcClients:PatientService"]!);
})
.AddHealthBookingResiliencePipeline("notification-patient-grpc");

builder.Services.AddScoped<IPatientEmailClient, NotificationPatientGrpcClient>();

// ── MassTransit / RabbitMQ consumers ─────────────────────────────────────
builder.Services.AddMassTransit(cfg =>
{
    cfg.AddConsumer<AppointmentBookedConsumer>();
    cfg.AddConsumer<AppointmentCancelledConsumer>();
    cfg.AddConsumer<AppointmentRescheduledConsumer>();

    cfg.UsingRabbitMq((ctx, rmq) =>
    {
        rmq.Host(builder.Configuration.GetConnectionString("RabbitMq"));
        rmq.UseMessageRetry(r =>
            r.Exponential(5, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5)));
        rmq.ConfigureEndpoints(ctx);
    });
});

// ── OpenTelemetry ────────────────────────────────────────────────────────
builder.Services.AddHealthBookingTelemetry("notification-service", builder.Configuration);

// ── Health checks ─────────────────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddSqlServer(
        builder.Configuration.GetConnectionString("NotificationDb")!,
        name: "sql-notification",
        tags: ["ready"])
    .AddRabbitMQ(
        builder.Configuration.GetConnectionString("RabbitMq")!,
        name: "rabbitmq",
        tags: ["ready"]);

var app = builder.Build();

// ── Auto-migrate ──────────────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
    await db.Database.MigrateAsync();
}

app.UseSerilogRequestLogging();

app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = hc => hc.Tags.Contains("ready")
});

app.Run();
