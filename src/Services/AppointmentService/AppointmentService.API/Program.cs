using AppointmentService.API.Endpoints;
using AppointmentService.Application.Commands.BookAppointment;
using AppointmentService.Application.Interfaces;
using AppointmentService.Infrastructure.BackgroundServices;
using AppointmentService.Infrastructure.Clients;
using AppointmentService.Infrastructure.Persistence;
using AppointmentService.Infrastructure.Persistence.Interceptors;
using AppointmentService.Infrastructure.Persistence.Repositories;
using AppointmentService.Infrastructure.Services;
using FluentValidation;
using HealthBooking.Contracts.Grpc;
using HealthBooking.SharedKernel.Behaviors;
using HealthBooking.SharedKernel.Extensions;
using MassTransit;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
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
builder.Services.AddDbContext<AppointmentDbContext>(opts =>
    opts.UseSqlServer(
        builder.Configuration.GetConnectionString("AppointmentDb"),
        sql => sql.MigrationsAssembly(typeof(AppointmentDbContext).Assembly.FullName)));

builder.Services.AddScoped<AuditInterceptor>();
builder.Services.AddScoped<OutboxPublishingInterceptor>();

// ── Repositories ──────────────────────────────────────────────────────────
builder.Services.AddScoped<IAppointmentRepository, AppointmentRepository>();
builder.Services.AddScoped<IIdempotencyRepository, IdempotencyRepository>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

// ── gRPC clients ──────────────────────────────────────────────────────────
builder.Services.AddGrpcClient<PatientGrpc.PatientGrpcClient>(opts =>
{
    opts.Address = new Uri(builder.Configuration["GrpcClients:PatientService"]!);
})
.AddHealthBookingResiliencePipeline("patient-grpc");

builder.Services.AddGrpcClient<ProviderGrpc.ProviderGrpcClient>(opts =>
{
    opts.Address = new Uri(builder.Configuration["GrpcClients:ProviderService"]!);
})
.AddHealthBookingResiliencePipeline("provider-grpc");

builder.Services.AddScoped<IPatientGrpcClient, PatientGrpcClient>();
builder.Services.AddScoped<IProviderSlotGrpcClient, ProviderSlotGrpcClient>();

// ── MassTransit / RabbitMQ ────────────────────────────────────────────────
builder.Services.AddMassTransit(cfg =>
{
    cfg.UsingRabbitMq((ctx, rmq) =>
    {
        rmq.Host(builder.Configuration.GetConnectionString("RabbitMq"));
        rmq.ConfigureEndpoints(ctx);
    });
});

// ── OutboxProcessor ───────────────────────────────────────────────────────
builder.Services.AddHostedService<OutboxProcessor>();

// ── MediatR + validation ──────────────────────────────────────────────────
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(BookAppointmentCommand).Assembly);
    cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
});
builder.Services.AddValidatorsFromAssembly(typeof(BookAppointmentCommand).Assembly);

// ── JWT Bearer ────────────────────────────────────────────────────────────
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        opts.Authority = builder.Configuration["IdentityServer:BaseUrl"];
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateAudience = true,
            ValidAudiences    = ["appointment-service"]
        };
    });
builder.Services.AddAuthorization();

// ── Health checks ─────────────────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddSqlServer(
        builder.Configuration.GetConnectionString("AppointmentDb")!,
        name: "sql-appointment",
        tags: ["ready"])
    .AddRabbitMQ(
        builder.Configuration.GetConnectionString("RabbitMq")!,
        name: "rabbitmq",
        tags: ["ready"]);

var app = builder.Build();

// ── Auto-migrate ──────────────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppointmentDbContext>();
    await db.Database.MigrateAsync();
}

app.UseSerilogRequestLogging();
app.UseAuthentication();
app.UseAuthorization();

app.MapAppointmentEndpoints();

app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = hc => hc.Tags.Contains("ready")
});

app.Run();
