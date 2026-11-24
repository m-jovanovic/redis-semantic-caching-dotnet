using System.Globalization;
using Microsoft.Extensions.Options;
using SemanticCache.Api.Features.Policies;
using SemanticCache.Api.Infrastructure.Configuration;
using SemanticCache.Api.Infrastructure.Tracing;
using StackExchange.Redis;

namespace SemanticCache.Api.Features.ExportJobs;

/// <summary>Live data. The support agent reads it on every request and never caches it.</summary>
public sealed class ExportJobStore(IConnectionMultiplexer connection, IOptions<DemoOptions> options)
{
    private readonly IDatabase _database = connection.GetDatabase();

    public async Task SeedAsync()
    {
        var now = DateTimeOffset.UtcNow;

        await SeedAsync(new ExportJob("EXP-1042", DemoTenants.Acme, "running", 400, 1_000, 1, now));
        await SeedAsync(new ExportJob("EXP-1043", DemoTenants.Acme, "queued", 0, 500, 1, now));
        await SeedAsync(new ExportJob("EXP-1042", DemoTenants.Globex, "completed", 500, 500, 1, now));
        await SeedAsync(new ExportJob("EXP-2041", DemoTenants.Globex, "running", 100, 750, 1, now));
    }

    public async Task<List<ExportJob>> ListAsync(string tenantId, TraceContext trace)
    {
        var key = ListKey(tenantId);

        var ids = await trace.MeasureAsync(
            "redis",
            "SMEMBERS",
            input: new { key },
            action: async () =>
            {
                var members = await _database.SetMembersAsync(key);

                return members.Select(member => member.ToString()).Order().ToArray();
            });

        var jobs = new List<ExportJob>();

        foreach (var id in ids)
        {
            jobs.Add(await GetAsync(tenantId, id, trace));
        }

        return jobs;
    }

    private async Task<ExportJob> GetAsync(string tenantId, string id, TraceContext trace)
    {
        var key = JobKey(tenantId, id);

        var fields = await trace.MeasureAsync(
            "redis",
            "HGETALL",
            input: new { key },
            action: async () =>
            {
                var entries = await _database.HashGetAllAsync(key);

                return entries.ToStringDictionary();
            });

        return new ExportJob(
            Id: fields["id"],
            TenantId: fields["tenantId"],
            Status: fields["status"],
            ProcessedRows: int.Parse(fields["processedRows"]),
            TotalRows: int.Parse(fields["totalRows"]),
            Revision: int.Parse(fields["revision"]),
            UpdatedAt: DateTimeOffset.Parse(fields["updatedAt"], CultureInfo.InvariantCulture));
    }

    private async Task SeedAsync(ExportJob job)
    {
        var key = JobKey(job.TenantId, job.Id);

        if (!await _database.KeyExistsAsync(key))
        {
            await _database.HashSetAsync(
                key,
                [
                    new HashEntry("id", job.Id),
                    new HashEntry("tenantId", job.TenantId),
                    new HashEntry("status", job.Status),
                    new HashEntry("processedRows", job.ProcessedRows),
                    new HashEntry("totalRows", job.TotalRows),
                    new HashEntry("revision", job.Revision),
                    new HashEntry("updatedAt", job.UpdatedAt.ToString("O", CultureInfo.InvariantCulture))
                ]);
        }

        await _database.SetAddAsync(ListKey(job.TenantId), job.Id);
    }

    private string ListKey(string tenantId)
    {
        return $"{options.Value.KeyPrefix}export-jobs:{tenantId}";
    }

    private string JobKey(string tenantId, string id)
    {
        return $"{options.Value.KeyPrefix}export-job:{tenantId}:{id}";
    }
}
