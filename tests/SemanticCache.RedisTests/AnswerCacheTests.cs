using System.Buffers.Binary;
using System.Text.Json;
using SemanticCache.Api.Features.Caching;

namespace SemanticCache.RedisTests;

public sealed class AnswerCacheTests : RedisTest
{
    private static readonly CacheScope Scope = new("acme", 1, "model:sha256:fixture|prompt:v1");

    [Fact]
    public async Task Creating_the_index_twice_is_safe()
    {
        await Cache.CreateIndexAsync();

        Assert.NotNull(await Database.ExecuteAsync("FT.INFO", Keys.Index));
    }

    [Fact]
    public async Task Exact_entry_has_a_ttl_no_vector_and_conservative_question_normalization()
    {
        const string question = "What is the export limit?";
        await StoreAsync(question, Scope, vector: null);

        var match = await Cache.FindExactAsync("  WHAT  is\t the export limit?  ", Scope, Trace());

        Assert.NotNull(match);
        Assert.Equal(question, match.Question);
        Assert.False(await Database.HashExistsAsync(match.Id, "vector"));
        Assert.Equal(0, (int)await Database.HashGetAsync(match.Id, "vectorDimensions"));

        var ttl = await Database.KeyTimeToLiveAsync(match.Id);
        Assert.NotNull(ttl);
        Assert.InRange(ttl.Value.TotalSeconds, 1, 60);

        Assert.Null(await Cache.FindExactAsync("What is the export limit", Scope, Trace()));
        Assert.Null(await Cache.FindExactAsync(question, Scope with { TenantId = "globex" }, Trace()));
        Assert.Null(await Cache.FindExactAsync(question, Scope with { Revision = 2 }, Trace()));
        Assert.Null(await Cache.FindExactAsync(question, Scope with { Configuration = "other" }, Trace()));
        Assert.Null(await Cache.FindNearestAsync(Unit(0), Scope, Trace()));
    }

    [Fact]
    public async Task Overwriting_with_an_exact_entry_removes_the_previous_embedding()
    {
        const string question = "Overwrite this cached answer?";

        await StoreAsync(question, Scope, Unit(0));
        Assert.NotNull(await Cache.FindNearestAsync(Unit(0), Scope, Trace()));

        await StoreAsync(question, Scope, vector: null, "Exact-only replacement");
        var exact = await Cache.FindExactAsync(question, Scope, Trace());

        Assert.NotNull(exact);
        Assert.Equal("Exact-only replacement", exact.Answer);
        Assert.False(await Database.HashExistsAsync(exact.Id, "vector"));
        Assert.Null(await Cache.FindNearestAsync(Unit(0), Scope, Trace()));
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("revision")]
    [InlineData("configuration")]
    public async Task Nearest_search_filters_each_part_of_the_scope_before_knn(string part)
    {
        // The wrong scope has a perfect match and the right scope has distance 1.
        // Searching everything and filtering afterward would fail this test.
        var correct = Scope;

        var wrong = part switch
        {
            "tenant" => Scope with { TenantId = "globex" },
            "configuration" => Scope with { Configuration = "other-model" },
            _ => Scope
        };

        await StoreAsync("A closer but ineligible answer", wrong, Unit(0));

        if (part == "revision")
        {
            var policy = await Policies.UpdateExportLimitAsync("acme", 12000, Trace());
            correct = Scope with { Revision = policy.Revision };
        }

        await StoreAsync("The eligible answer", correct, Unit(1));

        var nearest = await Cache.FindNearestAsync(Unit(0), correct, Trace());

        Assert.NotNull(nearest);
        Assert.Equal("The eligible answer", nearest.Question);
        Assert.InRange(nearest.Distance, 0.99999, 1.00001);
        Assert.Equal(correct.TenantId, nearest.TenantId);
        Assert.Equal(correct.Revision, nearest.Revision);
    }

