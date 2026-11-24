using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SemanticCache.Api.Features.Support;
using SemanticCache.Api.Infrastructure.Configuration;
using SemanticCache.Api.Infrastructure.Ollama;
using SemanticCache.Api.Infrastructure.Tracing;

namespace SemanticCache.Api.Features.Caching;

/// <summary>
/// Changes whenever a model, the prompt, the tools or the fixed facts change,
/// so answers from an older setup are never reused.
/// </summary>
public sealed class CacheFingerprint(OllamaClient ollama, IOptions<DemoOptions> options)
{
    public async Task<string> GetAsync(TraceContext trace, CancellationToken ct)
    {
        var chatModel = options.Value.ChatModel;
        var embeddingModel = options.Value.EmbeddingModel;

        var digests = await trace.MeasureAsync(
            "provider",
            "/api/tags",
            input: new { purpose = "Model digest check for cache scope; no inference" },
            action: () => ollama.GetModelDigestsAsync(ct));

        if (!digests.TryGetValue(chatModel, out var chatDigest) ||
            !digests.TryGetValue(embeddingModel, out var embeddingDigest))
        {
            throw new InvalidOperationException(
                "Required Ollama models are missing. Run docker compose up -d to download them.");
        }

        return Compute(chatModel, chatDigest, embeddingModel, embeddingDigest);
    }

    public static string Compute(params string[] models)
    {
        var setup = JsonSerializer.SerializeToUtf8Bytes(new
        {
            models,
            SupportPrompt.Text,
            SupportTools.Definitions,
            SupportAgent.Generation,
            SupportFacts.Version,
            SupportFacts.KeyRotation,
            SupportFacts.ExportDuration,
            SupportFacts.NoLiveData,
            OllamaClient.EmbeddingPrefix
        });

        return Convert.ToHexStringLower(SHA256.HashData(setup));
    }
}
