using System.Text.Json.Nodes;
using SemanticCache.Api.Features.Policies;

namespace SemanticCache.Api.Features.Support;

/// <summary>The facts behind get_support_policy, one topic at a time.</summary>
public static class SupportFacts
{
    /// <summary>Change this when the wording below changes, so older answers are not reused.</summary>
    public const string Version = "support-assistant-v5";

    public const string KeyRotation =
        "Create a new API key in Settings > API keys. " +
        "Update your application to use the new key, verify it works, then revoke the old key. " +
        "Never share secret keys.";

    public const string ExportDuration =
        "Unknown. Export duration is not part of this policy " +
        "and cannot be determined from the row limit.";

    public const string NoLiveData = "Unavailable. This tool only exposes policy facts.";

    public static JsonObject For(TenantPolicy policy, string? topic)
    {
        return topic switch
        {
            SupportTopic.ExportRowLimit => ExportRowLimit(policy),
            SupportTopic.LogRetention => LogRetention(policy),
            SupportTopic.ApiKeyRotation => ApiKeyRotation(policy),
            _ => Everything(policy)
        };
    }

    private static JsonObject ExportRowLimit(TenantPolicy policy)
    {
        return new JsonObject
        {
            ["tenant"] = policy.Name,
            ["tenantId"] = policy.Id,
            ["revision"] = policy.Revision,
            ["exportRowLimit"] = policy.ExportLimit,
            ["covers"] =
                "Only the maximum number of ROWS in one export. " +
                "Columns, imports, file size and number of exports are unknown."
        };
    }

    private static JsonObject LogRetention(TenantPolicy policy)
    {
        return new JsonObject
        {
            ["tenant"] = policy.Name,
            ["tenantId"] = policy.Id,
            ["revision"] = policy.Revision,
            ["logRetentionDays"] = policy.RetentionDays,
            ["covers"] =
                "Only how long LOGS are kept. " +
                "Backups and how to change the period are unknown."
        };
    }

    // No tenant in the facts, so no tenant in the answer. Every customer gets the same steps.
    private static JsonObject ApiKeyRotation(TenantPolicy policy)
    {
        return new JsonObject
        {
            ["revision"] = policy.Revision,
            ["apiKeyRotation"] = KeyRotation,
            ["covers"] =
                "Only the steps to ROTATE an API key. " +
                "Other keys, how often to rotate, and live status are unknown."
        };
    }

    private static JsonObject Everything(TenantPolicy policy)
    {
        return new JsonObject
        {
            ["tenant"] = policy.Name,
            ["tenantId"] = policy.Id,
            ["revision"] = policy.Revision,
            ["exportRowLimit"] = policy.ExportLimit,
            ["exportDuration"] = ExportDuration,
            ["logRetentionDays"] = policy.RetentionDays,
            ["apiKeyRotation"] = KeyRotation,
            ["liveAccountData"] = NoLiveData
        };
    }
}
