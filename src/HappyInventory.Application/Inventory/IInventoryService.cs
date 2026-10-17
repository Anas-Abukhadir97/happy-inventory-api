namespace HappyInventory.Application.Inventory;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, string TotalCount, string TotalPages);

public sealed record WarehouseView(int Id, string Name, string Address, string City, string CountryCode,
    Guid Version, DateTime CreatedAtUtc, DateTime UpdatedAtUtc);
public sealed record WarehouseWrite(string? Name, string? Address, string? City, string? CountryCode);
public sealed record ItemView(int Id, int WarehouseId, string Name, string? Sku, int Qty, string CostPrice,
    string? MsrpPrice, Guid Version, DateTime CreatedAtUtc, DateTime UpdatedAtUtc);
public sealed record ItemWrite(string? Name, string? Sku, int Qty, string? CostPrice, string? MsrpPrice);
public sealed record CountryView(string Code, string NameKey);
public sealed record WarehouseStatus(int WarehouseId, string Name, string DistinctItemCount, string TotalQuantity);
public sealed record ItemQuantity(int Id, string Name, int WarehouseId, string WarehouseName, int Qty);

public sealed class BusinessFailure(string code, string resourceKey, int statusCode) : Exception(code)
{
    public string Code { get; } = code;
    public string ResourceKey { get; } = resourceKey;
    public int StatusCode { get; } = statusCode;
}

public interface IInventoryService
{
    Task<IReadOnlyList<CountryView>> CountriesAsync(CancellationToken cancellationToken);
    Task<PagedResult<WarehouseView>> WarehousesAsync(int page, int pageSize, string? search, CancellationToken cancellationToken);
    Task<WarehouseView?> WarehouseAsync(int id, CancellationToken cancellationToken);
    Task<WarehouseView> CreateWarehouseAsync(WarehouseWrite write, CancellationToken cancellationToken);
    Task<WarehouseView> UpdateWarehouseAsync(int id, Guid version, WarehouseWrite write, CancellationToken cancellationToken);
    Task DeleteWarehouseAsync(int id, Guid version, CancellationToken cancellationToken);
    Task<PagedResult<ItemView>> ItemsAsync(int warehouseId, int page, int pageSize, string? search, CancellationToken cancellationToken);
    Task<ItemView?> ItemAsync(int warehouseId, int itemId, CancellationToken cancellationToken);
    Task<ItemView> CreateItemAsync(int warehouseId, ItemWrite write, CancellationToken cancellationToken);
    Task<ItemView> UpdateItemAsync(int warehouseId, int itemId, Guid version, ItemWrite write, CancellationToken cancellationToken);
    Task DeleteItemAsync(int warehouseId, int itemId, Guid version, CancellationToken cancellationToken);
    Task<PagedResult<WarehouseStatus>> WarehouseStatusesAsync(int page, int pageSize, CancellationToken cancellationToken);
    Task<IReadOnlyList<ItemQuantity>> QuantityItemsAsync(bool highest, CancellationToken cancellationToken);
}
