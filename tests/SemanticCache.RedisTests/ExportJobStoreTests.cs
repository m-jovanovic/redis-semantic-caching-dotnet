namespace SemanticCache.RedisTests;

public sealed class ExportJobStoreTests : RedisTest
{
    [Fact]
    public async Task Jobs_are_scoped_to_the_tenant_even_with_the_same_id()
    {
        var acme = await Jobs.ListAsync("acme", Trace());
        var globex = await Jobs.ListAsync("globex", Trace());

        Assert.Equal(["EXP-1042", "EXP-1043"], acme.Select(job => job.Id));
        Assert.Equal(["EXP-1042", "EXP-2041"], globex.Select(job => job.Id));

        Assert.Equal("running", acme[0].Status);
        Assert.Equal("completed", globex[0].Status);

        Assert.All(acme, job => Assert.Equal("acme", job.TenantId));
        Assert.All(globex, job => Assert.Equal("globex", job.TenantId));
    }

    [Fact]
    public async Task Seeding_again_keeps_the_current_status()
    {
        await Database.HashSetAsync(Prefix + "export-job:acme:EXP-1042", "status", "completed");

        await Jobs.SeedAsync();

        var jobs = await Jobs.ListAsync("acme", Trace());
        Assert.Equal("completed", jobs[0].Status);
    }

    [Fact]
    public async Task Clearing_the_cache_keeps_the_jobs()
    {
        await Inspector.ClearAsync(Trace());

        Assert.Equal(2, (await Jobs.ListAsync("acme", Trace())).Count);
    }
}
