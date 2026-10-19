namespace HappyInventory.Application.Notifications;

using HappyInventory.Application.Inventory;

public sealed record NotificationCreate(int RecipientUserId, string? Title, string? Body,
    bool SendEmail, bool SendSms, string? SmsPhoneNumber);

public sealed record NotificationView(Guid Id, int RecipientUserId, int SenderUserId,
    string Title, string Body, string EmailStatus, string SmsStatus,
    DateTime CreatedAtUtc, DateTime? ReadAtUtc);

public interface INotificationService
{
    Task<PagedResult<NotificationView>> ListAsync(int userId, int page, int pageSize, CancellationToken cancellationToken);
    Task<NotificationView> CreateAsync(int senderId, NotificationCreate write, CancellationToken cancellationToken);
    Task<bool> MarkReadAsync(int userId, Guid id, CancellationToken cancellationToken);
}

public interface INotificationPush
{
    void Publish(int userId, Guid notificationId);
}
