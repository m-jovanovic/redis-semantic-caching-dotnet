using System.Globalization;
using Microsoft.Extensions.Options;
using SemanticCache.Api.Features.Policies;
using SemanticCache.Api.Infrastructure.Configuration;
using SemanticCache.Api.Infrastructure.Tracing;
using StackExchange.Redis;

namespace SemanticCache.Api.Features.Caching;

public sealed class AnswerCache(
    IConnectionMultiplexer connection,
    CacheKeys keys,
    PolicyStore policies,
    IOptions<DemoOptions> options)
{
    private readonly IDatabase _database = connection.GetDatabase();

    /// <summary>Runs once, at startup. Redis then indexes every hash with this prefix.</summary>
    public async Task CreateIndexAsync()
    {
        object[] arguments =
        [
            keys.Index,
            "ON", "HASH",
            "PREFIX", 1, keys.EntryPrefix,
            "SCHEMA",
            "tenant", "TAG",
            "revision", "NUMERIC",
            "configuration", "TAG",
            "vector", "VECTOR", "FLAT", 6,
            "TYPE", "FLOAT32",
            "DIM", options.Value.VectorDimensions,
            "DISTANCE_METRIC", "COSINE"
        ];

        try
        {
            await _database.ExecuteAsync("FT.CREATE", arguments);
        }
        catch (RedisServerException ex) when (IndexExists(ex))
        {
            // Created by an earlier run.
        }
    }

    /// <summary>Same text, same scope: one hash lookup.</summary>
    public async Task<CacheCandidate?> FindExactAsync(
        string question,
        CacheScope scope,
        TraceContext trace)
    {
        var key = keys.ForEntry(question, scope);

        RedisValue[] fields = ["question", "answer", "tenantId", "revision", "createdAt"];

        var values = await trace.MeasureAsync(
            "redis",
            "HMGET",
            input: new { key, fields = fields.ToStringArray() },
            action: () => _database.HashGetAsync(key, fields),
            describe: result => new { found = !result[0].IsNull, question = result[0].ToString() });

        if (values[0].IsNull)
        {
            return null;
        }

        return new CacheCandidate(
            Id: key,
            Question: values[0].ToString(),
            Answer: values[1].ToString(),
            Distance: 0,
            TenantId: values[2].ToString(),
            Revision: (int)values[3],
            CreatedAt: ParseDate(values[4].ToString()));
    }

    /// <summary>Same meaning, same scope: the nearest vector that passes the filter.</summary>
    public async Task<CacheCandidate?> FindNearestAsync(
        float[] vector,
        CacheScope scope,
        TraceContext trace)
    {
        // The filter runs inside the search, so another tenant's answer is never a candidate.
        var query =
            $"(@tenant:{{{CacheKeys.Tag(scope.TenantId)}}} " +
            $"@revision:[{scope.Revision} {scope.Revision}] " +
            $"@configuration:{{{CacheKeys.Tag(scope.Configuration)}}})" +
            "=>[KNN 1 @vector $queryVector AS distance]";

        object[] arguments =
        [
            keys.Index, query,
            "PARAMS", 2, "queryVector", VectorEncoding.ToBytes(vector),
            "SORTBY", "distance", "ASC",
            "RETURN", 6, "question", "answer", "tenantId", "revision", "createdAt", "distance",
            "LIMIT", 0, 1,
            "DIALECT", 2
        ];

        var result = await trace.MeasureAsync(
            "redis",
            "FT.SEARCH",
            input: new { index = keys.Index, query, scope, vector = VectorEncoding.Describe(vector) },
            action: () => _database.ExecuteAsync("FT.SEARCH", arguments),
            describe: reply => new { candidate = ReadCandidate(reply) });

        return ReadCandidate(result);
    }

    /// <summary>Stores the answer, unless the policy changed while the model was writing it.</summary>
    public async Task StoreAsync(CacheEntry entry, TraceContext trace)
    {
        const string script =
            """
            if redis.call('HGET', KEYS[2], 'revision') ~= ARGV[1] then
                return 0
            end

            redis.call('HDEL', KEYS[1], 'vector')
            redis.call('HSET', KEYS[1], unpack(ARGV, 3))
            redis.call('EXPIRE', KEYS[1], ARGV[2])

            return 1
            """;

        var key = keys.ForEntry(entry.Question, entry.Scope);
        var ttlSeconds = options.Value.CacheTtlSeconds;

        RedisKey[] scriptKeys = [key, policies.KeyFor(entry.Scope.TenantId)];
        RedisValue[] scriptArguments = [entry.Scope.Revision, ttlSeconds, .. ToFields(entry)];

        await trace.MeasureAsync(
            "redis",
            "EVAL",
            input: new
            {
                script = "Check policy revision, HSET cache entry + EXPIRE (atomic)",
                key,
                question = entry.Question,
                scope = entry.Scope,
                ttlSeconds,
                vector = VectorEncoding.Describe(entry.Vector)
            },
            action: () => _database.ScriptEvaluateAsync(script, scriptKeys, scriptArguments),
            describe: result => new { stored = (long)result == 1 });
    }

    /// <summary>Counts a hit without recreating an entry that has just expired.</summary>
    public async Task RecordHitAsync(string key, TraceContext trace)
    {
        const string script =
            """
            if redis.call('EXISTS', KEYS[1]) == 0 then
                return 0
            end

            return redis.call('HINCRBY', KEYS[1], 'hits', 1)
            """;

        await trace.MeasureAsync(
            "redis",
            "EVAL",
            input: new { script = "HINCRBY hits if the entry still exists", key },
            action: () => _database.ScriptEvaluateAsync(script, [key]),
            describe: result => new { hits = (long)result });
    }

    private static List<RedisValue> ToFields(CacheEntry entry)
    {
        List<RedisValue> fields =
        [
            "question", entry.Question,
            "answer", entry.Answer,
            "tenantId", entry.Scope.TenantId,
            "tenant", CacheKeys.Tag(entry.Scope.TenantId),
            "revision", entry.Scope.Revision,
            "configuration", CacheKeys.Tag(entry.Scope.Configuration),
            "createdAt", entry.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
            "vectorDimensions", entry.Vector?.Length ?? 0,
            "shareable", entry.Shareable ? 1 : 0,
            "hits", 0
        ];

        if (entry.Vector is not null)
        {
            fields.Add("vector");
            fields.Add(VectorEncoding.ToBytes(entry.Vector));
        }

        return fields;
    }

    // FT.SEARCH replies with [total, key, [field, value, field, value, ...]].
    private static CacheCandidate? ReadCandidate(RedisResult result)
    {
        var rows = (RedisResult[])result!;

        if (rows.Length < 3 || rows[2].IsNull)
        {
            return null;
        }

        var fields = rows[2].ToDictionary();

        // The entry can expire between the index lookup and the field read.
        if (!fields.ContainsKey("question") || !fields.ContainsKey("answer"))
        {
            return null;
        }

        return new CacheCandidate(
            Id: rows[1].ToString(),
            Question: fields["question"].ToString(),
            Answer: fields["answer"].ToString(),
            Distance: double.Parse(fields["distance"].ToString(), CultureInfo.InvariantCulture),
            TenantId: fields["tenantId"].ToString(),
            Revision: (int)fields["revision"],
            CreatedAt: ParseDate(fields["createdAt"].ToString()));
    }

    private static bool IndexExists(RedisServerException exception)
    {
        return exception.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase);
    }

    private static DateTimeOffset ParseDate(string value)
    {
        return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
    }
}
