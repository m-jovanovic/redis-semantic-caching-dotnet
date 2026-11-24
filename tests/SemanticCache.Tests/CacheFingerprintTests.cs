using SemanticCache.Api.Features.Caching;

namespace SemanticCache.Tests;

public sealed class CacheFingerprintTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Changing_either_model_or_its_digest_changes_the_fingerprint(int changedIndex)
    {
        string[] original =
        [
            "qwen3:4b",
            "chat-digest-v1",
            "nomic-embed-text:v1.5",
            "embedding-digest-v1"
        ];

        var updated = (string[])original.Clone();
        updated[changedIndex] += "-changed";

        Assert.NotEqual(CacheFingerprint.Compute(original), CacheFingerprint.Compute(updated));
    }

    [Fact]
    public void The_same_setup_always_has_the_same_fingerprint()
    {
        Assert.Equal(CacheFingerprint.Compute("model"), CacheFingerprint.Compute("model"));
    }
}
