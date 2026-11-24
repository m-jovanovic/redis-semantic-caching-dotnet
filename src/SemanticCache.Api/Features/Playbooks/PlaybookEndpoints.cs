using SemanticCache.Api.Features.Caching;
using SemanticCache.Api.Infrastructure.Tracing;

namespace SemanticCache.Api.Features.Playbooks;

public static class PlaybookEndpoints
{
    public static void MapPlaybooks(this WebApplication app)
    {
        app.MapPost("/api/playbook/proposals", ProposeAsync);
    }

    private static async Task<IResult> ProposeAsync(
        ProposeRequest request,
        AgentPlaybookClient playbook,
        AnswerCacheInspector inspector,
        TraceHub hub,
        ILogger<AgentPlaybookClient> logger,
        CancellationToken ct)
    {
        if (!playbook.IsConfigured)
        {
            return Results.BadRequest(new { error = "Agent Playbooks is not configured." });
        }

        using var trace = hub.Start(request.SessionId);

        var cached = await inspector.FindShareableAsync(request.Id, trace);

        if (cached is null)
        {
            return Results.BadRequest(new
            {
                error = "Only tenant-neutral cached answers can be proposed."
            });
        }

        try
        {
            var proposalId = await playbook.ProposeAsync(cached, trace, ct);

            return Results.Ok(new { proposalId });
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Proposal failed");

            return Results.Json(
                new { error = ex.Message },
                statusCode: StatusCodes.Status502BadGateway);
        }
    }
}

public sealed record ProposeRequest(string Id, string? SessionId);
