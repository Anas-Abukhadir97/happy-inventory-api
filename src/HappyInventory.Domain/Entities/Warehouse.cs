namespace HappyInventory.Domain.Entities;

public sealed class Warehouse
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string NormalizedName { get; set; }
    public required string Address { get; set; }
    public required string City { get; set; }
    public required string CountryCode { get; set; }
    public Country Country { get; set; } = null!;
    public Guid Version { get; set; } = Guid.NewGuid();
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
}
