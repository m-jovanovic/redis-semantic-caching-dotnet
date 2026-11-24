namespace SemanticCache.Api.Infrastructure.Configuration;

/// <summary>Reads KEY=VALUE lines, so local secrets stay out of source control.</summary>
public static class DotEnv
{
    public static Dictionary<string, string?> Read(string path)
    {
        return File.Exists(path) ? Parse(File.ReadLines(path)) : [];
    }

    public static Dictionary<string, string?> Parse(IEnumerable<string> lines)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines.Select(line => line.Trim()))
        {
            var separator = line.IndexOf('=');

            if (line.StartsWith('#') || separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim().Replace("__", ":");
            var value = line[(separator + 1)..].Trim().Trim('"');

            // An unfilled template value must not override a working default.
            if (value.Length > 0 && !value.Contains("PASTE_", StringComparison.Ordinal))
            {
                values[key] = value;
            }
        }

        return values;
    }
}
