using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using ProviderService.Infrastructure.Persistence;
using ProviderService.Infrastructure.Persistence.Interceptors;
using ProviderService.Infrastructure.Services;

namespace ProviderService.Infrastructure;

/*
 * ProviderDbContextFactory
 * ------------------------
 * IDesignTimeDbContextFactory used by EF Core CLI tools at design time.
 *
 * WHO USES IT:
 *   EF Core tooling only (dotnet ef migrations add / dotnet ef database update).
 *
 * WHY THIS APPROACH:
 *   Provides a fully configured DbContext—including AuditInterceptor and
 *   OutboxPublishingInterceptor—so generated migrations match the runtime schema.
 */
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
