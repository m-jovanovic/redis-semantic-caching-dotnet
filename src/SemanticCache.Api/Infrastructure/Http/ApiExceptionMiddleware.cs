namespace SemanticCache.Api.Infrastructure.Http;

public sealed class ApiExceptionMiddleware(
    RequestDelegate next,
    ILogger<ApiExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex) when (!context.Response.HasStarted)
        {
            logger.LogError(ex, "Demo operation failed");

            context.Response.StatusCode = DemoErrors.StatusCode(ex);
            await context.Response.WriteAsJsonAsync(new { error = DemoErrors.Describe(ex) });
        }
    }
}
