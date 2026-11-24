using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using SemanticCache.Api.Features.Chat;
using SemanticCache.Api.Features.Policies;
using SemanticCache.Api.Infrastructure.Configuration;
using SemanticCache.Api.Infrastructure.Ollama;
using SemanticCache.Api.Infrastructure.Tracing;

namespace SemanticCache.Api.Features.Support;

/// <summary>The expensive path: one model call to pick a tool, one to write the answer.</summary>
public sealed class SupportAgent(
    OllamaClient ollama,
    SupportTools tools,
    IOptions<DemoOptions> options)
{
    public static readonly GenerationOptions Generation = new(Seed: 42, NumPredict: 350);

    public async Task<SupportAnswer> AnswerAsync(
        string question,
        TenantPolicy policy,
        Usage usage,
        TraceContext trace,
        CancellationToken ct)
    {
        List<JsonObject> messages =
        [
            ChatMessage.System(SupportPrompt.Text),
            ChatMessage.User(question)
        ];

        // First call: the model picks a tool.
        var choice = await AskAsync(messages, SupportTools.Definitions, usage, trace, ct);

        if (choice.ToolCalls.Count == 0)
        {
            return new SupportAnswer(
                RequireText(choice),
                Reusable: false,
                Shareable: false,
                policy.Revision,
                WhyNotReusable: "the model answered without a tool.");
        }

        messages.Add(choice.Message);

        var results = new List<ToolResult>();

        foreach (var call in choice.ToolCalls)
        {
            usage.AddToolCall();

            var result = await tools.RunAsync(call, policy, trace);

            results.Add(result);
            messages.Add(ChatMessage.Tool(call.Name, result.Content));
        }

        // Second call: the model writes the answer from the facts.
        var answer = await AskAsync(messages, toolDefinitions: null, usage, trace, ct);

        return ToAnswer(RequireText(answer), results, policy);
    }

    private static SupportAnswer ToAnswer(
        string text,
        List<ToolResult> results,
        TenantPolicy policy)
    {
        var reusable = results.All(result => result.Reusable);
        var sameForEveryTenant = results.All(result => result.Shareable);
        var namesTenant = text.Contains(policy.Name, StringComparison.OrdinalIgnoreCase);

        var policyRevision = results
            .Select(result => result.PolicyRevision)
            .LastOrDefault(revision => revision is not null);

        return new SupportAnswer(
            text,
            reusable,
            Shareable: reusable && sameForEveryTenant && !namesTenant,
            policyRevision ?? policy.Revision,
            WhyNotReusable: "it used live data, or facts this demo does not cache.");
    }

    private async Task<ModelReply> AskAsync(
        List<JsonObject> messages,
        JsonArray? toolDefinitions,
        Usage usage,
        TraceContext trace,
        CancellationToken ct)
    {
        var model = options.Value.ChatModel;

        var reply = await trace.MeasureAsync(
            "model",
            "/api/chat",
            input: new { model, messages = messages.ToArray(), tools = toolDefinitions },
            action: () => ollama.ChatAsync(messages, Generation, toolDefinitions, ct),
            describe: result => new
            {
                model,
                message = result.Message,
                inputTokens = result.InputTokens,
                outputTokens = result.OutputTokens
            });

        usage.AddModelCall(reply);

        if (reply.Truncated)
        {
            throw new InvalidOperationException(
                "The local model reached its output limit. No partial answer was cached.");
        }

        return reply;
    }

    private static string RequireText(ModelReply reply)
    {
        if (reply.Text.Length == 0)
        {
            throw new InvalidOperationException(
                "The model returned no answer. Nothing was cached; retry the request.");
        }

        return reply.Text;
    }
}
