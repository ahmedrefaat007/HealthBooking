using Microsoft.AspNetCore.Http;
using PatientService.Application.Interfaces;
using System.Security.Claims;

namespace PatientService.Infrastructure.Services;

public sealed class CurrentUserService(IHttpContextAccessor accessor) : ICurrentUserService
{
    public string? UserId =>
        accessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
}
