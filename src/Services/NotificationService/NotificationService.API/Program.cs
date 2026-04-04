using MassTransit;
using Microsoft.EntityFrameworkCore;
using NotificationService.Application.Consumers;
using NotificationService.Application.Interfaces;
using NotificationService.Infrastructure.Clients;
using NotificationService.Infrastructure.Email;
using NotificationService.Infrastructure.Persistence;
using NotificationService.Infrastructure.Persistence.Repositories;
using HealthBooking.Contracts.Grpc;
using HealthBooking.SharedKernel.Extensions;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) =>
    cfg.ReadFrom.Configuration(ctx.Configuration)
       .Enrich.FromLogContext()
       .WriteTo.Console());

// ── Database ──────────────────────────────────────────────────────────────
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

    cfg.UsingRabbitMq((ctx, rmq) =>
    {
        rmq.Host(builder.Configuration.GetConnectionString("RabbitMq"));
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
