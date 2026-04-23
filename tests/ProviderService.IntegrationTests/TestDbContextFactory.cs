using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ProviderService.Infrastructure.Persistence;
using ProviderService.Infrastructure.Persistence.Interceptors;
using ProviderService.Infrastructure.Services;

namespace ProviderService.IntegrationTests;

internal static class TestDbContextFactory
{
    public static ProviderDbContext Create(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ProviderDbContext>()
            .UseSqlServer(connectionString,
                sql => sql.MigrationsAssembly("ProviderService.Infrastructure"))
            .Options;

        var httpAccessor = new HttpContextAccessor();
        var currentUser = new CurrentUserService(httpAccessor);
        var audit = new AuditInterceptor(currentUser);
        var outbox = new OutboxPublishingInterceptor();

        return new ProviderDbContext(options, audit, outbox);
    }
}
