using Microsoft.Extensions.Options;
using SemanticCache.Api.Features.Caching;
using SemanticCache.Api.Features.ExportJobs;
using SemanticCache.Api.Features.Policies;
using SemanticCache.Api.Infrastructure.Configuration;
using SemanticCache.Api.Infrastructure.Tracing;
using StackExchange.Redis;

namespace SemanticCache.RedisTests;

/// <summary>Runs against a real Redis. Every test gets its own key prefix and search index.</summary>
public abstract class RedisTest : IAsyncLifetime
{
    private const string DefaultConnection =
        "127.0.0.1:6396,abortConnect=true,protocol=resp2,connectTimeout=5000,asyncTimeout=5000";

    private readonly TraceHub _traces = new();
    private ConnectionMultiplexer _connection = null!;

    protected string Prefix { get; } = $"scdemo-test-{Guid.NewGuid():N}:";
    protected IDatabase Database => _connection.GetDatabase();

    protected CacheKeys Keys { get; private set; } = null!;
    protected PolicyStore Policies { get; private set; } = null!;
    protected ExportJobStore Jobs { get; private set; } = null!;
    protected AnswerCache Cache { get; private set; } = null!;
    protected AnswerCacheInspector Inspector { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var connection = Environment.GetEnvironmentVariable("REDIS_TEST_CONNECTION") ?? DefaultConnection;
        _connection = await ConnectionMultiplexer.ConnectAsync(connection);

        var options = Settings(ttlSeconds: 60);

        Keys = new CacheKeys(options);
        Policies = new PolicyStore(_connection, options);
        Jobs = new ExportJobStore(_connection, options);
        Cache = CacheWithTtl(60);
        Inspector = new AnswerCacheInspector(_connection, Keys);

        await Cache.CreateIndexAsync();
        await Policies.SeedAsync();
        await Jobs.SeedAsync();
    }

    public async Task DisposeAsync()
    {
        if (_connection is null)
        {
            return;
        }

        // Cleanup never uses FLUSHDB or the demo's own prefix.
        var server = _connection.GetServer(_connection.GetEndPoints()[0]);

        await foreach (var key in server.KeysAsync(Database.Database, Prefix + "*"))
        {
            await Database.KeyDeleteAsync(key);
        }

        await Database.ExecuteAsync("FT.DROPINDEX", Prefix + "idx");
        await _connection.DisposeAsync();
    }

    protected AnswerCache CacheWithTtl(int ttlSeconds)
    {
        return new AnswerCache(_connection, Keys, Policies, Settings(ttlSeconds));
    }

    protected TraceContext Trace()
    {
        return _traces.Start("redis-tests");
    }

    protected static float[] Unit(int index)
    {
        var vector = new float[768];
        vector[index] = 1;

        return vector;
    }

    private IOptions<DemoOptions> Settings(int ttlSeconds)
    {
        return Options.Create(new DemoOptions
        {
            KeyPrefix = Prefix,
            CacheTtlSeconds = ttlSeconds
        });
    }
}
