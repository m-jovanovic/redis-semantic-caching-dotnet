namespace SemanticCache.Api.Infrastructure.Configuration;

public sealed class DemoOptions
{
    public const string Section = "Demo";

    public string RedisConnection { get; set; } = "127.0.0.1:6396,abortConnect=false,protocol=resp2";
    public string KeyPrefix { get; set; } = "scdemo2:";

    public string OllamaUrl { get; set; } = "http://127.0.0.1:11436";
    public string ChatModel { get; set; } = "qwen3:4b-instruct-2507-q4_K_M";
    public string EmbeddingModel { get; set; } = "nomic-embed-text:v1.5";

    public int VectorDimensions { get; set; } = 768;
    public double DistanceThreshold { get; set; } = 0.25;
    public int CacheTtlSeconds { get; set; } = 1800;
}
