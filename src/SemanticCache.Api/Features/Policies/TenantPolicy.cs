namespace SemanticCache.Api.Features.Policies;

/// <param name="Revision">Goes up with every change and retires older cached answers.</param>
public sealed record TenantPolicy(
    string Id,
    string Name,
    int Revision,
    int ExportLimit,
    int RetentionDays);
