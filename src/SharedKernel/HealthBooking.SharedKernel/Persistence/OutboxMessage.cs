namespace HealthBooking.SharedKernel.Persistence;

/*
 * OutboxMessage
 * -------------
 * Represents a single domain-event record stored in the transactional outbox.
 *
 * WHO USES IT:
 *   OutboxPublishingInterceptor (writes), OutboxProcessor (reads + publishes),
 *   AppointmentDbContext / PatientDbContext / ProviderDbContext (DbSet<OutboxMessage>).
 *
 * WHY THIS APPROACH:
 *   The Transactional Outbox Pattern guarantees that a domain event is persisted
 *   atomically with the aggregate state change (same DB transaction).  A background
 *   processor later reads Pending messages and publishes them to RabbitMQ, achieving
 *   at-least-once delivery without distributed transactions.
 *
 * STATUS LIFECYCLE:
 *   Pending → Published (success) | Failed (after 5 retries)
 */
public sealed class OutboxMessage
{
    /* Unique identifier; also the RabbitMQ message ID for deduplication. */
    public Guid Id { get; init; } = Guid.NewGuid();

    /* Assembly-qualified type name used by the processor to deserialise the event. */
    public string EventType { get; init; } = default!;

    /* Semantic version of the event schema (e.g., "1.0"). */
    public string SchemaVersion { get; init; } = default!;

    /* JSON-serialised event payload. */
    public string Payload { get; init; } = default!;

    /* Target RabbitMQ exchange derived from the event type name. */
    public string DestinationExchange { get; init; } = default!;

    public DateTimeOffset CreatedAt { get; init; }

    /* Set to UtcNow by the processor on successful publish. */
    public DateTimeOffset? PublishedAt { get; set; }

    /* Incremented on each failed publish attempt; record marked Failed after 5. */
    public int RetryCount { get; set; }

    /* Current processing status: Pending | Published | Failed. */
    public string Status { get; set; } = "Pending";
}

/*
 * OutboxStatus
 * ------------
 * Enum mirror of the Status string column — kept for typed comparisons in code.
 * The column itself is stored as a varchar for readability in DB queries.
 */
public enum OutboxStatus
{
    Pending,
    Published,
    Failed
}
