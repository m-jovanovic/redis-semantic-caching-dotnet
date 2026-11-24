using SemanticCache.Api.Features.Caching;
using SemanticCache.Api.Features.Chat;
using SemanticCache.Api.Features.Playbooks;
using SemanticCache.Api.Features.Policies;
using SemanticCache.Api.Features.Status;
using SemanticCache.Api.Infrastructure;
using SemanticCache.Api.Infrastructure.Configuration;
using SemanticCache.Api.Infrastructure.Http;
using SemanticCache.Api.Infrastructure.Tracing;

var builder = WebApplication.CreateBuilder(args);

builder.AddDemoConfiguration(args);

builder.Services.AddRedis();
builder.Services.AddOllama();
builder.Services.AddSemanticCache();
builder.Services.AddSupportChat();
builder.Services.AddAgentPlaybooks(builder.Configuration);

var app = builder.Build();

await app.PrepareRedisAsync();

app.UseMiddleware<LocalDemoMiddleware>();
app.UseMiddleware<ApiExceptionMiddleware>();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapChat();
app.MapCache();
app.MapPolicies();
app.MapPlaybooks();
app.MapStatus();
app.MapRequestInspector();
app.MapFallbackToFile("index.html");

await app.RunAsync();

public partial class Program;
