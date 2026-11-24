using Microsoft.Extensions.Options;
using SemanticCache.Api.Infrastructure.Configuration;
using SemanticCache.Api.Infrastructure.Ollama;
using SemanticCache.Api.Infrastructure.Tracing;

namespace SemanticCache.Api.Features.Caching;

/// <summary>
/// A close vector is only a candidate. This asks a model whether both questions share one answer.
/// </summary>
public sealed class SameQuestionCheck(OllamaClient ollama, IOptions<DemoOptions> options)
{
    private const string Prompt =
        "Two customer support questions follow. " +
        "Would one and the same answer fully and correctly resolve both? " +
        "Pay attention to what is being asked about (the object, the action, and whether it is " +
        "a limit, a procedure, a duration or a live status). " +
        "Reply with exactly YES or NO.";

    // YES or NO fits in three tokens.
    private static readonly GenerationOptions Generation = new(Seed: 1, NumPredict: 3);

    public Task<CheckResult> RunAsync(
        string known,
        string asked,
        TraceContext trace,
        CancellationToken ct)
    {
        return trace.MeasureAsync(
            "check",
            "same question?",
            input: new { model = options.Value.ChatModel, known, asked },
            action: () => AskAsync(known, asked, ct));
    }

    private async Task<CheckResult> AskAsync(string known, string asked, CancellationToken ct)
    {
        var reply = await ollama.ChatAsync(
            [
                ChatMessage.System(Prompt),
                ChatMessage.User($"A: {known}\nB: {asked}")
            ],
            Generation,
            tools: null,
            ct);

        // Anything except a clear yes is a miss, and a miss only costs a normal answer.
        var same = reply.Text.StartsWith("YES", StringComparison.OrdinalIgnoreCase);

        return new CheckResult(same, reply.InputTokens, reply.OutputTokens);
    }
}

public sealed record CheckResult(bool Same, int InputTokens, int OutputTokens);
