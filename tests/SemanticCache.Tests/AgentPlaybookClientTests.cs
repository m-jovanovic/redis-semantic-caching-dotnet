using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SemanticCache.Api.Features.Caching;
using SemanticCache.Api.Features.Playbooks;
using SemanticCache.Api.Infrastructure.Tracing;

namespace SemanticCache.Tests;

public sealed class AgentPlaybookClientTests
{
    private const string Match =
        """
        {
          "items": [
            {
              "entryId": "ent_1",
              "kind": "FAQ",
              "version": 3,
              "score": 0.91,
              "intents": [],
              "faq": {
                "question": "How do I rotate an API key?",
                "answer": "Create a new one."
              }
            }
          ]
        }
        """;

    [Fact]
    public async Task Search_sends_the_question_and_reads_the_published_version()
    {
        var handler = new StubHandler(HttpStatusCode.OK, Match);

        var faq = await Client(handler).SearchAsync("Rotate key?", Trace(), CancellationToken.None);

        Assert.Equal(
            new PublishedFaq("ent_1", 3, 0.91, "How do I rotate an API key?", "Create a new one."),
            faq);

        Assert.Equal(
            "https://example.test/iris/data/v1/agent-playbooks/playbooks/pb_1/faqs/search",
            handler.Uri);

        Assert.Equal("Bearer test-key", handler.Authorization);

        var body = JsonDocument.Parse(handler.Body!).RootElement;
        Assert.Equal("Rotate key?", body.GetProperty("query").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, """{"items":[]}""")]
    [InlineData(HttpStatusCode.OK, """{"items":[{"entryId":"ent_1","kind":"SKILL"}]}""")]
    [InlineData(HttpStatusCode.OK, "{}")]
    [InlineData(HttpStatusCode.OK, "not json")]
    [InlineData(HttpStatusCode.InternalServerError, "{}")]
    public async Task No_match_or_an_unusable_response_falls_through(HttpStatusCode status, string body)
    {
        var client = Client(new StubHandler(status, body));

        Assert.Null(await client.TrySearchAsync("Anything", Trace(), CancellationToken.None));
    }

    [Fact]
    public async Task Proposal_uses_the_documented_new_entry_shape()
    {
        var handler = new StubHandler(HttpStatusCode.Created, """{"proposalId":"prp_1"}""");

        var proposalId = await Client(handler).ProposeAsync(Cached(), Trace(), CancellationToken.None);

        Assert.Equal("prp_1", proposalId);

        var body = JsonDocument.Parse(handler.Body!).RootElement;
        var source = body.GetProperty("source");
        var add = body.GetProperty("action").GetProperty("add");

        Assert.Equal("semantic-cache", source.GetProperty("kind").GetString());
        Assert.Equal("cache:1", source.GetProperty("reference").GetProperty("cacheKey").GetString());
        Assert.Equal(7, source.GetProperty("content").GetProperty("hits").GetInt32());

        Assert.Equal(
            JsonValueKind.Object,
            add.GetProperty("target").GetProperty("newEntry").ValueKind);

        var faq = add
            .GetProperty("candidate")
            .GetProperty("content")
            .GetProperty("payload")
            .GetProperty("faq");

        Assert.Equal("Q?", faq.GetProperty("question").GetString());
        Assert.Equal("A.", faq.GetProperty("answer").GetString());
    }

    [Fact]
    public async Task A_duplicate_proposal_names_the_existing_one()
    {
        const string conflict =
            """
            { "type": "/errors/proposal-already-exists", "status": 409, "existingId": "prp_9" }
            """;

        var client = Client(new StubHandler(HttpStatusCode.Conflict, conflict));

        var error = await Assert.ThrowsAsync<PlaybookException>(
            () => client.ProposeAsync(Cached(), Trace(), CancellationToken.None));

        Assert.Equal("prp_9", error.ExistingId);
        Assert.Contains("already proposed as prp_9", error.Message);
    }

    [Theory]
    [InlineData("""{ "type": "/errors/content-limit-exceeded" }""", "content-limit-exceeded")]
    [InlineData("not json", "HTTP 409.")]
    public async Task Other_conflicts_are_not_reported_as_duplicates(string body, string expected)
    {
        var client = Client(new StubHandler(HttpStatusCode.Conflict, body));

        var error = await Assert.ThrowsAsync<PlaybookException>(
            () => client.ProposeAsync(Cached(), Trace(), CancellationToken.None));

        Assert.Contains(expected, error.Message);
        Assert.DoesNotContain("already proposed", error.Message);
    }

    [Fact]
    public async Task The_key_never_reaches_the_trace()
    {
        var trace = Trace();
        var client = Client(new StubHandler(HttpStatusCode.Forbidden, "{}"));

        await client.TrySearchAsync("Anything", trace, CancellationToken.None);

        Assert.DoesNotContain("test-key", JsonSerializer.Serialize(trace.Events));
    }

    [Theory]
    [InlineData("http://example.test", "pb_1", "key")]
    [InlineData("https://example.test", "", "key")]
    [InlineData("https://example.test", "pb_1", "")]
    public void Missing_or_insecure_settings_disable_it(string endpoint, string id, string key)
    {
        var options = new AgentPlaybookOptions { EndpointUrl = endpoint, PlaybookId = id, ApiKey = key };
        var client = new AgentPlaybookClient(new HttpClient(), Options.Create(options));

        Assert.False(client.IsConfigured);
    }

    private static AgentPlaybookClient Client(StubHandler handler)
    {
        var options = new AgentPlaybookOptions
        {
            EndpointUrl = "https://example.test",
            PlaybookId = "pb_1",
            ApiKey = "test-key"
        };

        return new AgentPlaybookClient(new HttpClient(handler), Options.Create(options));
    }

    private static CachedAnswer Cached()
    {
        return new CachedAnswer(
            "cache:1",
            "Q?",
            "A.",
            "acme",
            Revision: 1,
            DateTimeOffset.UtcNow,
            Hits: 7,
            Shareable: true,
            VectorDimensions: 768,
            TtlSeconds: 60);
    }

    private static TraceContext Trace()
    {
        return new TraceHub().Start("test");
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? Uri { get; private set; }
        public string? Authorization { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct)
        {
            Uri = request.RequestUri!.ToString();
            Authorization = request.Headers.Authorization?.ToString();
            Body = await request.Content!.ReadAsStringAsync(ct);

            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }
    }
}
