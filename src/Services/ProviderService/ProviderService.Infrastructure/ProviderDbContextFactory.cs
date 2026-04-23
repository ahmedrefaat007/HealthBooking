using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using ProviderService.Infrastructure.Persistence;
using ProviderService.Infrastructure.Persistence.Interceptors;
using ProviderService.Infrastructure.Services;

namespace ProviderService.Infrastructure;

public sealed class ProviderDbContextFactory : IDesignTimeDbContextFactory<ProviderDbContext>
{
    public ProviderDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ProviderDbContext>();
        optionsBuilder.UseSqlServer(
            "Server=AHMED-REFAAT\\SQLEXPRESS;Database=HealthBooking_Provider;Integrated Security=True;TrustServerCertificate=True;",
            sql => sql.MigrationsAssembly("ProviderService.Infrastructure"));

        var httpAccessor = new HttpContextAccessor();
        var currentUser = new CurrentUserService(httpAccessor);
        var audit = new AuditInterceptor(currentUser);
        var outbox = new OutboxPublishingInterceptor();

        return new ProviderDbContext(optionsBuilder.Options, audit, outbox);
    }
}
