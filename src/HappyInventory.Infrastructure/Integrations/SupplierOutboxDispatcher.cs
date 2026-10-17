using HappyInventory.Application.Integrations;
using HappyInventory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HappyInventory.Infrastructure.Integrations;

public sealed class LocalSupplierNotificationAdapter(ILogger<LocalSupplierNotificationAdapter> logger)
    : ISupplierNotificationAdapter
{
    public Task DeliverAsync(Guid messageId, string payloadJson, CancellationToken ct)
    {
        // The local adapter records only the stable ID; destinations and payloads stay out of logs.
        logger.LogInformation("Local supplier notification recorded: {MessageId}", messageId);
        return Task.CompletedTask;
    }
}
public sealed class SupplierOutboxDispatcher(InventoryDbContext db, ISupplierNotificationAdapter adapter,
    ILogger<SupplierOutboxDispatcher> logger) : ISupplierOutboxDispatcher
{
    public async Task<int> DispatchDueAsync(CancellationToken ct)
    {
        var delivered = 0;
        // SQLite permits one writer. Keep the transaction through delivery so competing workers
        // cannot dispatch the same pending message concurrently. The adapter must deduplicate IDs.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var due = await db.SupplierOutboxMessages
            .Where(x => x.Status != "Sent" && x.AttemptCount < 5 && x.NextAttemptAtUtc <= DateTime.UtcNow)
            .OrderBy(x => x.NextAttemptAtUtc).ThenBy(x => x.Id).Take(10).ToListAsync(ct);
        foreach (var message in due)
        {
            try
            {
                await adapter.DeliverAsync(message.Id, message.PayloadJson, ct);
                message.AttemptCount++;
                message.Status = "Sent";
                message.DeliveredAtUtc = DateTime.UtcNow;
                delivered++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                message.AttemptCount++;
                message.Status = "Failed";
                message.NextAttemptAtUtc = DateTime.UtcNow.AddSeconds(
                    Math.Min(300, 5 * (1 << (message.AttemptCount - 1))));
                logger.LogWarning(ex, "Local supplier delivery failed for {MessageId}", message.Id);
            }
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return delivered;
    }
}
