using Microsoft.AspNetCore.Http;
using ProviderService.Application.Interfaces;
using System.Security.Claims;

namespace ProviderService.Infrastructure.Services;

public sealed class CurrentUserService(IHttpContextAccessor accessor) : ICurrentUserService
{
    public string UserId =>
        accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
}
