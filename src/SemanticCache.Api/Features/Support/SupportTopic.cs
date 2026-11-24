namespace SemanticCache.Api.Features.Support;

/// <summary>The topics the model can ask get_support_policy about.</summary>
public static class SupportTopic
{
    public const string ExportRowLimit = "export_row_limit";
    public const string LogRetention = "log_retention";
    public const string ApiKeyRotation = "api_key_rotation";
    public const string Other = "other";

    public static bool IsKnown(string? topic)
    {
        return topic is ExportRowLimit or LogRetention or ApiKeyRotation;
    }
}
