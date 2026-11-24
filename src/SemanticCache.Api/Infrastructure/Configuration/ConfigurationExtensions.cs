namespace SemanticCache.Api.Infrastructure.Configuration;

public static class ConfigurationExtensions
{
    /// <summary>User secrets, then .env, then environment variables, then the command line.</summary>
    public static void AddDemoConfiguration(this WebApplicationBuilder builder, string[] args)
    {
        var envFile = Path.Combine(builder.Environment.ContentRootPath, "..", "..", ".env");

        builder.Configuration
            .AddUserSecrets<Program>(optional: true)
            .AddInMemoryCollection(DotEnv.Read(envFile))
            .AddEnvironmentVariables()
            .AddCommandLine(args);

        builder.Services.Configure<DemoOptions>(
            builder.Configuration.GetSection(DemoOptions.Section));

        if (string.IsNullOrEmpty(builder.Configuration["urls"]))
        {
            builder.WebHost.UseUrls("http://localhost:5186");
        }
    }
}
