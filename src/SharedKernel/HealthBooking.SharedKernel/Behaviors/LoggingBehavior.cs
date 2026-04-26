using MediatR;
using Microsoft.Extensions.Logging;

namespace HealthBooking.SharedKernel.Behaviors;

/*
 * LoggingBehavior<TRequest, TResponse>
 * ------------------------------------
 * MediatR pipeline behavior that logs the start and end of every
 * command/query handled in the system.
 *
 * WHO USES IT:
 *   Registered in each service's Program.cs via
 *   cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>)).
 *
 * WHY THIS APPROACH:
 *   A single cross-cutting behavior replaces dozens of per-handler log
 *   statements.  Because it sits in SharedKernel, all microservices benefit
 *   from uniform request tracing without duplicating code.
 *
 * USAGE ORDER IN PIPELINE:
 *   ValidationBehavior → LoggingBehavior → PerformanceBehavior → Handler
 */
public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    /*
     * Wraps the next handler with Info-level log entries.
     * Logs the request type name — avoids logging sensitive payload values.
     */
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        var requestName = typeof(TRequest).Name;
        logger.LogInformation("Handling {RequestName}", requestName);
        var response = await next();
        logger.LogInformation("Handled {RequestName}", requestName);
        return response;
    }
}
