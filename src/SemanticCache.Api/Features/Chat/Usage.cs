using SemanticCache.Api.Features.Caching;
using SemanticCache.Api.Infrastructure.Ollama;

namespace SemanticCache.Api.Features.Chat;

/// <summary>Counts everything one question paid for.</summary>
public sealed class Usage
{
    public int ModelCalls { get; private set; }
    public int InputTokens { get; private set; }
    public int OutputTokens { get; private set; }

    public int ToolCalls { get; private set; }

    public int EmbeddingCalls { get; private set; }
    public int EmbeddingTokens { get; private set; }

    public int CheckCalls { get; private set; }
    public int CheckInputTokens { get; private set; }
    public int CheckOutputTokens { get; private set; }

    public int PlaybookCalls { get; private set; }

    public void AddModelCall(ModelReply reply)
    {
        ModelCalls++;
        InputTokens += reply.InputTokens;
        OutputTokens += reply.OutputTokens;
    }

    public void AddToolCall()
    {
        ToolCalls++;
    }

    public void AddEmbedding(Embedding embedding)
    {
        EmbeddingCalls++;
        EmbeddingTokens += embedding.Tokens;
    }

    public void AddCheck(CheckResult check)
    {
        CheckCalls++;
        CheckInputTokens += check.InputTokens;
        CheckOutputTokens += check.OutputTokens;
    }

    public void AddPlaybookCall()
    {
        PlaybookCalls++;
    }
}
