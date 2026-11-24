using SemanticCache.Api.Features.Policies;
using SemanticCache.Api.Features.Support;

namespace SemanticCache.Tests;

public sealed class SupportFactsTests
{
    private static readonly TenantPolicy Policy = new("acme", "Acme", 7, 5000, 30);

    [Theory]
    [InlineData(SupportTopic.ExportRowLimit, "exportRowLimit")]
    [InlineData(SupportTopic.LogRetention, "logRetentionDays")]
    public void Tenant_topics_return_only_the_requested_fact_and_its_scope(string topic, string fact)
    {
        var facts = SupportFacts.For(Policy, topic);

        Assert.Equal(5, facts.Count);
        Assert.Equal("Acme", facts["tenant"]!.GetValue<string>());
        Assert.Equal("acme", facts["tenantId"]!.GetValue<string>());
        Assert.Equal(7, facts["revision"]!.GetValue<int>());
        Assert.True(facts.ContainsKey(fact));
        Assert.True(facts.ContainsKey("covers"));
    }

    [Fact]
    public void Key_rotation_contains_nothing_about_the_tenant()
    {
        var facts = SupportFacts.For(Policy, SupportTopic.ApiKeyRotation);

        Assert.Equal(3, facts.Count);
        Assert.Contains("Settings > API keys", facts["apiKeyRotation"]!.GetValue<string>());
        Assert.DoesNotContain("acme", facts.ToJsonString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Export_fact_uses_the_current_policy_value()
    {
        var facts = SupportFacts.For(Policy, SupportTopic.ExportRowLimit);

        Assert.Equal(5000, facts["exportRowLimit"]!.GetValue<int>());
    }

    [Theory]
    [InlineData(SupportTopic.Other)]
    [InlineData(null)]
    public void Other_questions_keep_the_full_policy_and_explicit_unknowns(string? topic)
    {
        var facts = SupportFacts.For(Policy, topic);

        Assert.Equal(8, facts.Count);
        Assert.Equal(5000, facts["exportRowLimit"]!.GetValue<int>());
        Assert.Equal(30, facts["logRetentionDays"]!.GetValue<int>());
        Assert.Contains("Settings > API keys", facts["apiKeyRotation"]!.GetValue<string>());
        Assert.StartsWith("Unknown.", facts["exportDuration"]!.GetValue<string>());
        Assert.StartsWith("Unavailable.", facts["liveAccountData"]!.GetValue<string>());
    }
}
