using Microsoft.AspNetCore.Http;
using NotificationService.Application.Interfaces;
using System.Security.Claims;

namespace NotificationService.Infrastructure.Services;

/*
 * CurrentUserService
 * ------------------
 * ICurrentUserService implementation that reads the NameIdentifier claim
 * from the current HTTP request's ClaimsPrincipal.
 *
 * WHO USES IT:
 *   AuditInterceptor: stamps CreatedBy/ModifiedBy on entity saves.
 *
 * WHY THIS APPROACH:
 *   IHttpContextAccessor is request-scoped, so UserId is always the authenticated
 *   user for the current request without any static or thread-local state.
 */
public sealed class CurrentUserService(IHttpContextAccessor accessor) : ICurrentUserService
{
    public string? UserId =>
        accessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
}
