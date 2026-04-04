using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Mappers;
using Microsoft.EntityFrameworkCore;

namespace HealthBooking.IdentityServer;

public static class SeedData
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        await using var scope = services.GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope();

        var configDb = scope.ServiceProvider.GetRequiredService<ConfigurationDbContext>();
        await configDb.Database.MigrateAsync();

        var persistedDb = scope.ServiceProvider.GetRequiredService<PersistedGrantDbContext>();
        await persistedDb.Database.MigrateAsync();

        // Seed identity resources
        foreach (var resource in Config.IdentityResources)
        {
            if (!await configDb.IdentityResources.AnyAsync(r => r.Name == resource.Name))
                configDb.IdentityResources.Add(resource.ToEntity());
        }

        // Seed API scopes
        foreach (var scope2 in Config.ApiScopes)
        {
            if (!await configDb.ApiScopes.AnyAsync(s => s.Name == scope2.Name))
                configDb.ApiScopes.Add(scope2.ToEntity());
        }

        // Seed API resources
        foreach (var resource in Config.ApiResources)
        {
            if (!await configDb.ApiResources.AnyAsync(r => r.Name == resource.Name))
                configDb.ApiResources.Add(resource.ToEntity());
        }

        // Seed clients
        foreach (var client in Config.Clients)
        {
            if (!await configDb.Clients.AnyAsync(c => c.ClientId == client.ClientId))
                configDb.Clients.Add(client.ToEntity());
        }

        await configDb.SaveChangesAsync();
    }
}
