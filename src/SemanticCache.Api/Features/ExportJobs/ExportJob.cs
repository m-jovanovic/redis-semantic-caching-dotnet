namespace SemanticCache.Api.Features.ExportJobs;

public sealed record ExportJob(
    string Id,
    string TenantId,
    string Status,
    int ProcessedRows,
    int TotalRows,
    int Revision,
    DateTimeOffset UpdatedAt);
