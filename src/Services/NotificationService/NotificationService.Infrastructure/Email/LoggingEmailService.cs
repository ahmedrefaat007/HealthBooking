using Microsoft.Extensions.Logging;
using NotificationService.Application.Interfaces;

namespace NotificationService.Infrastructure.Email;

/// <summary>
/// Development stub — logs the email instead of sending it.
/// Swap for SendGrid/SMTP adapter in production (Week 6).
/// </summary>
public sealed class LoggingEmailService(ILogger<LoggingEmailService> logger)
    : IEmailService
{
    public Task SendAsync(
        string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        logger.LogInformation(
            "[EMAIL STUB] To={To} | Subject={Subject} | Body={Body}",
            to, subject, htmlBody);

        return Task.CompletedTask;
    }
}
