using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SemanticCache.Api.Infrastructure.Configuration;

namespace SemanticCache.Api.Infrastructure.Ollama;

public sealed class OllamaClient(HttpClient http, IOptions<DemoOptions> options)
{
    // nomic-embed-text expects this prefix on a search query.
    public const string EmbeddingPrefix = "search_query: ";

    private const string KeepAlive = "30m";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<ModelReply> ChatAsync(
        IReadOnlyList<JsonObject> messages,
        GenerationOptions generation,
        JsonArray? tools,
        CancellationToken ct)
    {
        var request = new
        {
            Model = options.Value.ChatModel,
            Messages = messages,
            Stream = false,
            Think = false,
            KeepAlive,
            Options = generation,
            Tools = tools
        };

        var response = await PostAsync<ChatCompletion>("/api/chat", request, ct);

        return new ModelReply(
            response.Message,
            response.PromptEvalCount,
            response.EvalCount,
            Truncated: response.DoneReason == "length");
    }

    public async Task<Embedding> EmbedAsync(string question, CancellationToken ct)
    {
        var request = new
        {
            Model = options.Value.EmbeddingModel,
            Input = EmbeddingPrefix + question,
            Truncate = false,
            KeepAlive
        };

        var response = await PostAsync<EmbeddingCompletion>("/api/embed", request, ct);
        var vector = response.Embeddings[0];

        if (vector.Length != options.Value.VectorDimensions)
        {
            throw new InvalidOperationException("Embedding dimensions do not match the Redis index.");
        }

        return new Embedding(vector, response.PromptEvalCount);
    }

    public async Task<Dictionary<string, string>> GetModelDigestsAsync(CancellationToken ct)
    {
        var response = await http.GetFromJsonAsync<ModelList>("/api/tags", Json, ct);

        return response!.Models.ToDictionary(model => model.Name, model => model.Digest);
    }

    private async Task<T> PostAsync<T>(string path, object request, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync(path, request, Json, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);

            throw new HttpRequestException($"Ollama returned {(int)response.StatusCode}: {body}");
        }

        return (await response.Content.ReadFromJsonAsync<T>(Json, ct))!;
    }

    private sealed record ChatCompletion(
        JsonObject Message,
        int PromptEvalCount,
        int EvalCount,
        string? DoneReason);

    private sealed record EmbeddingCompletion(float[][] Embeddings, int PromptEvalCount);

    private sealed record ModelList(List<InstalledModel> Models);

    private sealed record InstalledModel(string Name, string Digest);
}
