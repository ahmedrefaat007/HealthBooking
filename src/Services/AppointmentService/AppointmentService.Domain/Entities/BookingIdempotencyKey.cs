namespace AppointmentService.Domain.Entities;

public sealed class BookingIdempotencyKey
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Key { get; init; } = default!;
    public Guid AppointmentId { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
