namespace SemanticCache.Api.Features.Chat;

public sealed record ChatReply(
    string Source,
    string Answer,
    string Reason,
    int? PolicyRevision = null);

public static class AnswerSource
{
    public const string Playbook = "playbook";
    public const string Exact = "exact";
    public const string Semantic = "semantic";
    public const string Model = "model";

    /// <summary>A model answer that must not be stored.</summary>
    public const string Bypass = "bypass";
}
