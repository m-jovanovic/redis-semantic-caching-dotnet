using SemanticCache.Api.Infrastructure.Tracing;

namespace SemanticCache.Api.Features.Policies;

public static class PolicyEndpoints
{
    public static void MapPolicies(this WebApplication app)
    {
        app.MapPost("/api/tenants/{id}/policy", UpdateAsync);
    }

    private static async Task<IResult> UpdateAsync(
        string id,
        PolicyUpdate update,
        string? sessionId,
        PolicyStore policies,
        TraceHub hub)
    {
        if (!DemoTenants.IsKnown(id) || update.ExportLimit is < 1 or > 1_000_000)
        {
            return Results.BadRequest(new
            {
                error = "Choose a demo tenant and an export limit between 1 and 1,000,000."
            });
        }

        using var trace = hub.Start(sessionId);

        return Results.Ok(await policies.UpdateExportLimitAsync(id, update.ExportLimit, trace));
    }
}

public sealed record PolicyUpdate(int ExportLimit);
