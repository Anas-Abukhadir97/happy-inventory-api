namespace HappyInventory.Domain.Entities;

public sealed class InventoryDeduction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int CallerUserId { get; set; }
    public User CallerUser { get; set; } = null!;
    public required string IdempotencyKey { get; set; }
    public required string RequestFingerprint { get; set; }
    public required string ExternalReference { get; set; }
    public int WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;
    public int ItemId { get; set; }
    public Item Item { get; set; } = null!;
    public int Quantity { get; set; }
    public int ResultQuantity { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
