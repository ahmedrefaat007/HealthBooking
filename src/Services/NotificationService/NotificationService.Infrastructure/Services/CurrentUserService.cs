using Microsoft.AspNetCore.Http;
using NotificationService.Application.Interfaces;
using System.Security.Claims;

namespace NotificationService.Infrastructure.Services;

public sealed class CurrentUserService(IHttpContextAccessor accessor) : ICurrentUserService
{
    public string? UserId =>
        accessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
}
