using Microsoft.AspNetCore.Http;
using ProviderService.Application.Interfaces;
using System.Security.Claims;

namespace ProviderService.Infrastructure.Services;

/*
 * CurrentUserService (ProviderService)
 * --------------------------------------
 * ICurrentUserService implementation that reads the NameIdentifier claim
 * from the current HTTP request's ClaimsPrincipal.
 *
 * WHO USES IT:
 *   AuditInterceptor: stamps CreatedBy/ModifiedBy on entity saves.
 *
 * WHY THIS APPROACH:
 *   IHttpContextAccessor is request-scoped so UserId is always the authenticated
 *   caller.  Falls back to "system" for background and design-time contexts.
 */
public sealed class CurrentUserService(IHttpContextAccessor accessor) : ICurrentUserService
{
    public string UserId =>
        accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
}
