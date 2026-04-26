using MediatR;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace HealthBooking.SharedKernel.Behaviors;

/*
 * PerformanceBehavior<TRequest, TResponse>
 * ----------------------------------------
 * MediatR pipeline behavior that measures request handler execution time
 * and emits a Warning log when a handler exceeds the 500 ms threshold.
 *
 * WHO USES IT:
 *   Each service's DI pipeline (registered in Program.cs alongside
 *   LoggingBehavior and ValidationBehavior).
 *
 * WHY THIS APPROACH:
 *   Centralised slow-query alerting without framework-specific middleware.
 *   The 500 ms threshold is a good starting point for healthcare APIs where
 *   patients and providers expect responsive UI interactions.
 */
public sealed class PerformanceBehavior<TRequest, TResponse>(ILogger<PerformanceBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    /*
     * Starts a Stopwatch, calls the next handler, then checks elapsed time.
     * Logs a warning only when the threshold is exceeded to avoid noise.
     */
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var response = await next();
        sw.Stop();

        /* Warn on slow handlers — useful for identifying N+1 queries and missing indexes. */
        if (sw.ElapsedMilliseconds > 500)
            logger.LogWarning("Long running request: {RequestName} ({ElapsedMs} ms)",
                typeof(TRequest).Name, sw.ElapsedMilliseconds);

        return response;
    }
}
