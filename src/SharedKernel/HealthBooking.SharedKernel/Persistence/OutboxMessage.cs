namespace HealthBooking.SharedKernel.Persistence;

public sealed class OutboxMessage
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string EventType { get; init; } = default!;
    public string SchemaVersion { get; init; } = default!;
    public string Payload { get; init; } = default!;
    public string DestinationExchange { get; init; } = default!;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? PublishedAt { get; set; }
    public int RetryCount { get; set; }
    public string Status { get; set; } = "Pending";
}

public enum OutboxStatus
{
    Pending,
    Published,
    Failed
}
