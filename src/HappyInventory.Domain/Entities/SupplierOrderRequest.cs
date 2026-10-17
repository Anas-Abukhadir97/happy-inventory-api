namespace HappyInventory.Domain.Entities;

public sealed class SupplierOrderRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;
    public int ItemId { get; set; }
    public Item Item { get; set; } = null!;
    public int RequestedQuantity { get; set; }
    public required string SupplierDestination { get; set; }
    public int RequestedByUserId { get; set; }
    public User RequestedByUser { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; }
}
