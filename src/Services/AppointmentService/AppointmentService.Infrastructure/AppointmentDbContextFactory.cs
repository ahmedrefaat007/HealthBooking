using AppointmentService.Infrastructure.Persistence;
using AppointmentService.Infrastructure.Persistence.Interceptors;
using AppointmentService.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

namespace AppointmentService.Infrastructure;

public sealed class AppointmentDbContextFactory : IDesignTimeDbContextFactory<AppointmentDbContext>
{
    public AppointmentDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppointmentDbContext>();
        optionsBuilder.UseSqlServer(
            "Server=localhost,1435;Database=AppointmentDb;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=True;",
            sql => sql.MigrationsAssembly("AppointmentService.Infrastructure"));

        var httpAccessor = new HttpContextAccessor();
        var currentUser  = new CurrentUserService(httpAccessor);
        var audit        = new AuditInterceptor(currentUser);
        var outbox       = new OutboxPublishingInterceptor();

        return new AppointmentDbContext(optionsBuilder.Options, audit, outbox);
    }
}
