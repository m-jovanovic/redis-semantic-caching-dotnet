namespace SemanticCache.Api.Features.Caching;

public sealed record CacheCandidate(
    string Id,
    string Question,
    string Answer,
    double Distance,
    string TenantId,
    int Revision,
    DateTimeOffset CreatedAt);
