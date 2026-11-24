using System.Collections.Concurrent;
using System.Threading.Channels;

namespace SemanticCache.Api.Infrastructure.Tracing;

/// <summary>Keeps recent traces and streams their events to the page.</summary>
public sealed class TraceHub
{
    private const string SystemSession = "system";
    private const int MaxRequests = 500;

    private readonly ConcurrentDictionary<Guid, Subscriber> _subscribers = new();
    private readonly ConcurrentDictionary<string, TraceContext> _requests = new();
    private long _sequence;

    public static bool IsValidSession(string? value)
    {
        return value is { Length: > 0 and <= 80 } &&
            value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
    }

    public TraceContext Start(string? sessionId = null)
    {
        var session = IsValidSession(sessionId) ? sessionId! : SystemSession;
        var trace = new TraceContext(Guid.NewGuid().ToString("N"), session, this);

        _requests[trace.RequestId] = trace;
        RemoveOldRequests();

        return trace;
    }

    public TraceContext? Find(string id)
    {
        return _requests.GetValueOrDefault(id);
    }

    public IEnumerable<TraceContext> List(string session)
    {
        return _requests.Values
            .Where(trace => trace.SessionId == session)
            .OrderByDescending(trace => trace.StartedAt)
            .Take(100);
    }

    public (Guid Id, ChannelReader<TraceEvent> Reader) Subscribe(string session)
    {
        var id = Guid.NewGuid();

        var channel = Channel.CreateBounded<TraceEvent>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

        _subscribers[id] = new Subscriber(session, channel);

        return (id, channel.Reader);
    }

    public void Unsubscribe(Guid id)
    {
        if (_subscribers.TryRemove(id, out var subscriber))
        {
            subscriber.Channel.Writer.TryComplete();
        }
    }

    internal TraceEvent Publish(
        TraceContext trace,
        string category,
        string operation,
        string status,
        object? data,
        double? durationMs)
    {
        var item = new TraceEvent(
            Interlocked.Increment(ref _sequence),
            trace.RequestId,
            trace.SessionId,
            DateTimeOffset.UtcNow,
            category,
            operation,
            status,
            durationMs,
            data);

        foreach (var subscriber in _subscribers.Values)
        {
            if (subscriber.Session == trace.SessionId)
            {
                subscriber.Channel.Writer.TryWrite(item);
            }
        }

        return item;
    }

    private void RemoveOldRequests()
    {
        if (_requests.Count <= MaxRequests)
        {
            return;
        }

        var finished = _requests.Values
            .OrderBy(trace => trace.StartedAt)
            .Take(100)
            .Where(trace => trace.Finished);

        foreach (var trace in finished)
        {
            _requests.TryRemove(trace.RequestId, out _);
        }
    }

    private sealed record Subscriber(string Session, Channel<TraceEvent> Channel);
}
