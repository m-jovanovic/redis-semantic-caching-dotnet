#:project ../src/SemanticCache.Api/SemanticCache.Api.csproj
#:property TreatWarningsAsErrors=false
#:property PublishAot=false

// Runs the app's own embedder and yes/no check over 27 hand-labeled question pairs.
//
//   docker compose up -d
//   dotnet run evaluation/evaluate.cs

using Microsoft.Extensions.Options;
using SemanticCache.Api.Features.Caching;
using SemanticCache.Api.Infrastructure.Configuration;
using SemanticCache.Api.Infrastructure.Ollama;
using SemanticCache.Api.Infrastructure.Tracing;

Group[] groups =
[
    new("How many rows can I export?",
        Same:
        [
            "What is the maximum number of rows in an export?",
            "What's the row limit for exports?",
            "How big can my export be, in rows?",
            "Is there a cap on exported rows?",
            "max rows per csv export",
            "Can I export 50,000 rows at once?"
        ],
        Different:
        [
            "How many rows can I import?",
            "How many exports can I run per day?",
            "How long do exports take?",
            "Has my export finished?",
            "How many columns can I export?",
            "How do I export rows?"
        ]),

    new("How do I rotate an API key?",
        Same:
        [
            "What's the process for rotating my API key?",
            "How can I replace my API key safely?",
            "Steps to roll over API keys",
            "I need to swap out my API key, how?"
        ],
        Different:
        [
            "How do I delete an API key?",
            "How do I create an API key?",
            "How do I rotate a webhook secret?",
            "How often should I rotate an API key?",
            "Did my API key rotation finish?"
        ]),

    new("What is the log retention period?",
        Same:
        [
            "How long do you keep logs?",
            "How many days are logs stored?",
            "When do my logs get deleted?"
        ],
        Different:
        [
            "What is the backup retention period?",
            "How do I change the log retention period?",
            "How do I download logs?"
        ])
];

var settings = new DemoOptions();
settings.OllamaUrl = Environment.GetEnvironmentVariable("OLLAMA_URL") ?? settings.OllamaUrl;

var options = Options.Create(settings);
var ollama = new OllamaClient(new HttpClient { BaseAddress = new Uri(settings.OllamaUrl) }, options);
var embedder = new QuestionEmbedder(ollama, options);
var check = new SameQuestionCheck(ollama, options);
var trace = new TraceHub().Start("evaluation");

var results = new List<PairResult>();

foreach (var group in groups)
{
    var known = await embedder.EmbedAsync(group.Known, trace, CancellationToken.None);
    var pairs = group.Same.Select(q => (q, true)).Concat(group.Different.Select(q => (q, false)));

    Console.WriteLine($"\n{group.Known}");

    foreach (var (question, same) in pairs)
    {
        var asked = await embedder.EmbedAsync(question, trace, CancellationToken.None);
        var distance = CosineDistance(known.Vector, asked.Vector);
        var verdict = await check.RunAsync(group.Known, question, trace, CancellationToken.None);

        results.Add(new PairResult(same, distance, verdict.Same));

        Console.WriteLine(
            $"  {(same ? "same     " : "different")}  {distance:F4}  " +
            $"{(verdict.Same ? "YES" : "no "),-4} {question}");
    }
}

var sameTotal = results.Count(r => r.Same);
var differentTotal = results.Count(r => !r.Same);

Console.WriteLine("\n| Max distance | Check | Right reuse | Wrong reuse |\n| --- | --- | --- | --- |");

foreach (var limit in (double[])[0.08, 0.12, 0.20, 0.25, 0.30])
{
    Report(limit, useCheck: false);
    Report(limit, useCheck: true);
}

void Report(double limit, bool useCheck)
{
    var reused = results.Where(r => r.Distance <= limit && (!useCheck || r.Accepted)).ToList();

    Console.WriteLine(
        $"| {limit:F2} | {(useCheck ? "on" : "off")} | " +
        $"{reused.Count(r => r.Same)}/{sameTotal} | {reused.Count(r => !r.Same)}/{differentTotal} |");
}

static double CosineDistance(float[] a, float[] b)
{
    double dot = 0, lengthA = 0, lengthB = 0;

    for (var i = 0; i < a.Length; i++)
    {
        dot += a[i] * b[i];
        lengthA += a[i] * a[i];
        lengthB += b[i] * b[i];
    }

    return 1 - dot / Math.Sqrt(lengthA * lengthB);
}

internal sealed record Group(string Known, string[] Same, string[] Different);

internal sealed record PairResult(bool Same, double Distance, bool Accepted);
