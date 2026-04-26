using Microsoft.EntityFrameworkCore;

namespace HealthBooking.IdentityServer.Data;

/*
 * IdentityDbContext
 * -----------------
 * EF Core DbContext that hosts the OpenIddict entity tables
 * (applications, authorisations, scopes, tokens).
 *
 * WHO USES IT:
 *   - Program.cs: registered via AddDbContext and passed to OpenIddict's
 *     UseEntityFrameworkCore() so OpenIddict manages its own schema.
 *   - SeedData: obtains a scoped instance to run EF migrations on startup.
 *
 * WHY THIS APPROACH:
 *   OpenIddict stores all OAuth2/OIDC artefacts (clients, tokens, scopes)
 *   in relational tables managed by EF Core.  Inheriting from DbContext and
 *   calling builder.UseOpenIddict() in OnModelCreating is the recommended
 *   integration path — no custom entity definitions are required.
 */
public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options)
    : DbContext(options)
{
    /*
     * Calls UseOpenIddict() so OpenIddict registers its own entity mappings
     * (Application, Authorization, Scope, Token) into the model.
     */
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.UseOpenIddict();
    }
}
