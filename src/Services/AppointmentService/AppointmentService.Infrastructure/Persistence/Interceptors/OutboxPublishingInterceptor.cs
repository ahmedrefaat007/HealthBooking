using HealthBooking.SharedKernel.Domain;
using HealthBooking.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;

namespace AppointmentService.Infrastructure.Persistence.Interceptors;

public sealed class OutboxPublishingInterceptor : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken ct = default)
    {
        var context = eventData.Context!;

        var aggregates = context.ChangeTracker
            .Entries<AggregateRoot>()
            .Where(e => e.Entity.DomainEvents.Count > 0)
            .Select(e => e.Entity)
            .ToList();

        foreach (var aggregate in aggregates)
        {
            foreach (var domainEvent in aggregate.DomainEvents)
            {
                var outboxMessage = new OutboxMessage
                {
                    EventType           = domainEvent.GetType().FullName!,
                    SchemaVersion       = "1.0",
                    Payload             = JsonSerializer.Serialize(domainEvent, domainEvent.GetType()),
                    DestinationExchange = domainEvent.GetType().Name
                                             .Replace("DomainEvent", "")
                                             .Replace("Event", "")
                                             .ToLowerInvariant(),
                    CreatedAt           = DateTimeOffset.UtcNow
                };
                context.Set<OutboxMessage>().Add(outboxMessage);
            }

            aggregate.ClearDomainEvents();
        }

        return base.SavingChangesAsync(eventData, result, ct);
    }
}
