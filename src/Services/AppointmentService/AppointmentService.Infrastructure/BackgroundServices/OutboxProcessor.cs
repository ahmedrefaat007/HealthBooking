using AppointmentService.Infrastructure.Persistence;
using HealthBooking.SharedKernel.Persistence;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace AppointmentService.Infrastructure.BackgroundServices;

public sealed class OutboxProcessor(
    IServiceScopeFactory         scopeFactory,
    ILogger<OutboxProcessor>     logger)
    : BackgroundService
{
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(5);
    private const int BatchSize = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("OutboxProcessor started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "OutboxProcessor encountered an error during polling.");
            }

            await Task.Delay(PollingInterval, stoppingToken);
        }

        logger.LogInformation("OutboxProcessor stopped.");
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db  = scope.ServiceProvider.GetRequiredService<AppointmentDbContext>();
        var bus = scope.ServiceProvider.GetRequiredService<IBus>();

        var messages = await db.OutboxMessages
            .Where(m => m.Status == "Pending")
            .OrderBy(m => m.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (messages.Count == 0)
            return;

        foreach (var msg in messages)
        {
            try
            {
                var eventType = Type.GetType(msg.EventType);
                if (eventType is null)
                {
                    logger.LogWarning("Cannot resolve event type '{EventType}'.", msg.EventType);
                    msg.Status     = "Failed";
                    msg.RetryCount++;
                    continue;
                }

                var payload = JsonSerializer.Deserialize(msg.Payload, eventType);
                if (payload is null)
                {
                    logger.LogWarning("Cannot deserialize event payload for '{EventType}'.", msg.EventType);
                    msg.Status     = "Failed";
                    msg.RetryCount++;
                    continue;
                }

                await bus.Publish(payload, eventType, ct);

                msg.Status      = "Published";
                msg.PublishedAt = DateTimeOffset.UtcNow;

                logger.LogDebug("Published outbox message {Id} ({EventType}).", msg.Id, msg.EventType);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to publish outbox message {Id}.", msg.Id);
                msg.RetryCount++;
                if (msg.RetryCount >= 5)
                    msg.Status = "Failed";
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
