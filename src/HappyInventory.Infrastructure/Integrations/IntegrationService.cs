using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HappyInventory.Application.Integrations;
using HappyInventory.Application.Inventory;
using HappyInventory.Domain.Entities;
using HappyInventory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HappyInventory.Infrastructure.Integrations;

public sealed class IntegrationService(InventoryDbContext db) : IIntegrationService
{
    public async Task<DeductionView> DeductAsync(int callerId, string? key, DeductionWrite write, CancellationToken ct)
    {
        if (key is null || key.Length is < 1 or > 128 || key.Any(c => c < '!' || c > '~') ||
            write is null || string.IsNullOrWhiteSpace(write.ExternalReference) ||
            write.ExternalReference.Length > 200 || write.WarehouseId < 1 || write.ItemId < 1 || write.Quantity < 1)
            throw Invalid();

        var reference = write.ExternalReference.Trim();
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { reference, write.WarehouseId, write.ItemId, write.Quantity }))));
        // SQLite's serializable write transaction reserves the single writer before reading the key.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var prior = await db.InventoryDeductions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.CallerUserId == callerId && x.IdempotencyKey == key, ct);
        if (prior is not null)
        {
            if (prior.RequestFingerprint != fingerprint)
                throw new BusinessFailure("idempotency_conflict", "IdempotencyConflict", 409);
            return new DeductionView(prior.Id, prior.WarehouseId, prior.ItemId, prior.ResultQuantity, true);
        }

        var now = DateTime.UtcNow;
        // The predicate protects the >=1 invariant even against a separate concurrent order.
        var changed = await db.Items.Where(x => x.Id == write.ItemId && x.WarehouseId == write.WarehouseId &&
                x.Qty > write.Quantity && db.Warehouses.Any(w => w.Id == x.WarehouseId))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Qty, x => x.Qty - write.Quantity)
                .SetProperty(x => x.Version, Guid.NewGuid())
                .SetProperty(x => x.UpdatedAtUtc, now), ct);
        if (changed == 0)
        {
            var exists = await db.Items.AsNoTracking().AnyAsync(x => x.Id == write.ItemId &&
                x.WarehouseId == write.WarehouseId && db.Warehouses.Any(w => w.Id == x.WarehouseId), ct);
            throw exists ? new BusinessFailure("insufficient_stock", "InsufficientStock", 409)
                : new BusinessFailure("not_found", "NotFound", 404);
        }

        var remaining = await db.Items.AsNoTracking().Where(x => x.Id == write.ItemId)
            .Select(x => x.Qty).SingleAsync(ct);
        var deduction = new InventoryDeduction
        {
            CallerUserId = callerId,
            IdempotencyKey = key,
            RequestFingerprint = fingerprint,
            ExternalReference = reference,
            WarehouseId = write.WarehouseId,
            ItemId = write.ItemId,
            Quantity = write.Quantity,
            ResultQuantity = remaining,
            CreatedAtUtc = now
        };
        db.InventoryDeductions.Add(deduction);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new DeductionView(deduction.Id, deduction.WarehouseId, deduction.ItemId, remaining, false);
    }

    public async Task<SupplierOrderView> CreateOrderAsync(int callerId, SupplierOrderWrite write, CancellationToken ct)
    {
        if (write is null || write.WarehouseId < 1 || write.ItemId < 1 || write.RequestedQuantity < 1 ||
            string.IsNullOrWhiteSpace(write.SupplierDestination) || write.SupplierDestination.Length > 254)
            throw Invalid();
        if (!await db.Items.AsNoTracking().AnyAsync(x => x.Id == write.ItemId &&
            x.WarehouseId == write.WarehouseId && db.Warehouses.Any(w => w.Id == x.WarehouseId), ct))
            throw new BusinessFailure("not_found", "NotFound", 404);

        var now = DateTime.UtcNow;
        var order = new SupplierOrderRequest
        {
            WarehouseId = write.WarehouseId,
            ItemId = write.ItemId,
            RequestedQuantity = write.RequestedQuantity,
            SupplierDestination = write.SupplierDestination.Trim(),
            RequestedByUserId = callerId,
            CreatedAtUtc = now
        };
        var message = new SupplierOutboxMessage
        {
            OrderRequestId = order.Id,
            OrderRequest = order,
            PayloadJson = "",
            Status = "Pending",
            AttemptCount = 0,
            NextAttemptAtUtc = now,
            CreatedAtUtc = now
        };
        message.PayloadJson = JsonSerializer.Serialize(new
        {
            messageId = message.Id,
            orderId = order.Id,
            order.WarehouseId,
            order.ItemId,
            order.RequestedQuantity,
            order.SupplierDestination
        });
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        db.SupplierOrderRequests.Add(order);
        db.SupplierOutboxMessages.Add(message);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Map(order, message.Status);
    }

    public async Task<PagedResult<SupplierOrderView>> OrdersAsync(int page, int pageSize, CancellationToken ct)
    {
        if (page < 1 || pageSize is < 1 or > 10) throw Invalid();
        var count = await db.SupplierOrderRequests.LongCountAsync(ct);
        var rows = await db.SupplierOrderRequests.AsNoTracking()
            .Join(db.SupplierOutboxMessages.AsNoTracking(), order => order.Id, message => message.OrderRequestId,
                (order, message) => new { order, message.Status })
            .OrderByDescending(x => x.order.CreatedAtUtc).ThenByDescending(x => x.order.Id)
            .Skip((int)Math.Min((long)(page - 1) * pageSize, int.MaxValue)).Take(pageSize)
            .Select(x => new SupplierOrderView(x.order.Id, x.order.WarehouseId, x.order.ItemId,
                x.order.RequestedQuantity, x.order.SupplierDestination, x.Status, x.order.CreatedAtUtc))
            .ToListAsync(ct);
        return new PagedResult<SupplierOrderView>(rows, page, pageSize,
            count.ToString(CultureInfo.InvariantCulture),
            ((count + pageSize - 1) / pageSize).ToString(CultureInfo.InvariantCulture));
    }

    private static SupplierOrderView Map(SupplierOrderRequest order, string status) =>
        new(order.Id, order.WarehouseId, order.ItemId, order.RequestedQuantity,
            order.SupplierDestination, status, order.CreatedAtUtc);

    private static BusinessFailure Invalid() => new("invalid_request", "InvalidRequest", 400);
}
