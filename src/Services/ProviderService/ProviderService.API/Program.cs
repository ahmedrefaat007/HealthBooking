using FluentValidation;
using HealthBooking.SharedKernel.Behaviors;
using HealthBooking.SharedKernel.Extensions;
using MassTransit;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ProviderService.API.Endpoints;
using ProviderService.API.Grpc;
using ProviderService.Application.Commands.RegisterProvider;
using ProviderService.Application.Interfaces;
using ProviderService.Infrastructure.Messaging.Consumers;
using ProviderService.Infrastructure.Persistence;
using ProviderService.Infrastructure.Persistence.Interceptors;
using ProviderService.Infrastructure.Persistence.Repositories;
using ProviderService.Infrastructure.Services;
using Serilog;

/*
 * ProviderService / Program.cs
 * -----------------------------
 * Bootstraps the ProviderService microservice.
 *
 * WHO USES IT:
 *   .NET runtime entry point; called by Docker Compose or the dotnet CLI.
 *
 * WHAT IS REGISTERED:
 *   - SQL Server DbContext (ProviderDbContext) with AuditInterceptor + OutboxPublishingInterceptor.
 *   - ProviderRepository, SlotRepository, CurrentUserService.
 *   - Redis distributed cache (IDistributedCache) + RedisCacheService.
 *   - MassTransit consumers: AppointmentBookedConsumer, SlotReleasedConsumer.
 *   - MediatR pipeline: ValidationBehavior.
 *   - JWT Bearer validation (audience: provider-service).
 *   - gRPC server: ProviderGrpcService.
 *   - OpenTelemetry tracing exported to Jaeger via OTLP.
 *   - Health checks: SQL Server + Redis + RabbitMQ.
 */
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) =>
    cfg.ReadFrom.Configuration(ctx.Configuration)
       .Enrich.FromLogContext()
       .WriteTo.Console());

// ── Database ──────────────────────────────────────────────────────────────
builder.Services.AddDbContext<ProviderDbContext>(opts =>
    opts.UseSqlServer(
        builder.Configuration.GetConnectionString("ProviderDb"),
        sql => sql.MigrationsAssembly(typeof(ProviderDbContext).Assembly.FullName)));

builder.Services.AddScoped<AuditInterceptor>();
builder.Services.AddScoped<OutboxPublishingInterceptor>();

// ── Repositories & services ───────────────────────────────────────────────
builder.Services.AddScoped<IProviderRepository, ProviderRepository>();
builder.Services.AddScoped<ISlotRepository, SlotRepository>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

// ── Redis cache ───────────────────────────────────────────────────────────
builder.Services.AddStackExchangeRedisCache(opts =>
{
    opts.Configuration = builder.Configuration.GetConnectionString("Redis");
});
builder.Services.AddScoped<ICacheService, RedisCacheService>();

// ── MassTransit (RabbitMQ) ────────────────────────────────────────────────
builder.Services.AddMassTransit(cfg =>
{
    cfg.AddConsumer<AppointmentBookedConsumer>();
    cfg.AddConsumer<SlotReleasedConsumer>();

    cfg.UsingRabbitMq((ctx, rmq) =>
    {
        rmq.Host(builder.Configuration.GetConnectionString("RabbitMq"));

        rmq.UseMessageRetry(r =>
            r.Exponential(5, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5)));

        rmq.ConfigureEndpoints(ctx);
    });
});

// ── MediatR + validation behavior ────────────────────────────────────────
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(RegisterProviderCommand).Assembly);
    cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
});
builder.Services.AddValidatorsFromAssembly(typeof(RegisterProviderCommand).Assembly);

// ── JWT Bearer ────────────────────────────────────────────────────────────
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        opts.Authority = builder.Configuration["IdentityServer:BaseUrl"];
        opts.RequireHttpsMetadata = false;
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateAudience = true,
            ValidAudiences = ["provider-service"]
        };
    });
builder.Services.AddAuthorization();

// ── gRPC ──────────────────────────────────────────────────────────────────
builder.Services.AddGrpc();

// ── OpenTelemetry ────────────────────────────────────────────────────────
builder.Services.AddHealthBookingTelemetry("provider-service", builder.Configuration);

// ── Health checks ─────────────────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddSqlServer(
        builder.Configuration.GetConnectionString("ProviderDb")!,
        name: "sql-provider",
        tags: ["ready"])
    .AddRedis(
        builder.Configuration.GetConnectionString("Redis")!,
        name: "redis",
        tags: ["ready"])
    .AddRabbitMQ(
        builder.Configuration.GetConnectionString("RabbitMq")!,
        name: "rabbitmq",
        tags: ["ready"]);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ProviderDbContext>();
    await db.Database.MigrateAsync();
}

app.UseSerilogRequestLogging();
app.UseAuthentication();
app.UseAuthorization();

app.MapProviderEndpoints();
app.MapGrpcService<ProviderGrpcService>();

app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = hc => hc.Tags.Contains("ready")
});

app.Run();
