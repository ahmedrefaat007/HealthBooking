using AppointmentService.Infrastructure.Persistence;
using AppointmentService.Infrastructure.Persistence.Interceptors;
using AppointmentService.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

namespace AppointmentService.Infrastructure;

/*
 * AppointmentDbContextFactory
 * ---------------------------
 * IDesignTimeDbContextFactory used by EF Core CLI tools (dotnet ef migrations add,
 * dotnet ef database update) at design time.
 *
 * WHO USES IT:
 *   EF Core tooling only; never instantiated at runtime.
 *
 * WHY THIS APPROACH:
 *   The design-time factory provides a fully configured DbContext—including
 *   AuditInterceptor and OutboxPublishingInterceptor—so migrations reflect the
 *   exact same schema that the runtime context produces.
 */
public sealed class AppointmentDbContextFactory : IDesignTimeDbContextFactory<AppointmentDbContext>
{
    public AppointmentDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppointmentDbContext>();
        optionsBuilder.UseSqlServer(
            "Server=AHMED-REFAAT\\SQLEXPRESS;Database=HealthBooking_Appointment;Integrated Security=True;TrustServerCertificate=True;",
            sql => sql.MigrationsAssembly("AppointmentService.Infrastructure"));

        var httpAccessor = new HttpContextAccessor();
        var currentUser = new CurrentUserService(httpAccessor);
        var audit = new AuditInterceptor(currentUser);
        var outbox = new OutboxPublishingInterceptor();

        return new AppointmentDbContext(optionsBuilder.Options, audit, outbox);
    }
}
