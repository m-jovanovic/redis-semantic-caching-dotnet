using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SemanticCache.Api.Features.Caching;
using SemanticCache.Api.Infrastructure.Tracing;

namespace SemanticCache.Api.Features.Playbooks;

// https://redis.io/docs/latest/develop/ai/context-engine/agent-playbooks/
public sealed class AgentPlaybookClient(HttpClient http, IOptions<AgentPlaybookOptions> options)
{
    private readonly AgentPlaybookOptions _options = options.Value;

    public bool IsConfigured =>
        Uri.TryCreate(_options.EndpointUrl, UriKind.Absolute, out var endpoint) &&
        endpoint.Scheme == Uri.UriSchemeHttps &&
        _options.PlaybookId.Length > 0 &&
        _options.ApiKey.Length > 0;

    /// <summary>Finds the published FAQ closest to the question.</summary>
    public Task<PublishedFaq?> SearchAsync(
        string question,
        TraceContext trace,
        CancellationToken ct)
    {
        var request = new { query = question, limit = 1 };

        return trace.MeasureAsync<PublishedFaq?>(
            "playbook",
            "POST /faqs/search",
            input: request,
            action: async () =>
            {
                var response = await PostAsync<SearchResponse>("faqs/search", request, ct);

                return response.Items?.FirstOrDefault()?.ToPublishedFaq();
            },
            describe: faq => faq is null ? new { matched = false } : faq);
    }

    /// <summary>An outage costs one model call, not the request. The trace records the error.</summary>
    public async Task<PublishedFaq?> TrySearchAsync(
        string question,
        TraceContext trace,
        CancellationToken ct)
    {
        try
        {
            return await SearchAsync(question, trace, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            return null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>Suggests a cached answer for a person to review. Nothing is published.</summary>
    public Task<string> ProposeAsync(CachedAnswer cached, TraceContext trace, CancellationToken ct)
    {
        var faq = new { question = cached.Question, answer = cached.Answer };

        var request = new
        {
            source = new
            {
                kind = "semantic-cache",
                reference = new { cacheKey = cached.Id },
                content = new { hits = cached.Hits }
            },
            action = new
            {
                add = new
                {
                    target = new { newEntry = new { } },
                    candidate = new { content = new { payload = new { faq } } }
                }
            }
        };

        return trace.MeasureAsync(
            "playbook",
            "POST /proposals",
            input: new { question = cached.Question, hits = cached.Hits },
            action: async () =>
            {
                var response = await PostAsync<ProposalResponse>("proposals", request, ct);

                return response.ProposalId;
            },
            describe: proposalId => new { proposalId });
    }

    private async Task<T> PostAsync<T>(string path, object body, CancellationToken ct)
    {
        var endpoint = _options.EndpointUrl.TrimEnd('/');
        var playbook = Uri.EscapeDataString(_options.PlaybookId);
        var url = $"{endpoint}/iris/data/v1/agent-playbooks/playbooks/{playbook}/{path}";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        request.Content = JsonContent.Create(body);

        using var response = await http.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            throw await PlaybookException.FromAsync(response, ct);
        }

        return (await response.Content.ReadFromJsonAsync<T>(ct))!;
    }

    private sealed record SearchResponse(List<SearchItem>? Items);

    private sealed record SearchItem(string EntryId, int Version, double Score, Faq? Faq)
    {
        public PublishedFaq? ToPublishedFaq()
        {
            return Faq is null
                ? null
                : new PublishedFaq(EntryId, Version, Score, Faq.Question, Faq.Answer);
        }
    }

    private sealed record Faq(string Question, string Answer);

    private sealed record ProposalResponse(string ProposalId);
}
