using HealthBooking.IdentityServer;
using Microsoft.EntityFrameworkCore;
using Serilog;

// Configure a minimal Serilog bootstrap logger so early startup logs are captured.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

// Create the web application builder (reads configuration, sets up DI container, etc).
var builder = WebApplication.CreateBuilder(args);

// Configure Serilog as the application's logging provider and enrich it with DI services.
builder.Host.UseSerilog((ctx, services, config) =>
    config
        .ReadFrom.Configuration(ctx.Configuration) // Load sinks/levels from appsettings
        .ReadFrom.Services(services)               // Allow enrichment from registered services
        .Enrich.FromLogContext()                   // Include ambient log context properties
        .WriteTo.Console());                       // Write logs to console (fallback)

// Read the Identity DB connection string from configuration, fail fast if missing.
var connString = builder.Configuration.GetConnectionString("IdentityDb")
    ?? throw new InvalidOperationException("ConnectionStrings:IdentityDb is missing.");

// Determine the migrations assembly (used by EF Core stores to locate migrations).
var migrationsAssembly = typeof(Program).Assembly.GetName().Name;

builder.Services
    .AddIdentityServer(options =>
    {
        // Enable raising detailed events for diagnostics/observability.
        options.Events.RaiseErrorEvents       = true;
        options.Events.RaiseInformationEvents = true;
        options.Events.RaiseFailureEvents     = true;
        options.Events.RaiseSuccessEvents     = true;
    })
    .AddConfigurationStore(opts =>
        opts.ConfigureDbContext = b =>
            // Configure the configuration store to use SQL Server and the migrations assembly.
            b.UseSqlServer(connString,
                sql => sql.MigrationsAssembly(migrationsAssembly)))
    .AddOperationalStore(opts =>
    {
        // Configure the operational store (tokens, consents, etc.) with SQL Server.
        opts.ConfigureDbContext = b =>
            b.UseSqlServer(connString,
                sql => sql.MigrationsAssembly(migrationsAssembly));
        opts.EnableTokenCleanup   = true;    // Enable automatic cleanup of stale tokens
        opts.TokenCleanupInterval = 3600;    // Cleanup interval in seconds (1 hour)
    })
    .AddDeveloperSigningCredential(); // Use a temporary signing key for development only

// Add health checks and wire a readiness check to the Identity DB.
builder.Services
    .AddHealthChecks()
    .AddSqlServer(connString, name: "identity-db");

// Build the app from the configured builder.
var app = builder.Build();

// Seed initial IdentityServer data (clients, resources, users) at startup.
await SeedData.InitializeAsync(app.Services);

// Enable Serilog request logging middleware (correlates requests with logs).
app.UseSerilogRequestLogging();

// Add IdentityServer middleware to the request pipeline.
app.UseIdentityServer();

// Expose health endpoints for readiness and liveness probes.
app.MapHealthChecks("/health/ready");
app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));

// Run the web application (blocks the calling thread until shutdown).
app.Run();
