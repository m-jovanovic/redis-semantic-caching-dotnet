using SemanticCache.Api.Features.Policies;
using SemanticCache.Api.Infrastructure.Tracing;

namespace SemanticCache.Api.Features.Chat;

/// <param name="Verify">Runs the yes/no check on a close match.</param>
/// <param name="Playbooks">Searches the published FAQs before the cache.</param>
public sealed record ChatRequest(
    string Message,
    string Mode = CacheMode.Semantic,
    string TenantId = DemoTenants.Acme,
    string SessionId = "default",
    double? Threshold = null,
    bool Verify = true,
    bool Playbooks = false)
{
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Message) || Message.Length > 2000)
        {
            return "Enter a question between 1 and 2,000 characters.";
        }

        if (Mode is not (CacheMode.Off or CacheMode.Exact or CacheMode.Semantic))
        {
            return "Mode must be off, exact or semantic.";
        }

        if (!DemoTenants.IsKnown(TenantId))
        {
            return "Choose a demo tenant: acme or globex.";
        }

        if (!TraceHub.IsValidSession(SessionId))
        {
            return "Invalid session ID.";
        }

        if (Threshold is not (null or (>= 0 and <= 0.3)))
        {
            return "Distance threshold must be between 0 and 0.3.";
        }

        return null;
    }
}

public static class CacheMode
{
    public const string Off = "off";
    public const string Exact = "exact";
    public const string Semantic = "semantic";
}
