namespace SemanticCache.Api.Features.Caching;

/// <param name="Vector">Null in exact mode, which pays for no embedding.</param>
/// <param name="Shareable">True when the answer is the same for every tenant.</param>
public sealed record CacheEntry(
    string Question,
    string Answer,
    CacheScope Scope,
    float[]? Vector,
    DateTimeOffset CreatedAt,
    bool Shareable = false);
