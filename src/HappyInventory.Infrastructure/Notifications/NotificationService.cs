using System.Net;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;
using HappyInventory.Application.Inventory;
using HappyInventory.Application.Notifications;
using HappyInventory.Domain.Entities;
using HappyInventory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HappyInventory.Infrastructure.Notifications;

public sealed class NotificationService(InventoryDbContext db, INotificationPush push,
    IConfiguration config, ILogger<NotificationService> logger) : INotificationService
{
    private static readonly HttpClient SmsClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly Regex PhonePattern = new(@"^\+[1-9][0-9]{6,14}$", RegexOptions.Compiled);

    public async Task<PagedResult<NotificationView>> ListAsync(int userId, int page, int pageSize, CancellationToken ct)
    {
        if (page < 1 || pageSize is < 1 or > 10)
            throw new BusinessFailure("invalid_request", "InvalidRequest", 400);
        var query = db.Notifications.AsNoTracking().Where(x => x.RecipientUserId == userId);
        var count = await query.LongCountAsync(ct);
        var rows = await query.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
            .Skip((int)Math.Min((long)(page - 1) * pageSize, int.MaxValue))
            .Take(pageSize).Select(x => new NotificationView(x.Id, x.RecipientUserId, x.SenderUserId,
                x.Title, x.Body, x.EmailStatus, x.SmsStatus, x.CreatedAtUtc, x.ReadAtUtc))
            .ToListAsync(ct);
        return new PagedResult<NotificationView>(rows, page, pageSize,
            count.ToString(CultureInfo.InvariantCulture),
            ((count + pageSize - 1) / pageSize).ToString(CultureInfo.InvariantCulture));
    }

    public async Task<NotificationView> CreateAsync(int senderId, NotificationCreate write, CancellationToken ct)
    {
        var title = write.Title?.Trim();
        var body = write.Body?.Trim();
        if (string.IsNullOrWhiteSpace(title) || title.Length > 120 ||
            string.IsNullOrWhiteSpace(body) || body.Length > 1000 || write.RecipientUserId < 1 ||
            (write.SendSms && (write.SmsPhoneNumber is null || !PhonePattern.IsMatch(write.SmsPhoneNumber))))
            throw new BusinessFailure("invalid_request", "InvalidRequest", 400);
        if (write.SendEmail && !EmailConfigured() || write.SendSms && !SmsConfigured())
            throw new BusinessFailure("notification_channel_unavailable", "NotificationChannelUnavailable", 503);
        var recipient = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == write.RecipientUserId && x.IsActive, ct);
        if (recipient is null) throw new BusinessFailure("not_found", "NotFound", 404);

        var row = new Notification
        {
            RecipientUserId = recipient.Id,
            SenderUserId = senderId,
            Title = title,
            Body = body,
            EmailStatus = write.SendEmail ? "Pending" : "NotRequested",
            SmsStatus = write.SendSms ? "Pending" : "NotRequested",
            CreatedAtUtc = DateTime.UtcNow
        };
        db.Notifications.Add(row);
        await db.SaveChangesAsync(ct);
        push.Publish(recipient.Id, row.Id);

        // External delivery is explicit and never retried automatically after an uncertain result.
        if (write.SendEmail)
        {
            try
            {
                await SendEmailAsync(recipient.Email, title, body, ct);
                row.EmailStatus = "Sent";
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                row.EmailStatus = "Failed";
                logger.LogWarning(ex, "Email notification failed for {NotificationId}", row.Id);
            }
        }
        if (write.SendSms)
        {
            try
            {
                await SendSmsAsync(write.SmsPhoneNumber!, body, ct);
                row.SmsStatus = "Sent";
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                row.SmsStatus = "Failed";
                logger.LogWarning(ex, "SMS notification failed for {NotificationId}", row.Id);
            }
        }
        if (write.SendEmail || write.SendSms)
        {
            await db.SaveChangesAsync(ct);
            push.Publish(recipient.Id, row.Id);
        }
        return View(row);
    }

    public async Task<bool> MarkReadAsync(int userId, Guid id, CancellationToken ct)
    {
        var row = await db.Notifications.SingleOrDefaultAsync(x => x.Id == id && x.RecipientUserId == userId, ct);
        if (row is null) return false;
        if (row.ReadAtUtc is null)
        {
            row.ReadAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            push.Publish(userId, id);
        }
        return true;
    }

    private bool EmailConfigured() =>
        !string.IsNullOrWhiteSpace(config["Notifications:Email:Host"]) &&
        !string.IsNullOrWhiteSpace(config["Notifications:Email:From"]) &&
        !string.IsNullOrWhiteSpace(config["Notifications:Email:Username"]) &&
        !string.IsNullOrWhiteSpace(config["Notifications:Email:Password"]);

    private bool SmsConfigured() =>
        Uri.TryCreate(config["Notifications:Sms:LocalUrl"], UriKind.Absolute, out var url) &&
        (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps) &&
        url.AbsolutePath == "/message" &&
        IPAddress.TryParse(url.Host, out var address) && IsPrivateAddress(address) &&
        !string.IsNullOrWhiteSpace(config["Notifications:Sms:Username"]) &&
        !string.IsNullOrWhiteSpace(config["Notifications:Sms:Password"]);

    private static bool IsPrivateAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return true;
        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 &&
            (bytes[0] == 10 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31 ||
             bytes[0] == 192 && bytes[1] == 168);
    }

    private async Task SendEmailAsync(string recipient, string title, string body, CancellationToken ct)
    {
        using var mail = new MailMessage(config["Notifications:Email:From"]!, recipient, title, body);
        using var smtp = new SmtpClient(config["Notifications:Email:Host"]!,
            config.GetValue("Notifications:Email:Port", 587))
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(config["Notifications:Email:Username"],
                config["Notifications:Email:Password"])
        };
        await smtp.SendMailAsync(mail, ct);
    }

    private async Task SendSmsAsync(string phone, string body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, config["Notifications:Sms:LocalUrl"]);
        var raw = $"{config["Notifications:Sms:Username"]}:{config["Notifications:Sms:Password"]}";
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(raw)));
        request.Content = JsonContent.Create(new { textMessage = new { text = body }, phoneNumbers = new[] { phone } });
        using var response = await SmsClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    private static NotificationView View(Notification row) => new(row.Id, row.RecipientUserId,
        row.SenderUserId, row.Title, row.Body, row.EmailStatus, row.SmsStatus,
        row.CreatedAtUtc, row.ReadAtUtc);
}
