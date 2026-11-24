using SemanticCache.Api.Infrastructure.Configuration;

namespace SemanticCache.Tests;

public sealed class DotEnvTests
{
    [Fact]
    public void Reads_filled_values_and_ignores_comments_and_templates()
    {
        var values = DotEnv.Parse(
        [
            "# comment",
            "",
            "Demo__RedisConnection=host:1,user=default,password=a=b",
            "#Demo__KeyPrefix=x",
            "AgentPlaybook__ApiKey=PASTE_PLAYBOOK_KEY",
            "Empty="
        ]);

        Assert.Equal("host:1,user=default,password=a=b", Assert.Single(values).Value);
        Assert.True(values.ContainsKey("Demo:RedisConnection"));
    }
}
