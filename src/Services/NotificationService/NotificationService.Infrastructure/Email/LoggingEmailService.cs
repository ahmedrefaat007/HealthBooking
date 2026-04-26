using Microsoft.Extensions.Logging;
using NotificationService.Application.Interfaces;

namespace NotificationService.Infrastructure.Email;

/*
 * LoggingEmailService
 * -------------------
 * Development/test IEmailService stub that logs email content instead of sending.
 *
 * WHO USES IT:
 *   DI container: registered as IEmailService in Program.cs.
 *   All three notification consumers call IEmailService.SendAsync.
 *
 * WHY THIS APPROACH:
 *   Provides a working implementation during development without requiring an
 *   SMTP or SendGrid account.  Swap for a real adapter (SendGrid, SMTP) in
 *   production by replacing this registration — no consumer code changes needed.
 */
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
