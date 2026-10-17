namespace HappyInventory.Domain.Entities;

public sealed class RefreshSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FamilyId { get; set; }
    public int UserId { get; set; }
    public int SecurityVersion { get; set; }
    public User User { get; set; } = null!;
    public required string TokenHash { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime AbsoluteExpiresAtUtc { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public Guid? ReplacementId { get; set; }
}
