using HealthBooking.IdentityServer;
using Microsoft.EntityFrameworkCore;
using Serilog;

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

var migrationsAssembly = typeof(Program).Assembly.GetName().Name;

builder.Services
    .AddIdentityServer(options =>
    {
        options.Events.RaiseErrorEvents       = true;
        options.Events.RaiseInformationEvents = true;
        options.Events.RaiseFailureEvents     = true;
        options.Events.RaiseSuccessEvents     = true;
    })
    .AddConfigurationStore(opts =>
        opts.ConfigureDbContext = b =>
            b.UseSqlServer(connString,
                sql => sql.MigrationsAssembly(migrationsAssembly)))
    .AddOperationalStore(opts =>
    {
        opts.ConfigureDbContext = b =>
            b.UseSqlServer(connString,
                sql => sql.MigrationsAssembly(migrationsAssembly));
        opts.EnableTokenCleanup   = true;
        opts.TokenCleanupInterval = 3600;
    })
    .AddDeveloperSigningCredential();

builder.Services
    .AddHealthChecks()
    .AddSqlServer(connString, name: "identity-db");

var app = builder.Build();

await SeedData.InitializeAsync(app.Services);

app.UseSerilogRequestLogging();

app.UseIdentityServer();

app.MapHealthChecks("/health/ready");
app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));

app.Run();
