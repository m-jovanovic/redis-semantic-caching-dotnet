namespace SemanticCache.Api.Features.Caching;

/// <summary>A stored answer as the Cache entries panel shows it.</summary>
public sealed record CachedAnswer(
    string Id,
    string Question,
    string Answer,
    string TenantId,
    int Revision,
    DateTimeOffset CreatedAt,
    long Hits,
    bool Shareable,
    int VectorDimensions,
    int TtlSeconds);
