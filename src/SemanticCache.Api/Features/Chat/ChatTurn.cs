using System.Diagnostics;
using SemanticCache.Api.Features.Caching;
using SemanticCache.Api.Features.Policies;
using SemanticCache.Api.Infrastructure.Tracing;

namespace SemanticCache.Api.Features.Chat;

/// <summary>Everything learned about one question on its way to an answer.</summary>
public sealed class ChatTurn
{
    public required ChatRequest Request { get; init; }
    public required TenantPolicy Policy { get; init; }
    public required CacheScope Scope { get; init; }
    public required TraceContext Trace { get; init; }
    public required Stopwatch Clock { get; init; }

    public Usage Usage { get; } = new();

    public float[]? Vector { get; set; }
    public CacheCandidate? Candidate { get; set; }
    public string MissReason { get; set; } = "No cached answer for this tenant and policy revision.";

    public string Question => Request.Message;
}
