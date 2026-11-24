using SemanticCache.Api.Infrastructure.Tracing;

namespace SemanticCache.Api.Features.Caching;

public static class CacheEndpoints
{
    public static void MapCache(this WebApplication app)
    {
        app.MapGet("/api/cache", ListAsync);
        app.MapPost("/api/admin/reset", ClearAsync);
    }

    private static async Task<List<CachedAnswer>> ListAsync(
        string? tenantId,
        string? sessionId,
        AnswerCacheInspector inspector,
        TraceHub hub)
    {
        using var trace = hub.Start(sessionId);

        return await inspector.ListAsync(tenantId, trace);
    }

    private static async Task<IResult> ClearAsync(
        string? sessionId,
        AnswerCacheInspector inspector,
        TraceHub hub)
    {
        using var trace = hub.Start(sessionId);

        var deleted = await inspector.ClearAsync(trace);

        return Results.Ok(new { cleared = true, deleted });
    }
}
