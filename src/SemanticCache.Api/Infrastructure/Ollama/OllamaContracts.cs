using System.Text.Json.Nodes;

namespace SemanticCache.Api.Infrastructure.Ollama;

public sealed record GenerationOptions(
    int Seed,
    int NumPredict,
    int NumCtx = 4096,
    double Temperature = 0);

public sealed record Embedding(float[] Vector, int Tokens);

public sealed record ModelReply(
    JsonObject Message,
    int InputTokens,
    int OutputTokens,
    bool Truncated)
{
    public string Text => Message["content"]?.GetValue<string>().Trim() ?? "";

    public IReadOnlyList<ToolCall> ToolCalls => ToolCall.ReadAll(Message);
}

public sealed record ToolCall(string? Name, JsonObject? Arguments)
{
    public static IReadOnlyList<ToolCall> ReadAll(JsonObject message)
    {
        if (message["tool_calls"] is not JsonArray calls)
        {
            return [];
        }

        return calls
            .Select(call => call?["function"])
            .Select(function => new ToolCall(
                function?["name"]?.GetValue<string>(),
                function?["arguments"] as JsonObject))
            .ToList();
    }
}

public static class ChatMessage
{
    public static JsonObject System(string text)
    {
        return new JsonObject { ["role"] = "system", ["content"] = text };
    }

    public static JsonObject User(string text)
    {
        return new JsonObject { ["role"] = "user", ["content"] = text };
    }

    public static JsonObject Tool(string? name, JsonNode content)
    {
        return new JsonObject
        {
            ["role"] = "tool",
            ["tool_name"] = name,
            ["content"] = content.ToJsonString()
        };
    }
}
