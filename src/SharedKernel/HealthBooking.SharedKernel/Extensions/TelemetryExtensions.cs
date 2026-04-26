using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace HealthBooking.SharedKernel.Extensions;

/*
 * TelemetryExtensions
 * -------------------
 * Shared extension method that registers OpenTelemetry distributed tracing
 * pre-configured for the HealthBooking microservices.
 *
 * INSTRUMENTED SOURCES:
 *   - ASP.NET Core  — incoming HTTP + gRPC server requests.
 *   - HttpClient    — outgoing HTTP + gRPC-client calls.
 *   - MassTransit   — publish/consume spans via the built-in ActivitySource.
 *
 * WHO USES IT:
 *   Every service's Program.cs calls AddHealthBookingTelemetry(serviceName, config)
 *   to register telemetry.
 *
 * WHY THIS APPROACH:
 *   Centralising OpenTelemetry setup in SharedKernel ensures all services export
 *   the same span attributes and resource metadata so traces can be correlated in
 *   Jaeger (or any OTLP backend).  Health-check paths are filtered out to reduce
 *   span noise.  The endpoint defaults to localhost:4317 for local Docker Compose.
 */
public static class TelemetryExtensions
{
    public static IServiceCollection AddHealthBookingTelemetry(
        this IServiceCollection services,
        string serviceName,
        IConfiguration configuration)
    {
        var endpoint = configuration["OtelExporter:Endpoint"] ?? "http://localhost:4317";

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(
                    serviceName: serviceName,
                    serviceVersion: typeof(TelemetryExtensions).Assembly.GetName().Version?.ToString() ?? "1.0.0"))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(opts =>
                {
                    opts.RecordException = true;
                    // Exclude health-check endpoints to reduce noise
                    opts.Filter = ctx =>
                        !ctx.Request.Path.StartsWithSegments("/health");
                })
                .AddHttpClientInstrumentation(opts =>
                {
                    opts.RecordException = true;
                })
                // MassTransit 8.x exposes its own ActivitySource named "MassTransit"
                .AddSource("MassTransit")
                .AddOtlpExporter(opts =>
                {
                    opts.Endpoint = new Uri(endpoint);
                    opts.Protocol = OtlpExportProtocol.Grpc;
                }));

        return services;
    }
}
