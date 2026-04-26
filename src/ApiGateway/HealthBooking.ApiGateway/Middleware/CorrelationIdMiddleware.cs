using Serilog.Context;

namespace HealthBooking.ApiGateway.Middleware;

/*
 * CorrelationIdMiddleware
 * -----------------------
 * ASP.NET Core middleware that ensures every request carries a correlation ID
 * so that all log entries across services for the same request share a common
 * identifier, making distributed tracing trivial.
 *
 * WHO USES IT:
 *   ApiGateway Program.cs: app.UseMiddleware<CorrelationIdMiddleware>().
 *   All downstream services log the X-Correlation-Id header when they receive
 *   forwarded requests from YARP.
 *
 * WHY THIS APPROACH:
 *   - If the caller already provides X-Correlation-Id, it is preserved and
 *     echoed back in the response header (useful for client-side request tracking).
 *   - If not provided, a new GUID is generated so no request is ever untracked.
 *   - Serilog.Context.LogContext.PushProperty injects the ID into every log
 *     event written within the scope, enabling log-aggregation queries like
 *     "show all logs for correlation 3a2b...".
 */
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    private const string HeaderName = "X-Correlation-Id";

    /*
     * Reads or creates the correlation ID, stamps it on the response header and
     * on the Serilog log context, then passes control to the next middleware.
     */
    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[HeaderName].FirstOrDefault()
            ?? Guid.NewGuid().ToString("N");

        /* Echo back so the client can correlate its own request logs. */
        context.Response.Headers[HeaderName] = correlationId;
        context.Items[HeaderName] = correlationId;

        /* Push into Serilog so every log line in this request scope carries the ID. */
        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }
}
