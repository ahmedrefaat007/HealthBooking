using HealthBooking.IdentityServer.Data;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;

namespace HealthBooking.IdentityServer;

public static class SeedData
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        await using var scope = services.GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope();

        // Apply EF migrations on startup
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.Database.MigrateAsync();

        var scopeManager = scope.ServiceProvider
            .GetRequiredService<IOpenIddictScopeManager>();

        // Seed scopes (audience mappings)
        foreach (var descriptor in Config.Scopes)
        {
            if (await scopeManager.FindByNameAsync(descriptor.Name!) is null)
                await scopeManager.CreateAsync(descriptor);
        }

        var appManager = scope.ServiceProvider
            .GetRequiredService<IOpenIddictApplicationManager>();

        // Seed clients
        foreach (var descriptor in Config.Clients)
        {
            if (await appManager.FindByClientIdAsync(descriptor.ClientId!) is null)
                await appManager.CreateAsync(descriptor);
        }
    }
}
