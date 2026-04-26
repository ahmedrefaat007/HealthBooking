using HealthBooking.IdentityServer.Data;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;

namespace HealthBooking.IdentityServer;

/*
 * SeedData
 * --------
 * Runs database migration and seeds the initial OpenIddict scopes and clients
 * on every application startup.
 *
 * WHO USES IT:
 *   Program.cs: called as `await SeedData.InitializeAsync(app.Services)`
 *   before the HTTP pipeline is started.
 *
 * WHY THIS APPROACH:
 *   Idempotent seed-on-start ensures the IdentityServer is always configured
 *   correctly without manual intervention, which is essential for Docker-based
 *   local development and CI/CD pipelines.  FindByNameAsync / FindByClientIdAsync
 *   guards prevent duplicate creation on repeated restarts.
 */
public static class SeedData
{
    /*
     * 1. Applies any pending EF Core migrations (creates tables if first run).
     * 2. Seeds scopes from Config.Scopes (skips any that already exist).
     * 3. Seeds clients from Config.Clients (skips any that already exist).
     */
    public static async Task InitializeAsync(IServiceProvider services)
    {
        await using var scope = services.GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope();

        /* Apply EF migrations on startup */
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.Database.MigrateAsync();

        var scopeManager = scope.ServiceProvider
            .GetRequiredService<IOpenIddictScopeManager>();

        /* Seed scopes (audience mappings) */
        foreach (var descriptor in Config.Scopes)
        {
            if (await scopeManager.FindByNameAsync(descriptor.Name!) is null)
                await scopeManager.CreateAsync(descriptor);
        }

        var appManager = scope.ServiceProvider
            .GetRequiredService<IOpenIddictApplicationManager>();

        /* Seed clients */
        foreach (var descriptor in Config.Clients)
        {
            if (await appManager.FindByClientIdAsync(descriptor.ClientId!) is null)
                await appManager.CreateAsync(descriptor);
        }
    }
}
