namespace SemanticCache.Api.Features.Playbooks;

public sealed record PublishedFaq(
    string EntryId,
    int Version,
    double Score,
    string Question,
    string Answer);
