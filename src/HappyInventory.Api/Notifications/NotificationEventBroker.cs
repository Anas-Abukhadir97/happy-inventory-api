using System.Collections.Concurrent;
using System.Threading.Channels;
using HappyInventory.Application.Notifications;

namespace HappyInventory.Api.Notifications;

public sealed class NotificationEventBroker : INotificationPush
{
    private readonly ConcurrentDictionary<int, ConcurrentDictionary<Guid, Channel<Guid>>> _subscribers = new();

    public (ChannelReader<Guid> Reader, Action Unsubscribe) Subscribe(int userId)
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<Guid>(new BoundedChannelOptions(32)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
        _subscribers.GetOrAdd(userId, _ => new()).TryAdd(id, channel);
        return (channel.Reader, () =>
        {
            if (_subscribers.TryGetValue(userId, out var users) && users.TryRemove(id, out var removed))
                removed.Writer.TryComplete();
        }
        );
    }

    public void Publish(int userId, Guid notificationId)
    {
        if (!_subscribers.TryGetValue(userId, out var listeners)) return;
        foreach (var subscriber in listeners.Values) subscriber.Writer.TryWrite(notificationId);
    }
}
