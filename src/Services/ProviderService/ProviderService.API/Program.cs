using FluentValidation;
using HealthBooking.SharedKernel.Behaviors;
using HealthBooking.SharedKernel.Extensions;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ProviderService.API.Endpoints;
using ProviderService.API.Grpc;
using ProviderService.Application.Commands.RegisterProvider;
using ProviderService.Application.Interfaces;
using ProviderService.Infrastructure.Persistence;
using ProviderService.Infrastructure.Persistence.Interceptors;
using ProviderService.Infrastructure.Persistence.Repositories;
using ProviderService.Infrastructure.Services;
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
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateAudience = true,
            ValidAudiences    = ["provider-service"]
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
