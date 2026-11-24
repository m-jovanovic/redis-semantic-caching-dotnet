using StackExchange.Redis;

namespace SemanticCache.Api.Infrastructure.Http;

public static class DemoErrors
{
    public static int StatusCode(Exception error)
    {
        return error is ArgumentException
            ? StatusCodes.Status400BadRequest
            : StatusCodes.Status503ServiceUnavailable;
    }

    public static string Describe(Exception error)
    {
        return error switch
        {
            RedisConnectionException or RedisTimeoutException =>
                "Redis is unavailable or took too long to respond. " +
                "Run docker compose up -d, then refresh service status.",

            HttpRequestException =>
                "The local Ollama request failed. " +
                "Check that Ollama is running and both models are installed, " +
                "then refresh service status and try again. " +
                "Details are in the application log.",

            OperationCanceledException =>
                "The request timed out or was cancelled. " +
                "The first request can take longer while models load. " +
                "Check service status before trying again.",

            ArgumentException or InvalidOperationException => error.Message,

            _ =>
                "The request could not complete. " +
                "Check the application log for details, then refresh service status."
        };
    }
}
