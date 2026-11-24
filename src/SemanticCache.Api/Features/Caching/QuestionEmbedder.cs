using Microsoft.Extensions.Options;
using SemanticCache.Api.Infrastructure.Configuration;
using SemanticCache.Api.Infrastructure.Ollama;
using SemanticCache.Api.Infrastructure.Tracing;

namespace SemanticCache.Api.Features.Caching;

/// <summary>Turns a question into the vector Redis searches with.</summary>
public sealed class QuestionEmbedder(OllamaClient ollama, IOptions<DemoOptions> options)
{
    public Task<Embedding> EmbedAsync(string question, TraceContext trace, CancellationToken ct)
    {
        var model = options.Value.EmbeddingModel;

        return trace.MeasureAsync(
            "embedding",
            "/api/embed",
            input: new { model, question },
            action: () => ollama.EmbedAsync(question, ct),
            describe: result => new { model, dimensions = result.Vector.Length, tokens = result.Tokens });
    }
}
