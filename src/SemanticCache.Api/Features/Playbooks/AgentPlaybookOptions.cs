namespace SemanticCache.Api.Features.Playbooks;

/// <summary>Set these in .env. The key needs the read and propose actions, nothing more.</summary>
public sealed class AgentPlaybookOptions
{
    public const string Section = "AgentPlaybook";

    public string EndpointUrl { get; set; } = "";
    public string PlaybookId { get; set; } = "";
    public string ApiKey { get; set; } = "";
}
