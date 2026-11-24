using SemanticCache.Api.Infrastructure.Http;
using SemanticCache.Api.Infrastructure.Tracing;

namespace SemanticCache.Api.Features.Chat;

public static class ChatEndpoints
{
    public static void MapChat(this WebApplication app)
    {
        app.MapPost("/api/chat", ReplyAsync);
    }

    private static async Task<IResult> ReplyAsync(
        ChatRequest request,
        ChatService chat,
        TraceHub hub,
        ILogger<ChatService> logger,
        CancellationToken ct)
    {
        if (request.Validate() is { } problem)
        {
            return Results.BadRequest(new { error = problem });
        }

        using var trace = hub.Start(request.SessionId);

        try
        {
            var question = request with { Message = request.Message.Trim() };

            return Results.Ok(await chat.ReplyAsync(question, trace, ct));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Chat request {RequestId} failed", trace.RequestId);

            var error = DemoErrors.Describe(ex);

            trace.Emit("request", "chat", "error", new { error });
            trace.Result = new { error, requestId = trace.RequestId };

            return Results.Json(trace.Result, statusCode: DemoErrors.StatusCode(ex));
        }
    }
}
