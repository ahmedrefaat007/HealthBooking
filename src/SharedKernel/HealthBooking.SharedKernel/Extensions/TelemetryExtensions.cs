using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace HealthBooking.SharedKernel.Extensions;

/// <summary>
/// Registers OpenTelemetry distributed tracing, pre-configured for the HealthBooking stack.
///
/// Instrumented sources per service:
///   • ASP.NET Core  (incoming HTTP + gRPC server requests)
///   • HttpClient    (outgoing HTTP + gRPC-client calls)
///   • MassTransit   (publish / consume spans via built-in ActivitySource)
///
/// Traces are exported via OTLP to Jaeger (or any OTLP-compatible backend).
/// The endpoint is resolved from config key "OtelExporter:Endpoint"
/// (defaults to http://localhost:4317 for local development).
/// </summary>
public static class TelemetryExtensions
{
    public static IServiceCollection AddHealthBookingTelemetry(
        this IServiceCollection services,
        string                  serviceName,
        IConfiguration          configuration)
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
