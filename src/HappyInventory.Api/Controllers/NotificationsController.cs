using System.Globalization;
using HappyInventory.Api.Notifications;
using HappyInventory.Application.Auth;
using HappyInventory.Application.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace HappyInventory.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/notifications")]
public sealed class NotificationsController(INotificationService service, NotificationEventBroker events,
    IStringLocalizer<SharedResource> text) : BusinessControllerBase(text)
{
    [HttpGet]
    public Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 10,
        CancellationToken ct = default) => Execute(async () =>
    {
        var user = (CurrentUser)HttpContext.Items["CurrentUser"]!;
        Response.Headers.CacheControl = "no-store";
        return Ok(await service.ListAsync(user.Id, page, pageSize, ct));
    });

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public Task<IActionResult> Create([FromBody] NotificationCreate write, CancellationToken ct) => Execute(async () =>
    {
        var user = (CurrentUser)HttpContext.Items["CurrentUser"]!;
        var notification = await service.CreateAsync(user.Id, write, ct);
        return Created($"/api/v1/notifications/{notification.Id}", notification);
    });

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> Read(Guid id, CancellationToken ct)
    {
        var user = (CurrentUser)HttpContext.Items["CurrentUser"]!;
        return await service.MarkReadAsync(user.Id, id, ct) ? NoContent() : Failure("not_found", "NotFound", 404);
    }

    [HttpGet("stream")]
    public async Task Stream(CancellationToken ct)
    {
        var user = (CurrentUser)HttpContext.Items["CurrentUser"]!;
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache, no-transform";
        Response.Headers["X-Accel-Buffering"] = "no";
        var (reader, unsubscribe) = events.Subscribe(user.Id);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (long.TryParse(User.FindFirst("exp")?.Value, NumberStyles.None,
                CultureInfo.InvariantCulture, out var expiresAt))
        {
            var remaining = DateTimeOffset.FromUnixTimeSeconds(expiresAt) - DateTimeOffset.UtcNow;
            lifetime.CancelAfter(remaining > TimeSpan.Zero ? remaining : TimeSpan.FromMilliseconds(1));
        }
        try
        {
            await Response.WriteAsync(": connected\n\n", lifetime.Token);
            await Response.Body.FlushAsync(lifetime.Token);
            while (!lifetime.IsCancellationRequested)
            {
                using var heartbeat = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                heartbeat.CancelAfter(TimeSpan.FromSeconds(15));
                try
                {
                    if (await reader.WaitToReadAsync(heartbeat.Token))
                    {
                        while (reader.TryRead(out var id))
                            await Response.WriteAsync($"event: notification\ndata: {id:D}\n\n", lifetime.Token);
                    }
                    else break;
                }
                catch (OperationCanceledException) when (!lifetime.IsCancellationRequested)
                {
                    await Response.WriteAsync(": heartbeat\n\n", lifetime.Token);
                }
                await Response.Body.FlushAsync(lifetime.Token);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally { unsubscribe(); }
    }
}
