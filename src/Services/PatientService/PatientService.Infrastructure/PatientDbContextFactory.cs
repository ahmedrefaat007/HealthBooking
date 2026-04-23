using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using PatientService.Infrastructure.Persistence;
using PatientService.Infrastructure.Persistence.Interceptors;
using PatientService.Infrastructure.Services;

namespace PatientService.Infrastructure;

public sealed class PatientDbContextFactory : IDesignTimeDbContextFactory<PatientDbContext>
{
    public PatientDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<PatientDbContext>();
        optionsBuilder.UseSqlServer(
            "Server=AHMED-REFAAT\\SQLEXPRESS;Database=HealthBooking_Patient;Integrated Security=True;TrustServerCertificate=True;",
            sql => sql.MigrationsAssembly("PatientService.Infrastructure"));

        var httpAccessor = new HttpContextAccessor();
        var currentUser = new CurrentUserService(httpAccessor);
        var audit = new AuditInterceptor(currentUser);
        var outbox = new OutboxPublishingInterceptor();

        return new PatientDbContext(optionsBuilder.Options, audit, outbox);
    }
}
