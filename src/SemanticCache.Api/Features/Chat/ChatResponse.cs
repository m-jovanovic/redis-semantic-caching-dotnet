using SemanticCache.Api.Features.Caching;
using SemanticCache.Api.Infrastructure.Tracing;

namespace SemanticCache.Api.Features.Chat;

public sealed record ChatResponse(
    string RequestId,
    string SessionId,
    string TenantId,
    int PolicyRevision,
    string Mode,
    string Source,
    string Answer,
    double DurationMs,
    Usage Usage,
    CacheCandidate? Candidate,
    string CacheReason,
    string Provider,
    string ChatModel,
    IReadOnlyList<TraceEvent> Events);
