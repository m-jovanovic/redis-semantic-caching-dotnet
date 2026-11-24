using System.Collections.Concurrent;
using System.Diagnostics;

namespace SemanticCache.Api.Infrastructure.Tracing;

/// <summary>Collects what one request did, for the request inspector.</summary>
public sealed class TraceContext(string requestId, string sessionId, TraceHub hub) : IDisposable
{
    private readonly ConcurrentQueue<TraceEvent> _events = new();

    public string RequestId { get; } = requestId;
    public string SessionId { get; } = sessionId;
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;

    public bool Finished { get; private set; }
    public object? Result { get; set; }

    public IReadOnlyList<TraceEvent> Events => _events.ToArray();

    public void Emit(
        string category,
        string operation,
        string status = "info",
        object? data = null,
        double? durationMs = null)
    {
        _events.Enqueue(hub.Publish(this, category, operation, status, data, durationMs));
    }

    /// <summary>Runs the action and records its input, output and duration.</summary>
    public async Task<T> MeasureAsync<T>(
        string category,
        string operation,
        object? input,
        Func<Task<T>> action,
        Func<T, object?>? describe = null)
    {
        Emit(category, operation, "started", input);
        var clock = Stopwatch.StartNew();

        try
        {
            var result = await action();
            var output = describe is null ? result : describe(result);

            Emit(category, operation, "completed", output, clock.Elapsed.TotalMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            var error = new { error = ex.Message };

            Emit(category, operation, "error", error, clock.Elapsed.TotalMilliseconds);

            throw;
        }
    }

    public void Dispose()
    {
        Finished = true;
    }
}
