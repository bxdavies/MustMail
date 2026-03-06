using System.Collections.Concurrent;

namespace MustMail.MailServer;

public partial class UpdateService(ILogger<UpdateService> logger)
{
    private readonly ConcurrentDictionary<string, List<Func<Task>>> _subscribers = new();

    public IDisposable Subscribe(string userId, Func<Task> handler)
    {
        List<Func<Task>> handlers = _subscribers.GetOrAdd(userId, _ => []);

        lock (handlers)
        {
            handlers.Add(handler);
            LogSubscriberAdded(userId);
        }

        return new Unsubscriber(() =>
        {
            if (_subscribers.TryGetValue(userId, out List<Func<Task>>? list))
            {
                lock (list)
                {
                    _ = list.Remove(handler);
                    LogSubscriberRemoved(userId);
                    if (list.Count == 0)
                    {
                        _ = _subscribers.TryRemove(userId, out _);
                        LogSubscriberListRemoved(userId);
                    }


                }
            }
        });
    }

    public async Task NewMessageForUserAsync(string userId)
    {
        LogNewMessageTriggered(userId);
        if (!_subscribers.TryGetValue(userId, out List<Func<Task>>? handlers))
        {
            LogNoSubscribers(userId);
            return;
        }

        List<Func<Task>> copy;

        lock (handlers)
        {
            copy = [.. handlers];
            LogDispatchingSubscribers(userId, copy.Count);
        }

        foreach (Func<Task> handler in copy)
        {
            try
            {
                await handler();
            }
            catch (Exception ex)
            {
                LogSubscriberFailed(ex, userId);
            }
        }
    }

    private sealed class Unsubscriber(Action dispose) : IDisposable
    {
        public void Dispose()
        {
            dispose();
        }
    }

    // 1300s = UpdateService

    [LoggerMessage(
        EventId = 1301,
        Level = LogLevel.Debug,
        Message = "Subscriber registered for user {UserId}")]
    private partial void LogSubscriberAdded(string userId);


    [LoggerMessage(
        EventId = 1302,
        Level = LogLevel.Debug,
        Message = "Subscriber removed for user {UserId}")]
    private partial void LogSubscriberRemoved(string userId);


    [LoggerMessage(
        EventId = 1303,
        Level = LogLevel.Debug,
        Message = "Last subscriber removed for user {UserId}. Cleaning up subscriber list")]
    private partial void LogSubscriberListRemoved(string userId);


    [LoggerMessage(
        EventId = 1304,
        Level = LogLevel.Debug,
        Message = "New message notification triggered for user {UserId}")]
    private partial void LogNewMessageTriggered(string userId);


    [LoggerMessage(
        EventId = 1305,
        Level = LogLevel.Debug,
        Message = "No subscribers found for user {UserId}")]
    private partial void LogNoSubscribers(string userId);


    [LoggerMessage(
        EventId = 1306,
        Level = LogLevel.Debug,
        Message = "Dispatching message notification to {SubscriberCount} subscriber(s) for user {UserId}")]
    private partial void LogDispatchingSubscribers(string userId, int subscriberCount);


    [LoggerMessage(
        EventId = 1307,
        Level = LogLevel.Warning,
        Message = "Subscriber callback failed for user {UserId}")]
    private partial void LogSubscriberFailed(Exception exception, string userId);
}