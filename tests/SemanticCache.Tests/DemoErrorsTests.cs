using SemanticCache.Api.Infrastructure.Http;
using StackExchange.Redis;

namespace SemanticCache.Tests;

public sealed class DemoErrorsTests
{
    [Fact]
    public void Provider_errors_offer_recovery_without_echoing_raw_service_content()
    {
        var message = DemoErrors.Describe(
            new HttpRequestException("Raw provider response with implementation details"));

        Assert.Contains("Ollama", message);
        Assert.Contains("service status", message);
        Assert.DoesNotContain("Raw provider", message);
    }

    [Fact]
    public void Redis_timeouts_name_the_failed_service_without_showing_connection_diagnostics()
    {
        var message = DemoErrors.Describe(new RedisTimeoutException(
            CommandFlags.None,
            "verbose connection diagnostics",
            CommandStatus.Unknown));

        Assert.Contains("Redis", message);
        Assert.Contains("docker compose up", message);
        Assert.DoesNotContain("diagnostics", message);
    }

    [Fact]
    public void Cancellation_explains_model_warmup()
    {
        var message = DemoErrors.Describe(new OperationCanceledException());

        Assert.Contains("models load", message);
    }

    [Fact]
    public void Known_demo_messages_remain_actionable()
    {
        const string detail = "The model returned no answer. Nothing was cached; retry the request.";

        Assert.Equal(detail, DemoErrors.Describe(new InvalidOperationException(detail)));
    }

    [Fact]
    public void Unexpected_errors_point_to_logs_without_exposing_exception_details()
    {
        var message = DemoErrors.Describe(new Exception("internal implementation detail"));

        Assert.Contains("application log", message);
        Assert.DoesNotContain("internal implementation detail", message);
    }

    [Fact]
    public void Only_invalid_input_is_a_client_error()
    {
        Assert.Equal(400, DemoErrors.StatusCode(new ArgumentException("bad input")));
        Assert.Equal(503, DemoErrors.StatusCode(new HttpRequestException("offline")));
    }
}
