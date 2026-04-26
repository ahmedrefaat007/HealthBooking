using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NotificationService.Infrastructure.Persistence;
using NotificationService.Infrastructure.Persistence.Interceptors;
using NotificationService.Infrastructure.Services;

namespace NotificationService.IntegrationTests;

internal static class TestDbContextFactory
{
    public static NotificationDbContext Create(string connectionString)
    {
        var options = new DbContextOptionsBuilder<NotificationDbContext>()
            .UseSqlServer(connectionString,
                sql => sql.MigrationsAssembly("NotificationService.Infrastructure"))
            .Options;

        var httpAccessor = new HttpContextAccessor();
        var currentUser = new CurrentUserService(httpAccessor);
        var audit = new AuditInterceptor(currentUser);

        return new NotificationDbContext(options, audit);
    }
}
