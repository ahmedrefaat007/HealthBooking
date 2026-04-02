using MediatR;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace HealthBooking.SharedKernel.Behaviors;

public sealed class PerformanceBehavior<TRequest, TResponse>(ILogger<PerformanceBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var response = await next();
        sw.Stop();

        if (sw.ElapsedMilliseconds > 500)
            logger.LogWarning("Long running request: {RequestName} ({ElapsedMs} ms)",
                typeof(TRequest).Name, sw.ElapsedMilliseconds);

        return response;
    }
}
