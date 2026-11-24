namespace SemanticCache.Api.Infrastructure.Tracing;

public sealed record TraceEvent(
    long Sequence,
    string RequestId,
    string SessionId,
    DateTimeOffset Timestamp,
    string Category,
    string Operation,
    string Status,
    double? DurationMs,
    object? Data);
