using Microsoft.Extensions.Options;
using SemanticCache.Api.Features.Playbooks;
using SemanticCache.Api.Features.Policies;
using SemanticCache.Api.Infrastructure.Configuration;
using SemanticCache.Api.Infrastructure.Http;
using SemanticCache.Api.Infrastructure.Ollama;
using SemanticCache.Api.Infrastructure.Tracing;
using StackExchange.Redis;

namespace SemanticCache.Api.Features.Status;

public static class StatusEndpoints
{
    public static void MapStatus(this WebApplication app)
    {
        app.MapGet("/api/status", GetAsync);
    }

    private static async Task<IResult> GetAsync(
        PolicyStore policies,
        IConnectionMultiplexer redis,
        OllamaClient ollama,
        AgentPlaybookClient playbook,
        TraceHub hub,
        IOptions<DemoOptions> options,
        CancellationToken ct)
    {
        using var trace = hub.Start();
        var settings = options.Value;

        var acme = await policies.GetAsync(DemoTenants.Acme, trace);
        var globex = await policies.GetAsync(DemoTenants.Globex, trace);
        var ping = await redis.GetDatabase().PingAsync();

        var (installed, error) = await GetModelsAsync(ollama, ct);
        var chatReady = installed.ContainsKey(settings.ChatModel);
        var embeddingReady = installed.ContainsKey(settings.EmbeddingModel);

        return Results.Ok(new
        {
            ready = chatReady && embeddingReady,
            provider = "Ollama",
            settings.ChatModel,
            settings.EmbeddingModel,
            threshold = settings.DistanceThreshold,
            ttlSeconds = settings.CacheTtlSeconds,
            playbookConfigured = playbook.IsConfigured,
            tenants = new[] { acme, globex },
            redis = new { connected = true, latencyMs = ping.TotalMilliseconds },
            models = new { chatReady, embeddingReady, installed = installed.Keys },
            error = error ?? DescribeMissingModels(chatReady && embeddingReady)
        });
    }

    private static async Task<(Dictionary<string, string> Installed, string? Error)> GetModelsAsync(
        OllamaClient ollama,
        CancellationToken ct)
    {
        try
        {
            return (await ollama.GetModelDigestsAsync(ct), null);
        }
        catch (HttpRequestException ex)
        {
            return ([], DemoErrors.Describe(ex));
        }
    }

    private static string? DescribeMissingModels(bool ready)
    {
        return ready ? null : "Run docker compose up -d and wait for both models to download.";
    }
}
