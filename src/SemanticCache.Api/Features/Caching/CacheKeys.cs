using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using SemanticCache.Api.Infrastructure.Configuration;

namespace SemanticCache.Api.Features.Caching;

public sealed partial class CacheKeys(IOptions<DemoOptions> options)
{
    public string Index => options.Value.KeyPrefix + "idx";

    public string EntryPrefix => options.Value.KeyPrefix + "cache:";

    /// <summary>The same question in the same scope always produces the same key.</summary>
    public string ForEntry(string question, CacheScope scope)
    {
        var identity = JsonSerializer.SerializeToUtf8Bytes(new
        {
            scope.TenantId,
            scope.Revision,
            scope.Configuration,
            Question = Normalize(question)
        });

        return EntryPrefix + Convert.ToHexStringLower(SHA256.HashData(identity));
    }

    /// <summary>Hex keeps punctuation in a value from being read as search syntax.</summary>
    public static string Tag(string value)
    {
        return Convert.ToHexStringLower(Encoding.UTF8.GetBytes(value));
    }

    private static string Normalize(string question)
    {
        return Whitespace().Replace(question.Trim(), " ").ToLowerInvariant();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
