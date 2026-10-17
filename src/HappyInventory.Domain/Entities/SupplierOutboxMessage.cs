namespace HappyInventory.Domain.Entities;

public sealed class SupplierOutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderRequestId { get; set; }
    public SupplierOrderRequest OrderRequest { get; set; } = null!;
    public required string PayloadJson { get; set; }
    public required string Status { get; set; }
    public int AttemptCount { get; set; }
    public DateTime NextAttemptAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? DeliveredAtUtc { get; set; }
}
