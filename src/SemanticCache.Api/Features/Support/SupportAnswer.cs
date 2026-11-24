namespace SemanticCache.Api.Features.Support;

/// <param name="Reusable">Safe to store: a known policy fact that did not change on the way.</param>
/// <param name="Shareable">Safe to propose as an FAQ: the same for every tenant.</param>
public sealed record SupportAnswer(
    string Text,
    bool Reusable,
    bool Shareable,
    int PolicyRevision,
    string WhyNotReusable);
