using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace HealthBooking.SharedKernel.Extensions;

/*
 * ResilienceExtensions
 * --------------------
 * Extension method that attaches a standardised Polly 8 resilience pipeline
 * to any IHttpClientBuilder used in the solution.
 *
 * WHO USES IT:
 *   - AppointmentService: gRPC clients for PatientService and ProviderService.
 *   - PatientService: HTTP client for IdentityServer provisioning.
 *   - NotificationService: gRPC client for PatientService.
 *
 * WHY THIS APPROACH:
 *   Centralising the timeout/retry/circuit-breaker configuration prevents
 *   each service from choosing different values independently.  The chosen
 *   values (10 s timeout, 3 exponential retries, 50 % failure ratio CB) are
 *   industry-standard for internal microservice-to-microservice calls.
 */
public static class ResilienceExtensions
{
    /*
     * Adds the following layers (outermost → innermost):
     *  1. Timeout       — 10 s total per attempt.
     *  2. Retry         — up to 3 retries, exponential back-off with jitter.
     *  3. CircuitBreaker— opens after 50 % errors in a 30 s window (min 5 calls),
     *                     stays open for 30 s before probing again.
     */
    public static IHttpClientBuilder AddHealthBookingResiliencePipeline(
        this IHttpClientBuilder builder, string pipelineName)
    {
        builder.AddResilienceHandler(pipelineName, pipeline =>
        {
            /* Layer 1: global request timeout — prevents thread starvation. */
            pipeline.AddTimeout(TimeSpan.FromSeconds(10));

            /* Layer 2: retry with exponential back-off + jitter to avoid thundering herd. */
            pipeline.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromMilliseconds(500)
            });

            /* Layer 3: circuit breaker prevents hammering a failing downstream service. */
            pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,
                SamplingDuration = TimeSpan.FromSeconds(30),
                MinimumThroughput = 5,
                BreakDuration = TimeSpan.FromSeconds(30)
            });
        });

        return builder;
    }
}
