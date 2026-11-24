using SemanticCache.Api.Features.Policies;

namespace SemanticCache.RedisTests;

public sealed class PolicyStoreTests : RedisTest
{
    [Fact]
    public async Task Seeding_adds_both_tenants_and_preserves_a_changed_policy()
    {
        var acme = await Policies.GetAsync("acme", Trace());
        var globex = await Policies.GetAsync("globex", Trace());

        Assert.Equal(new TenantPolicy("acme", "Acme", 1, 10000, 30), acme);
        Assert.Equal(new TenantPolicy("globex", "Globex", 1, 1000, 7), globex);

        var changed = await Policies.UpdateExportLimitAsync("acme", 12345, Trace());
        Assert.Equal(2, changed.Revision);

        await Policies.SeedAsync();

        Assert.Equal(changed, await Policies.GetAsync("acme", Trace()));
        Assert.Equal(globex, await Policies.GetAsync("globex", Trace()));
    }

    [Fact]
    public async Task Concurrent_updates_increment_the_revision_without_losing_any()
    {
        var updates = await Task.WhenAll(Enumerable
            .Range(0, 16)
            .Select(i => Policies.UpdateExportLimitAsync("acme", 1000 + i, Trace())));

        Assert.Equal(Enumerable.Range(2, 16), updates.Select(policy => policy.Revision).Order());

        var current = await Policies.GetAsync("acme", Trace());
        var latest = updates.Single(policy => policy.Revision == 17);

        Assert.Equal(17, current.Revision);
        Assert.Equal(latest.ExportLimit, current.ExportLimit);
        Assert.Equal(30, current.RetentionDays);
    }

    [Fact]
    public async Task An_unknown_tenant_is_an_error()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Policies.GetAsync("other", Trace()));
    }
}
