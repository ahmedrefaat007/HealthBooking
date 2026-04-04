using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace NotificationService.Infrastructure.Persistence;

public sealed class NotificationDbContextFactory
    : IDesignTimeDbContextFactory<NotificationDbContext>
{
    public NotificationDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<NotificationDbContext>()
            .UseSqlServer(
                "Server=localhost,1436;Database=NotificationDb;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=True;",
                sql => sql.MigrationsAssembly("NotificationService.Infrastructure"))
            .Options;

        return new NotificationDbContext(options);
    }
}
