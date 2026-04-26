using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using NotificationService.Infrastructure.Persistence.Interceptors;
using NotificationService.Infrastructure.Services;

namespace NotificationService.Infrastructure.Persistence;

public sealed class NotificationDbContextFactory
    : IDesignTimeDbContextFactory<NotificationDbContext>
{
    public NotificationDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<NotificationDbContext>()
            .UseSqlServer(
                "Server=AHMED-REFAAT\\SQLEXPRESS;Database=HealthBooking_Notification;Integrated Security=True;TrustServerCertificate=True;",
                sql => sql.MigrationsAssembly("NotificationService.Infrastructure"))
            .Options;

        var audit = new AuditInterceptor(new CurrentUserService(new HttpContextAccessor()));
        return new NotificationDbContext(options, audit);
    }
}
