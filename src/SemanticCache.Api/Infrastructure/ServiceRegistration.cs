using Microsoft.Extensions.Options;
using SemanticCache.Api.Features.Caching;
using SemanticCache.Api.Features.Chat;
using SemanticCache.Api.Features.ExportJobs;
using SemanticCache.Api.Features.Playbooks;
using SemanticCache.Api.Features.Policies;
using SemanticCache.Api.Features.Support;
using SemanticCache.Api.Infrastructure.Configuration;
using SemanticCache.Api.Infrastructure.Ollama;
using SemanticCache.Api.Infrastructure.Tracing;
using StackExchange.Redis;

namespace SemanticCache.Api.Infrastructure;

public static class ServiceRegistration
{
    public static void AddRedis(this IServiceCollection services)
    {
        services.AddSingleton<IConnectionMultiplexer>(provider =>
            ConnectionMultiplexer.Connect(Settings(provider).RedisConnection));

        services.AddSingleton<PolicyStore>();
        services.AddSingleton<ExportJobStore>();
    }

    public static void AddOllama(this IServiceCollection services)
    {
        services.AddHttpClient<OllamaClient>((provider, client) =>
        {
            client.BaseAddress = new Uri(Settings(provider).OllamaUrl);
            client.Timeout = TimeSpan.FromMinutes(3);
        });
    }

    public static void AddSemanticCache(this IServiceCollection services)
    {
        services.AddSingleton<CacheKeys>();
        services.AddSingleton<AnswerCache>();
        services.AddSingleton<AnswerCacheInspector>();

        services.AddTransient<CacheFingerprint>();
        services.AddTransient<QuestionEmbedder>();
        services.AddTransient<SameQuestionCheck>();
    }

    public static void AddSupportChat(this IServiceCollection services)
    {
        services.AddSingleton<TraceHub>();

        services.AddTransient<SupportTools>();
        services.AddTransient<SupportAgent>();
        services.AddTransient<ChatService>();
    }

    public static void AddAgentPlaybooks(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<AgentPlaybookOptions>(
            configuration.GetSection(AgentPlaybookOptions.Section));

        services.AddHttpClient<AgentPlaybookClient>(client =>
            client.Timeout = TimeSpan.FromSeconds(10));
    }

    /// <summary>Creates the search index and the demo data if they are missing.</summary>
    public static async Task PrepareRedisAsync(this WebApplication app)
    {
        await app.Services.GetRequiredService<AnswerCache>().CreateIndexAsync();
        await app.Services.GetRequiredService<PolicyStore>().SeedAsync();
        await app.Services.GetRequiredService<ExportJobStore>().SeedAsync();
    }

    private static DemoOptions Settings(IServiceProvider provider)
    {
        return provider.GetRequiredService<IOptions<DemoOptions>>().Value;
    }
}
