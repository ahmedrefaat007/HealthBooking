using HealthBooking.SharedKernel.Domain;
using HealthBooking.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;

namespace ProviderService.Infrastructure.Persistence.Interceptors;

/*
 * OutboxPublishingInterceptor (ProviderService)
 * ----------------------------------------------
 * EF Core SaveChangesInterceptor that converts AggregateRoot domain events into
 * OutboxMessage rows in the same database transaction.
 *
 * WHO USES IT:
 *   ProviderDbContext: registered via OnConfiguring.
 *
 * WHY THIS APPROACH:
 *   Atomically captures SlotLockedEvent, SlotReleasedEvent, ProviderRegisteredEvent,
 *   etc. alongside the entity change so no event is lost if the broker is temporarily
 *   unavailable.  A separate OutboxProcessor (or MassTransit outbox) publishes them.
 */
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
                    EventType = domainEvent.GetType().FullName!,
                    SchemaVersion = "1.0",
                    Payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType()),
                    DestinationExchange = domainEvent.GetType().Name
                                             .Replace("DomainEvent", "")
                                             .Replace("Event", "")
                                             .ToLowerInvariant(),
                    CreatedAt = DateTimeOffset.UtcNow
                };
                context.Set<OutboxMessage>().Add(outboxMessage);
            }

            aggregate.ClearDomainEvents();
        }

        return base.SavingChangesAsync(eventData, result, ct);
    }
}
