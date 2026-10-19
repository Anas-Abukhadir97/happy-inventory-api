namespace HappyInventory.Domain.Entities;

public sealed class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int RecipientUserId { get; set; }
    public User RecipientUser { get; set; } = null!;
    public int SenderUserId { get; set; }
    public User SenderUser { get; set; } = null!;
    public required string Title { get; set; }
    public required string Body { get; set; }
    public required string EmailStatus { get; set; }
    public required string SmsStatus { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ReadAtUtc { get; set; }
}
