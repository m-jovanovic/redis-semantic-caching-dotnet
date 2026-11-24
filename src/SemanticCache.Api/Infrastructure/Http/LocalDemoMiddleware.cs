namespace SemanticCache.Api.Infrastructure.Http;

/// <summary>Adds security headers and rejects POST requests from other sites.</summary>
public sealed class LocalDemoMiddleware(RequestDelegate next)
{
    private const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; " +
        "connect-src 'self'; frame-ancestors 'none'; base-uri 'self'";

    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;

        headers["X-Content-Type-Options"] = "nosniff";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Content-Security-Policy"] = ContentSecurityPolicy;

        if (HttpMethods.IsPost(context.Request.Method) && IsFromAnotherSite(context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;

            await context.Response.WriteAsJsonAsync(new
            {
                error = "Use the local demo UI for this operation."
            });

            return;
        }

        await next(context);
    }

    private static bool IsFromAnotherSite(HttpRequest request)
    {
        var origin = request.Headers.Origin.ToString();

        return origin.Length > 0 && origin != $"{request.Scheme}://{request.Host}";
    }
}