    [Fact]
    public async Task Vectors_are_stored_as_float32_and_scope_punctuation_is_treated_as_data()
    {
        var scope = Scope with { Configuration = "model,v2|@tenant:{other} \\ \" unicode-λ" };
        var vector = Unit(0);
        vector[1] = 0.5f;

        await StoreAsync("Vector shape", scope, vector);
        var match = await Cache.FindNearestAsync(vector, scope, Trace());

        Assert.NotNull(match);
        Assert.InRange(match.Distance, -0.00001, 0.00001);

        var bytes = (byte[]?)await Database.HashGetAsync(match.Id, "vector");

        Assert.NotNull(bytes);
        Assert.Equal(768 * sizeof(float), bytes.Length);
        Assert.Equal(1f, BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(0, 4)));
        Assert.Equal(0.5f, BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(4, 4)));
        Assert.Equal(768, (int)await Database.HashGetAsync(match.Id, "vectorDimensions"));

        var otherScope = scope with { Configuration = "model" };
        Assert.Null(await Cache.FindNearestAsync(vector, otherScope, Trace()));
    }

    [Fact]
    public async Task A_stale_write_changes_neither_the_answer_the_vector_nor_the_ttl()
    {
        const string question = "Original policy answer";
        await StoreAsync(question, Scope, Unit(0), "Original answer");

        var original = await Cache.FindExactAsync(question, Scope, Trace());
        Assert.NotNull(original);

        var ttlBefore = await Database.KeyTimeToLiveAsync(original.Id);
        await Policies.UpdateExportLimitAsync("acme", 20000, Trace());

        var trace = Trace();
        var stale = new CacheEntry(question, "Stale replacement", Scope, null, DateTimeOffset.UtcNow);
        await Cache.StoreAsync(stale, trace);

        var after = await Cache.FindExactAsync(question, Scope, Trace());
        Assert.NotNull(after);
        Assert.Equal("Original answer", after.Answer);
        Assert.True(await Database.HashExistsAsync(original.Id, "vector"));
        Assert.True(await Database.KeyTimeToLiveAsync(original.Id) <= ttlBefore);

        var outcome = trace.Events.Single(e => e is { Operation: "EVAL", Status: "completed" });
        var data = JsonSerializer.SerializeToElement(outcome.Data);
        Assert.False(data.GetProperty("stored").GetBoolean());
    }

    [Fact]
    public async Task A_policy_change_hides_old_answers_and_accepts_new_ones()
    {
        const string question = "Original policy answer";
        await StoreAsync(question, Scope, Unit(0), "Original answer");

        var policy = await Policies.UpdateExportLimitAsync("acme", 20000, Trace());
        var current = Scope with { Revision = policy.Revision };

        await StoreAsync("Brand new stale answer", Scope, Unit(0));
        Assert.Null(await Cache.FindExactAsync("Brand new stale answer", Scope, Trace()));

        Assert.Null(await Cache.FindExactAsync(question, current, Trace()));
        Assert.Null(await Cache.FindNearestAsync(Unit(0), current, Trace()));

        await StoreAsync("Fresh policy answer", current, Unit(0), "Current answer");
        var nearest = await Cache.FindNearestAsync(Unit(0), current, Trace());

        Assert.Equal("Current answer", nearest?.Answer);
    }

    [Fact]
    public async Task An_expired_entry_is_gone_from_every_read()
    {
        var shortLived = CacheWithTtl(1);

        await shortLived.StoreAsync(
            new CacheEntry("Expires soon", "Temporary", Scope, Unit(0), DateTimeOffset.UtcNow),
            Trace());

        Assert.NotNull(await Cache.FindExactAsync("Expires soon", Scope, Trace()));
        Assert.NotNull(await Cache.FindNearestAsync(Unit(0), Scope, Trace()));

        await Task.Delay(TimeSpan.FromMilliseconds(1250));

        Assert.Null(await Cache.FindExactAsync("Expires soon", Scope, Trace()));
        Assert.Null(await Cache.FindNearestAsync(Unit(0), Scope, Trace()));
        Assert.Empty(await Inspector.ListAsync(null, Trace()));
    }

    [Fact]
    public async Task Hits_are_counted_only_while_the_entry_exists()
    {
        await StoreAsync("Counted?", Scope, vector: null);
        var entry = await Cache.FindExactAsync("Counted?", Scope, Trace());

        await Cache.RecordHitAsync(entry!.Id, Trace());
        await Cache.RecordHitAsync(entry.Id, Trace());

        Assert.Equal(2, Assert.Single(await Inspector.ListAsync("acme", Trace())).Hits);

        await Database.KeyDeleteAsync(entry.Id);
        await Cache.RecordHitAsync(entry.Id, Trace());

        Assert.False(await Database.KeyExistsAsync(entry.Id));
    }

    [Fact]
    public async Task Traces_summarize_the_vector_without_its_bytes()
    {
        var trace = Trace();

        await Cache.StoreAsync(
            new CacheEntry("Traced vector", "Answer", Scope, Unit(0), DateTimeOffset.UtcNow),
            trace);

        await Cache.FindNearestAsync(Unit(0), Scope, trace);

        foreach (var operation in new[] { "EVAL", "FT.SEARCH" })
        {
            var events = trace.Events.Where(e => e.Operation == operation).ToList();
            var started = events.Single(e => e.Status == "started");
            var vector = JsonSerializer.SerializeToElement(started.Data).GetProperty("vector");

            Assert.Equal(768, vector.GetProperty("dimensions").GetInt32());
            Assert.Equal(3072, vector.GetProperty("bytes").GetInt32());
            Assert.Equal(64, vector.GetProperty("sha256").GetString()!.Length);
            Assert.Contains(events, e => e is { Status: "completed", DurationMs: >= 0 });
        }
    }

    private Task StoreAsync(
        string question,
        CacheScope scope,
        float[]? vector,
        string answer = "Fixture answer")
    {
        var entry = new CacheEntry(question, answer, scope, vector, DateTimeOffset.UtcNow);

        return Cache.StoreAsync(entry, Trace());
    }
}
