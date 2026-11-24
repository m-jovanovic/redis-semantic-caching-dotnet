using SemanticCache.Api.Features.Caching;

namespace SemanticCache.RedisTests;

public sealed class AnswerCacheInspectorTests : RedisTest
{
    private static readonly CacheScope Scope = new("acme", 1, "fixture");

    [Fact]
    public async Task Only_shareable_cache_entries_can_be_proposed()
    {
        var specific = await StoreAsync("Tenant specific?", Scope, shareable: false);
        var neutral = await StoreAsync("Tenant neutral?", Scope, shareable: true);

        var found = await Inspector.FindShareableAsync(neutral, Trace());

        Assert.Equal("Tenant neutral?", found?.Question);
        Assert.Null(await Inspector.FindShareableAsync(specific, Trace()));
        Assert.Null(await Inspector.FindShareableAsync(Prefix + "policy:acme", Trace()));
        Assert.Null(await Inspector.FindShareableAsync(Keys.EntryPrefix + "missing", Trace()));
    }

    [Fact]
    public async Task Clearing_removes_every_cache_entry_and_nothing_else()
    {
        var otherKey = Prefix + "outside-cache";
        var similarKey = Prefix + "cache-other:sentinel";

        await Database.StringSetAsync(otherKey, "keep");
        await Database.StringSetAsync(similarKey, "keep too");

        var policy = await Policies.UpdateExportLimitAsync("acme", 8765, Trace());
        var acme = Scope with { Revision = policy.Revision };

        for (var i = 0; i < 150; i++)
        {
            await StoreAsync($"Cache row {i}", acme);
        }

        await StoreAsync("Globex row", Scope with { TenantId = "globex" });

        Assert.Equal(151, (await Inspector.ListAsync(null, Trace())).Count);
        Assert.Equal(150, (await Inspector.ListAsync("acme", Trace())).Count);

        Assert.Equal(151, await Inspector.ClearAsync(Trace()));

        Assert.Empty(await Inspector.ListAsync(null, Trace()));
        Assert.Equal(policy, await Policies.GetAsync("acme", Trace()));
        Assert.Equal("keep", (string?)await Database.StringGetAsync(otherKey));
        Assert.Equal("keep too", (string?)await Database.StringGetAsync(similarKey));
        Assert.NotNull(await Database.ExecuteAsync("FT.INFO", Keys.Index));
    }

    [Fact]
    public async Task Clearing_an_empty_cache_deletes_nothing()
    {
        Assert.Equal(0, await Inspector.ClearAsync(Trace()));
    }

    private async Task<string> StoreAsync(string question, CacheScope scope, bool shareable = false)
    {
        var entry = new CacheEntry(question, "Answer", scope, null, DateTimeOffset.UtcNow, shareable);

        await Cache.StoreAsync(entry, Trace());

        return Keys.ForEntry(question, scope);
    }
}
