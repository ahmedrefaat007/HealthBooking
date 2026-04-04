using Microsoft.EntityFrameworkCore;
using NotificationService.Application.Interfaces;
using NotificationService.Domain.Entities;

namespace NotificationService.Infrastructure.Persistence.Repositories;

public sealed class NotificationLogRepository(NotificationDbContext context)
    : INotificationLogRepository
{
    public Task<NotificationLog?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        context.NotificationLogs.FirstOrDefaultAsync(n => n.Id == id, ct);

    public Task<bool> ExistsByCorrelationAndTypeAsync(
        Guid correlationId, string eventType, CancellationToken ct = default) =>
        context.NotificationLogs
            .AnyAsync(n => n.CorrelationId == correlationId && n.EventType == eventType, ct);

    public async Task AddAsync(NotificationLog log, CancellationToken ct = default) =>
        await context.NotificationLogs.AddAsync(log, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        context.SaveChangesAsync(ct);
}
