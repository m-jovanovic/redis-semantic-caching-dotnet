using System.Net;
using System.Text.Json;

namespace SemanticCache.Api.Features.Playbooks;

/// <summary>Agent Playbooks said no. Type and ExistingId say why.</summary>
public sealed class PlaybookException(HttpStatusCode status, string? type, string? existingId)
    : HttpRequestException(Describe(status, type, existingId), inner: null, status)
{
    private const string AlreadyExists = "/errors/proposal-already-exists";

    public string? Type { get; } = type;
    public string? ExistingId { get; } = existingId;

    public static async Task<PlaybookException> FromAsync(
        HttpResponseMessage response,
        CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);

        try
        {
            var problem = JsonSerializer.Deserialize<Problem>(body, JsonSerializerOptions.Web);

            return new PlaybookException(response.StatusCode, problem?.Type, problem?.ExistingId);
        }
        catch (JsonException)
        {
            return new PlaybookException(response.StatusCode, type: null, existingId: null);
        }
    }

    private static string Describe(HttpStatusCode status, string? type, string? existingId)
    {
        if (type != AlreadyExists)
        {
            return $"Agent Playbooks returned HTTP {(int)status}{(type is null ? "." : $" ({type}).")}";
        }

        return
            $"This answer was already proposed{(existingId is null ? "" : $" as {existingId}")}. " +
            "Check the Inbox, then the Trash: a proposal there can also block a new one.";
    }

    private sealed record Problem(string? Type, string? ExistingId);
}
