namespace SemanticCache.Api.Features.Caching;

/// <summary>An answer may only be reused inside the scope it was created in.</summary>
public sealed record CacheScope(string TenantId, int Revision, string Configuration);
