using System.Globalization;
using SemanticCache.Api.Infrastructure.Tracing;
using StackExchange.Redis;

namespace SemanticCache.Api.Features.Caching;

/// <summary>What the demo page needs around the cache: list, find and clear entries.</summary>
public sealed class AnswerCacheInspector(IConnectionMultiplexer connection, CacheKeys keys)
{
    private readonly IDatabase _database = connection.GetDatabase();

    public async Task<List<CachedAnswer>> ListAsync(string? tenantId, TraceContext trace)
    {
        var answers = new List<CachedAnswer>();

        foreach (var key in await ScanAsync(trace))
        {
            var answer = await ReadAsync(key, trace);

            if (answer is not null && (tenantId is null || answer.TenantId == tenantId))
            {
                answers.Add(answer);
            }
        }

        return answers;
    }

    /// <summary>Only an answer that is the same for every tenant may leave the cache.</summary>
    public async Task<CachedAnswer?> FindShareableAsync(string id, TraceContext trace)
    {
        if (!id.StartsWith(keys.EntryPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var answer = await ReadAsync(id, trace);

        return answer is { Shareable: true } ? answer : null;
    }

    /// <summary>Deletes cache entries only. Policies, jobs and the index stay.</summary>
    public async Task<long> ClearAsync(TraceContext trace)
    {
        var entries = await ScanAsync(trace);

        if (entries.Length == 0)
        {
            return 0;
        }

        return await trace.MeasureAsync(
            "redis",
            "DEL",
            input: new { keys = entries.Length },
            action: () => _database.KeyDeleteAsync(entries),
            describe: deleted => new { deleted });
    }

    private Task<RedisKey[]> ScanAsync(TraceContext trace)
    {
        var pattern = keys.EntryPrefix + "*";
        var server = connection.GetServer(connection.GetEndPoints()[0]);

        return trace.MeasureAsync(
            "redis",
            "SCAN",
            input: new { pattern },
            action: () => ReadAllAsync(server.KeysAsync(_database.Database, pattern)),
            describe: found => new { found = found.Length });
    }

    private async Task<CachedAnswer?> ReadAsync(RedisKey key, TraceContext trace)
    {
        var entries = await trace.MeasureAsync(
            "redis",
            "HGETALL",
            input: new { key = key.ToString() },
            action: () => _database.HashGetAllAsync(key),
            describe: result => new { found = result.Length > 0 });

        var timeToLive = await _database.KeyTimeToLiveAsync(key);

        if (entries.Length == 0 || timeToLive is null)
        {
            return null;
        }

        var fields = entries.ToDictionary(entry => entry.Name.ToString(), entry => entry.Value);

        return new CachedAnswer(
            Id: key.ToString(),
            Question: fields["question"].ToString(),
            Answer: fields["answer"].ToString(),
            TenantId: fields["tenantId"].ToString(),
            Revision: (int)fields["revision"],
            CreatedAt: DateTimeOffset.Parse(fields["createdAt"].ToString(), CultureInfo.InvariantCulture),
            Hits: (long)fields["hits"],
            Shareable: fields["shareable"] == 1,
            VectorDimensions: (int)fields["vectorDimensions"],
            TtlSeconds: (int)Math.Ceiling(timeToLive.Value.TotalSeconds));
    }

    // SCAN may return a key more than once.
    private static async Task<RedisKey[]> ReadAllAsync(IAsyncEnumerable<RedisKey> scan)
    {
        var found = new HashSet<RedisKey>();

        await foreach (var key in scan)
        {
            found.Add(key);
        }

        return [.. found];
    }
}
