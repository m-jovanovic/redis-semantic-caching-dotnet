using Microsoft.Extensions.Options;
using SemanticCache.Api.Infrastructure.Configuration;
using SemanticCache.Api.Infrastructure.Tracing;
using StackExchange.Redis;

namespace SemanticCache.Api.Features.Policies;

public sealed class PolicyStore(IConnectionMultiplexer connection, IOptions<DemoOptions> options)
{
    private readonly IDatabase _database = connection.GetDatabase();

    public string KeyFor(string tenantId)
    {
        return $"{options.Value.KeyPrefix}policy:{tenantId}";
    }

    public async Task SeedAsync()
    {
        await SeedAsync(new TenantPolicy(
            DemoTenants.Acme,
            "Acme",
            Revision: 1,
            ExportLimit: 10_000,
            RetentionDays: 30));

        await SeedAsync(new TenantPolicy(
            DemoTenants.Globex,
            "Globex",
            Revision: 1,
            ExportLimit: 1_000,
            RetentionDays: 7));
    }

    public async Task<TenantPolicy> GetAsync(string tenantId, TraceContext trace)
    {
        var key = KeyFor(tenantId);

        var fields = await trace.MeasureAsync(
            "redis",
            "HGETALL",
            input: new { key },
            action: async () =>
            {
                var entries = await _database.HashGetAllAsync(key);

                return entries.ToStringDictionary();
            });

        if (fields.Count == 0)
        {
            throw new KeyNotFoundException($"Unknown tenant '{tenantId}'.");
        }

        return ToPolicy(fields);
    }

    /// <summary>The limit and the revision change together, in one script.</summary>
    public async Task<TenantPolicy> UpdateExportLimitAsync(
        string tenantId,
        int exportLimit,
        TraceContext trace)
    {
        const string script =
            """
            redis.call('HINCRBY', KEYS[1], 'revision', 1)
            redis.call('HSET', KEYS[1], 'exportLimit', ARGV[1])

            return redis.call('HGETALL', KEYS[1])
            """;

        var key = KeyFor(tenantId);

        var fields = await trace.MeasureAsync(
            "redis",
            "EVAL",
            input: new { script = "HINCRBY revision + HSET exportLimit (atomic)", key, exportLimit },
            action: async () =>
            {
                var result = await _database.ScriptEvaluateAsync(script, [key], [exportLimit]);

                return result
                    .ToDictionary()
                    .ToDictionary(field => field.Key, field => field.Value.ToString());
            });

        return ToPolicy(fields);
    }

    private async Task SeedAsync(TenantPolicy policy)
    {
        var key = KeyFor(policy.Id);

        if (await _database.KeyExistsAsync(key))
        {
            return;
        }

        await _database.HashSetAsync(
            key,
            [
                new HashEntry("id", policy.Id),
                new HashEntry("name", policy.Name),
                new HashEntry("revision", policy.Revision),
                new HashEntry("exportLimit", policy.ExportLimit),
                new HashEntry("retentionDays", policy.RetentionDays)
            ]);
    }

    private static TenantPolicy ToPolicy(Dictionary<string, string> fields)
    {
        return new TenantPolicy(
            Id: fields["id"],
            Name: fields["name"],
            Revision: int.Parse(fields["revision"]),
            ExportLimit: int.Parse(fields["exportLimit"]),
            RetentionDays: int.Parse(fields["retentionDays"]));
    }
}
