using HappyInventory.Application.Inventory;

namespace HappyInventory.Application.Integrations;

public sealed record DeductionWrite(string? ExternalReference, int WarehouseId, int ItemId, int Quantity);
public sealed record DeductionView(Guid DeductionId, int WarehouseId, int ItemId, int ResultQuantity, bool Replayed);
public sealed record SupplierOrderWrite(int WarehouseId, int ItemId, int RequestedQuantity, string? SupplierDestination);
public sealed record SupplierOrderView(Guid Id, int WarehouseId, int ItemId, int RequestedQuantity,
    string SupplierDestination, string Status, DateTime CreatedAtUtc);

public interface IIntegrationService
{
    Task<DeductionView> DeductAsync(int callerId, string? key, DeductionWrite write, CancellationToken ct);
    Task<SupplierOrderView> CreateOrderAsync(int callerId, SupplierOrderWrite write, CancellationToken ct);
    Task<PagedResult<SupplierOrderView>> OrdersAsync(int page, int pageSize, CancellationToken ct);
}

public interface ISupplierNotificationAdapter
{
    Task DeliverAsync(Guid messageId, string payloadJson, CancellationToken ct);
}

public interface ISupplierOutboxDispatcher
{
    Task<int> DispatchDueAsync(CancellationToken ct);
}
