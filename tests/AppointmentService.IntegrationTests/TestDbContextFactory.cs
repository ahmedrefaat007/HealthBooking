using AppointmentService.Infrastructure.Persistence;
using AppointmentService.Infrastructure.Persistence.Interceptors;
using AppointmentService.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace AppointmentService.IntegrationTests;

internal static class TestDbContextFactory
{
    public static AppointmentDbContext Create(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AppointmentDbContext>()
            .UseSqlServer(connectionString,
                sql => sql.MigrationsAssembly("AppointmentService.Infrastructure"))
            .Options;

        var httpAccessor = new HttpContextAccessor();
        var currentUser = new CurrentUserService(httpAccessor);
        var audit = new AuditInterceptor(currentUser);
        var outbox = new OutboxPublishingInterceptor();

        return new AppointmentDbContext(options, audit, outbox);
    }
}
