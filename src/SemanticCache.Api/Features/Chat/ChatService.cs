using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Options;
using SemanticCache.Api.Features.Caching;
using SemanticCache.Api.Features.Playbooks;
using SemanticCache.Api.Features.Policies;
using SemanticCache.Api.Features.Support;
using SemanticCache.Api.Infrastructure.Configuration;
using SemanticCache.Api.Infrastructure.Tracing;

namespace SemanticCache.Api.Features.Chat;

public sealed class ChatService(
    PolicyStore policies,
    CacheFingerprint fingerprint,
    AgentPlaybookClient playbook,
    AnswerCache cache,
    QuestionEmbedder embedder,
    SameQuestionCheck check,
    SupportAgent agent,
    IOptions<DemoOptions> options)
{
    public async Task<ChatResponse> ReplyAsync(
        ChatRequest request,
        TraceContext trace,
        CancellationToken ct)
    {
        var turn = await StartAsync(request, trace, ct);

        var reply =
            await TryPlaybookAsync(turn, ct) ??
            await TryExactCacheAsync(turn) ??
            await TrySemanticCacheAsync(turn, ct) ??
            await AskModelAsync(turn, ct);

        return Complete(turn, reply);
    }

    // 0. Who is asking, and what may be reused for them.
    private async Task<ChatTurn> StartAsync(
        ChatRequest request,
        TraceContext trace,
        CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();

        trace.Emit("request", "chat", "started", new
        {
            request.Message,
            request.Mode,
            request.TenantId,
            request.Verify,
            request.Playbooks
        });

        var policy = await policies.GetAsync(request.TenantId, trace);
        var configuration = await fingerprint.GetAsync(trace, ct);

        return new ChatTurn
        {
            Request = request,
            Policy = policy,
            Scope = new CacheScope(request.TenantId, policy.Revision, configuration),
            Trace = trace,
            Clock = clock
        };
    }

    // 1. A reviewed answer wins over cached model output.
    private async Task<ChatReply?> TryPlaybookAsync(ChatTurn turn, CancellationToken ct)
    {
        if (!turn.Request.Playbooks || !playbook.IsConfigured)
        {
            return null;
        }

        var faq = await playbook.TrySearchAsync(turn.Question, turn.Trace, ct);
        turn.Usage.AddPlaybookCall();

        if (faq is null || !await IsSameQuestionAsync(faq.Question, turn, ct))
        {
            return null;
        }

        var verdict = turn.Request.Verify
            ? "The check accepted it as the same question."
            : "The check is off, so the score alone decided.";

        return new ChatReply(
            AnswerSource.Playbook,
            faq.Answer,
            $"Published FAQ version {faq.Version}, score {faq.Score:F3}. {verdict}");
    }

    // 2. Same text, same scope.
    private async Task<ChatReply?> TryExactCacheAsync(ChatTurn turn)
    {
        if (turn.Request.Mode == CacheMode.Off)
        {
            return null;
        }

        var match = await cache.FindExactAsync(turn.Question, turn.Scope, turn.Trace);

        if (match is null)
        {
            return null;
        }

        turn.Candidate = match;
        await cache.RecordHitAsync(match.Id, turn.Trace);

        return new ChatReply(
            AnswerSource.Exact,
            match.Answer,
            "Same text, tenant, policy revision and model configuration.");
    }

    // 3. Same meaning, same scope.
    private async Task<ChatReply?> TrySemanticCacheAsync(ChatTurn turn, CancellationToken ct)
    {
        if (turn.Request.Mode != CacheMode.Semantic)
        {
            return null;
        }

        var embedding = await embedder.EmbedAsync(turn.Question, turn.Trace, ct);
        turn.Vector = embedding.Vector;
        turn.Usage.AddEmbedding(embedding);

        var nearest = await cache.FindNearestAsync(embedding.Vector, turn.Scope, turn.Trace);
        turn.Candidate = nearest;

        if (!IsCloseEnough(nearest, turn))
        {
            return null;
        }

        if (!await IsSameQuestionAsync(nearest.Question, turn, ct))
        {
            turn.MissReason =
                $"Distance {nearest.Distance:F4} was close enough, " +
                "but the check rejected it as a different question.";

            return null;
        }

        await cache.RecordHitAsync(nearest.Id, turn.Trace);

        var verdict = turn.Request.Verify
            ? ", and the check accepted it as the same question."
            : ". The check is off, so distance alone decided.";

        return new ChatReply(
            AnswerSource.Semantic,
            nearest.Answer,
            $"Distance {nearest.Distance:F4}{verdict}");
    }

    // Distance finds a candidate. It does not prove the questions match.
    private bool IsCloseEnough([NotNullWhen(true)] CacheCandidate? candidate, ChatTurn turn)
    {
        var threshold = turn.Request.Threshold ?? options.Value.DistanceThreshold;
        var accepted = candidate is not null && candidate.Distance <= threshold;

        turn.Trace.Emit(
            "cache",
            "distance gate",
            data: new { accepted, threshold, candidate, rule = "cosine distance <= threshold" });

        if (candidate is not null && !accepted)
        {
            turn.MissReason = $"Nearest distance {candidate.Distance:F4} exceeded {threshold:F4}.";
        }

        return accepted;
    }

    // The yes/no check decides, unless the demo turns it off.
    private async Task<bool> IsSameQuestionAsync(string known, ChatTurn turn, CancellationToken ct)
    {
        if (!turn.Request.Verify)
        {
            return true;
        }

        var result = await check.RunAsync(known, turn.Question, turn.Trace, ct);
        turn.Usage.AddCheck(result);

        turn.Trace.Emit(
            "cache",
            "same question check",
            data: new { accepted = result.Same, known, asked = turn.Question });

        return result.Same;
    }

    // 4. Pay for an answer, then keep it if it is safe to reuse.
    private async Task<ChatReply> AskModelAsync(ChatTurn turn, CancellationToken ct)
    {
        var answer = await agent.AnswerAsync(
            turn.Question,
            turn.Policy,
            turn.Usage,
            turn.Trace,
            ct);

        if (turn.Request.Mode == CacheMode.Off)
        {
            return new ChatReply(
                AnswerSource.Model,
                answer.Text,
                "Caching is disabled.",
                answer.PolicyRevision);
        }

        if (!answer.Reusable)
        {
            return new ChatReply(
                AnswerSource.Bypass,
                answer.Text,
                $"{turn.MissReason} Not stored: {answer.WhyNotReusable}",
                answer.PolicyRevision);
        }

        var entry = new CacheEntry(
            turn.Question,
            answer.Text,
            turn.Scope,
            turn.Vector,
            DateTimeOffset.UtcNow,
            answer.Shareable);

        await cache.StoreAsync(entry, turn.Trace);

        return new ChatReply(
            AnswerSource.Model,
            answer.Text,
            turn.MissReason,
            answer.PolicyRevision);
    }

    private ChatResponse Complete(ChatTurn turn, ChatReply reply)
    {
        var trace = turn.Trace;
        var durationMs = turn.Clock.Elapsed.TotalMilliseconds;

        trace.Emit("cache", "decision", data: new { reply.Source, reply.Reason });
        trace.Emit("request", "chat", "completed", new { reply.Source, turn.Usage }, durationMs);

        var response = new ChatResponse(
            RequestId: trace.RequestId,
            SessionId: trace.SessionId,
            TenantId: turn.Request.TenantId,
            PolicyRevision: reply.PolicyRevision ?? turn.Policy.Revision,
            Mode: turn.Request.Mode,
            Source: reply.Source,
            Answer: reply.Answer,
            DurationMs: durationMs,
            Usage: turn.Usage,
            Candidate: turn.Candidate,
            CacheReason: reply.Reason,
            Provider: "Ollama",
            ChatModel: options.Value.ChatModel,
            Events: trace.Events);

        trace.Result = response;

        return response;
    }
}
