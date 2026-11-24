using System.Text.Json;

namespace SemanticCache.Api.Infrastructure.Tracing;

public static class TraceEndpoints
{
    public static void MapRequestInspector(this WebApplication app)
    {
        app.MapGet("/api/events", StreamAsync);
        app.MapGet("/api/requests", List);
        app.MapGet("/api/requests/{id}", Find);
    }

    private static IEnumerable<object> List(string sessionId, TraceHub hub)
    {
        return hub.List(sessionId).Select(Snapshot);
    }

    private static IResult Find(string id, TraceHub hub)
    {
        return hub.Find(id) is { } trace
            ? Results.Ok(Snapshot(trace))
            : Results.NotFound();
    }

    private static object Snapshot(TraceContext trace)
    {
        return new
        {
            trace.RequestId,
            trace.StartedAt,
            trace.Finished,
            trace.Result,
            trace.Events
        };
    }

    private static async Task StreamAsync(
        string sessionId,
        HttpContext context,
        TraceHub hub,
        CancellationToken ct)
    {
        var response = context.Response;

        if (!TraceHub.IsValidSession(sessionId))
        {
            response.StatusCode = StatusCodes.Status400BadRequest;

            return;
        }

        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers["X-Accel-Buffering"] = "no";

        var (id, events) = hub.Subscribe(sessionId);

        try
        {
            await response.WriteAsync(": connected\n\n", ct);
            await response.Body.FlushAsync(ct);

            await foreach (var item in events.ReadAllAsync(ct))
            {
                var json = JsonSerializer.Serialize(item, JsonSerializerOptions.Web);

                await response.WriteAsync($"id: {item.Sequence}\nevent: trace\ndata: {json}\n\n", ct);
                await response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The page was closed.
        }
        finally
        {
            hub.Unsubscribe(id);
        }
    }
}
