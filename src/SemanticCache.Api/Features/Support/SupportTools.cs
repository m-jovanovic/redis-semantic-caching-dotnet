using System.Text.Json;
using System.Text.Json.Nodes;
using SemanticCache.Api.Features.ExportJobs;
using SemanticCache.Api.Features.Policies;
using SemanticCache.Api.Infrastructure.Ollama;
using SemanticCache.Api.Infrastructure.Tracing;

namespace SemanticCache.Api.Features.Support;

public sealed class SupportTools(PolicyStore policies, ExportJobStore jobs)
{
    private const string PolicyTool = "get_support_policy";
    private const string ExportJobTool = "get_export_job";

    public static readonly JsonArray Definitions = new(
        Define(
            PolicyTool,
            "Read one support fact: the export row limit, the log retention period, or the steps " +
            "to rotate an API key. Use topic other when the question is not about a listed topic, " +
            "for example columns, imports, backups or durations.",
            properties: new JsonObject
            {
                ["topic"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = new JsonArray(
                        SupportTopic.ExportRowLimit,
                        SupportTopic.LogRetention,
                        SupportTopic.ApiKeyRotation,
                        SupportTopic.Other)
                }
            },
            required: new JsonArray("topic")),
        Define(
            ExportJobTool,
            "Read the live status of the current tenant's export jobs: running or finished, " +
            "and rows processed. Not for limits.",
            properties: new JsonObject(),
            required: new JsonArray()));

    public Task<ToolResult> RunAsync(ToolCall call, TenantPolicy policy, TraceContext trace)
    {
        return call.Name switch
        {
            PolicyTool => ReadPolicyAsync(call.Arguments, policy, trace),
            ExportJobTool => ReadExportJobsAsync(policy, trace),
            _ => throw new InvalidOperationException(
                "The model requested an unknown tool. Nothing was cached; retry the request.")
        };
    }

    private async Task<ToolResult> ReadPolicyAsync(
        JsonObject? arguments,
        TenantPolicy policy,
        TraceContext trace)
    {
        var topic = arguments?["topic"]?.GetValue<string>();

        var facts = await trace.MeasureAsync(
            "tool",
            PolicyTool,
            input: new { arguments, tenantId = policy.Id },
            action: async () =>
            {
                // Read again: the policy may have changed since the request started.
                var current = await policies.GetAsync(policy.Id, trace);

                return SupportFacts.For(current, topic);
            });

        var revision = facts["revision"]!.GetValue<int>();

        return new ToolResult(
            facts,
            Reusable: SupportTopic.IsKnown(topic) && revision == policy.Revision,
            Shareable: topic == SupportTopic.ApiKeyRotation,
            PolicyRevision: revision);
    }

    private async Task<ToolResult> ReadExportJobsAsync(TenantPolicy policy, TraceContext trace)
    {
        var status = await trace.MeasureAsync(
            "tool",
            ExportJobTool,
            input: new { tenantId = policy.Id },
            action: async () =>
            {
                var current = await jobs.ListAsync(policy.Id, trace);

                return JsonSerializer.SerializeToNode(current)!;
            });

        // Live data. Read it again next time.
        return new ToolResult(status, Reusable: false, Shareable: false);
    }

    private static JsonObject Define(
        string name,
        string description,
        JsonObject properties,
        JsonArray required)
    {
        return new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = name,
                ["description"] = description,
                ["parameters"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = properties,
                    ["required"] = required,
                    ["additionalProperties"] = false
                }
            }
        };
    }
}

public sealed record ToolResult(
    JsonNode Content,
    bool Reusable,
    bool Shareable,
    int? PolicyRevision = null);
