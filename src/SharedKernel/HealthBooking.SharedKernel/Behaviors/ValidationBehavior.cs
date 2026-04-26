using FluentValidation;
using MediatR;

namespace HealthBooking.SharedKernel.Behaviors;

/*
 * ValidationBehavior<TRequest, TResponse>
 * ----------------------------------------
 * MediatR pipeline behavior that runs all registered FluentValidation
 * validators for a request BEFORE the handler is invoked.
 *
 * WHO USES IT:
 *   All services that use MediatR and define AbstractValidator<T> classes
 *   (AppointmentService, PatientService, ProviderService).
 *
 * WHY THIS APPROACH:
 *   - Keeps handlers free of manual validation checks.
 *   - Runs validators in parallel (Task.WhenAll) for better performance.
 *   - Throws a single ValidationException with ALL failures so the caller
 *     receives complete error feedback in one response.
 *   - Short-circuits with no overhead when no validators are registered.
 */
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    /*
     * If no validators exist for TRequest, skip straight to the next handler.
     * Otherwise run all validators in parallel and aggregate all failures.
     */
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        if (!validators.Any()) return await next();

        var context = new ValidationContext<TRequest>(request);
        var failures = (await Task.WhenAll(validators.Select(v => v.ValidateAsync(context, ct))))
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();

        /* Throw a single ValidationException with all rule violations collected above. */
        if (failures.Count > 0)
            throw new ValidationException(failures);

        return await next();
    }
}
