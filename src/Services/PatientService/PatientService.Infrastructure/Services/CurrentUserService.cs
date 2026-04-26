using Microsoft.AspNetCore.Http;
using PatientService.Application.Interfaces;
using System.Security.Claims;

namespace PatientService.Infrastructure.Services;

/*
 * CurrentUserService
 * ------------------
 * Resolves the authenticated user's ID from the current HTTP request context.
 *
 * WHO USES IT:
 *   AuditInterceptor: stamps CreatedBy / ModifiedBy on entity changes.
 *   Endpoint handlers: pass caller ID into UpdatePatientProfileCommand.
 *
 * WHY THIS APPROACH:
 *   Wrapping IHttpContextAccessor in a thin service keeps application-layer
 *   handlers free from ASP.NET Core dependencies, simplifying unit testing.
 *   Returns null when called outside an HTTP context (e.g., background jobs),
 *   allowing callers to default to "system".
 */
public sealed class CurrentUserService(IHttpContextAccessor accessor) : ICurrentUserService
{
    public string? UserId =>
        accessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
}
