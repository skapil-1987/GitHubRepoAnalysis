using System.Text;
using System.Text.Json;
using GithubRepoAnalysis.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GithubRepoAnalysis.Services;

public class OpenAIService : IOpenAIService
{
    public const string HttpClientName = "AzureOpenAI";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OpenAIService> _logger;

    public OpenAIService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<OpenAIService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<(IReadOnlyDictionary<string, int?> CodeQualityScores, string AiSummary)> GetBatchRepoInsightAsync(
        IReadOnlyList<(AnalyzeResponse Summary, CodeMetrics Metrics)> repos, CancellationToken ct = default)
    {
        var empty = (CodeQualityScores: (IReadOnlyDictionary<string, int?>)new Dictionary<string, int?>(), AiSummary: "AI analysis unavailable");

        var endpoint   = _configuration["AzureOpenAI:Endpoint"];
        var apiKey     = _configuration["AzureOpenAI:ApiKey"];
        var deployment = _configuration["AzureOpenAI:Deployment"];
        var apiVersion = _configuration["AzureOpenAI:ApiVersion"] ?? "2024-10-21";

        if (string.IsNullOrWhiteSpace(endpoint) ||
            string.IsNullOrWhiteSpace(apiKey) ||
            string.IsNullOrWhiteSpace(deployment))
        {
            _logger.LogInformation("Azure OpenAI not configured; skipping batch AI insights.");
            return empty;
        }

        var prompt = BuildBatchPrompt(repos);

        var payload = new
        {
            messages = new[]
            {
                new { role = "system", content = "You are an expert code reviewer. Always respond with valid JSON only, no markdown, no extra text." },
                new { role = "user",   content = prompt }
            },
            max_tokens  = 500,
            temperature = 0.2
        };

        var client = _httpClientFactory.CreateClient(HttpClientName);
        var url = $"{endpoint.TrimEnd('/')}/openai/deployments/{deployment}/chat/completions?api-version={apiVersion}";

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("api-key", apiKey);

        try
        {
            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("Azure OpenAI batch call returned {Status}: {Error}", response.StatusCode, error);
                return empty;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var raw = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? string.Empty;

            return ParseBatchResponse(raw, repos);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Azure OpenAI batch call failed.");
            return empty;
        }
    }

    private static string BuildBatchPrompt(IReadOnlyList<(AnalyzeResponse Summary, CodeMetrics Metrics)> repos)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are evaluating a student's top GitHub repositories.");
        sb.AppendLine("Below are the metadata and code metrics for each repository.");
        sb.AppendLine("Do NOT recalculate or change completenessScore or finalScore — use them only as context.");
        sb.AppendLine();

        for (int i = 0; i < repos.Count; i++)
        {
            var (s, m) = repos[i];
            sb.AppendLine($"===== Repository {i + 1}: {s.Repo} =====");
            sb.AppendLine($"- Languages       : {string.Join(", ", s.Languages)}");
            sb.AppendLine($"- Total Commits   : {s.TotalCommits}");
            sb.AppendLine($"- Completeness    : {s.CompletenessScore}");
            sb.AppendLine($"- Final Score     : {s.FinalScore}");
            sb.AppendLine($"- Code Files      : {m.CodeFiles}  |  Total LOC : {m.TotalLinesOfCode}  |  Avg LOC/file : {m.AverageLinesPerFile}");
            sb.AppendLine($"- Functions       : {m.FunctionCount}  |  Classes : {m.ClassCount}  |  Loops : {m.LoopCount}");
            sb.AppendLine($"- Conditionals    : {m.ConditionalCount}  |  Error Handling : {m.ErrorHandlingCount}  |  Imports : {m.ImportCount}");
            sb.AppendLine($"- Comment Lines   : {m.CommentLines}  |  Folder Structure : {m.HasStructure}  |  README size : {m.ReadmeSize}");
            sb.AppendLine();
        }

        sb.AppendLine("Provide the following in a single JSON response:");
        sb.AppendLine("1. repos: an array with a codeQualityScore (0-100) for each repository, in the same order as above.");
        sb.AppendLine("2. aiSummary: a concise 3-4 line summary covering overall code quality, recurring strengths, and areas to improve across all repos.");
        sb.AppendLine();
        sb.AppendLine("Return ONLY valid JSON in this exact format:");
        sb.AppendLine("{");
        sb.AppendLine("  \"repos\": [");
        sb.AppendLine("    { \"repo\": \"<full repo name>\", \"codeQualityScore\": <number> },");
        sb.AppendLine("    ...");
        sb.AppendLine("  ],");
        sb.AppendLine("  \"aiSummary\": \"<string>\"");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static (IReadOnlyDictionary<string, int?> CodeQualityScores, string AiSummary) ParseBatchResponse(
        string raw, IReadOnlyList<(AnalyzeResponse Summary, CodeMetrics Metrics)> repos)
    {
        var scores  = new Dictionary<string, int?>();
        var summary = "AI analysis unavailable";

        try
        {
            var json = raw.Trim().TrimStart('`');
            if (json.StartsWith("json", StringComparison.OrdinalIgnoreCase))
                json = json[4..].Trim();
            json = json.TrimEnd('`').Trim();

            using var doc  = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Parse per-repo codeQualityScore array
            if (root.TryGetProperty("repos", out var reposEl) && reposEl.ValueKind == JsonValueKind.Array)
            {
                var repoArray = reposEl.EnumerateArray().ToList();
                for (int i = 0; i < repoArray.Count && i < repos.Count; i++)
                {
                    var repoName = repos[i].Summary.Repo;
                    int? score   = repoArray[i].TryGetProperty("codeQualityScore", out var scoreEl)
                                   && scoreEl.TryGetInt32(out var s)
                        ? Math.Clamp(s, 0, 100)
                        : null;
                    scores[repoName] = score;
                }
            }

            // Parse shared aiSummary
            if (root.TryGetProperty("aiSummary", out var summaryEl))
                summary = summaryEl.GetString() ?? summary;
        }
        catch
        {
            // Return whatever was parsed so far
        }

        return (scores, summary);
    }
}
