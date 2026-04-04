using AppointmentService.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace AppointmentService.Infrastructure.Services;

public sealed class CurrentUserService(IHttpContextAccessor accessor) : ICurrentUserService
{
    public string UserId =>
        accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
}
