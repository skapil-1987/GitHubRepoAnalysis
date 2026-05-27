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

    // CHANGED: Now accepts code snippets per repo for actual code review by the AI.
    public async Task<(IReadOnlyDictionary<string, int?> CodeQualityScores, string AiSummary)> GetBatchRepoInsightAsync(
        IReadOnlyList<(AnalyzeResponse Summary, CodeMetrics Metrics, List<(string Path, string Content)> CodeSnippets)> repos,
        CancellationToken ct = default)
    {
        var empty = (CodeQualityScores: (IReadOnlyDictionary<string, int?>)new Dictionary<string, int?>(), AiSummary: "AI analysis unavailable");

        var endpoint   = _configuration["AzureOpenAI:Endpoint"];
        var apiKey     = _configuration["AzureOpenAI:ApiKey"];
        var deployment = _configuration["AzureOpenAI:Deployment"];
        var model      = _configuration["AzureOpenAI:Model"];
        var apiVersion = _configuration["AzureOpenAI:ApiVersion"] ?? "2024-10-21";

        if (string.IsNullOrWhiteSpace(endpoint) ||
            string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogInformation("Azure OpenAI not configured; skipping batch AI insights.");
            return empty;
        }

        var isServerless = string.IsNullOrWhiteSpace(deployment);
        if (isServerless)
            _logger.LogInformation("No deployment name configured; using Foundry serverless endpoint.");

        var prompt = BuildBatchPrompt(repos);

        // For serverless, model name must be in the request body.
        // For managed deployments, model is implied by the deployment name in the URL.
        object payload = isServerless
            ? new
            {
                model = model ?? "gpt-4o-mini",
                messages = new[]
                {
                    new { role = "system", content = "You are an expert code reviewer. Always respond with valid JSON only, no markdown, no extra text." },
                    new { role = "user",   content = prompt }
                },
                max_tokens  = 2000,
                temperature = 0.2
            }
            : (object)new
            {
                messages = new[]
                {
                    new { role = "system", content = "You are an expert code reviewer. Always respond with valid JSON only, no markdown, no extra text." },
                    new { role = "user",   content = prompt }
                },
                max_tokens  = 2000,
                temperature = 0.2
            };

        var client = _httpClientFactory.CreateClient(HttpClientName);

        // Serverless (Models-as-a-Service): {endpoint}/models/chat/completions
        // Managed / Classic Azure OpenAI:   {endpoint}/openai/deployments/{deployment}/chat/completions
        var trimmedEndpoint = endpoint.TrimEnd('/');
        var url = isServerless
            ? $"{trimmedEndpoint}/models/chat/completions?api-version={apiVersion}"
            : $"{trimmedEndpoint}/openai/deployments/{deployment}/chat/completions?api-version={apiVersion}";

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

        // Serverless uses Bearer token auth; managed deployments use api-key header
        if (isServerless)
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        else
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

    // CHANGED: Prompt now includes actual code snippets from representative files
    // so the AI can evaluate naming conventions, patterns, error handling, structure,
    // and overall code quality — not just aggregated metric counts.
    // Token budget: ~50-100K per student, so we can afford ~6K lines across 3 repos.
    private static string BuildBatchPrompt(
        IReadOnlyList<(AnalyzeResponse Summary, CodeMetrics Metrics, List<(string Path, string Content)> CodeSnippets)> repos)
    {
        // Cap total code lines sent to the AI to stay within token budget.
        // ~6,000 lines ≈ ~18,000 tokens for code + ~2,000 tokens for metadata/instructions.
        const int maxTotalCodeLines = 6000;
        const int maxLinesPerRepo   = 2500;

        var sb = new StringBuilder();
        sb.AppendLine("You are evaluating a college fresher's top GitHub repositories for a hiring assessment.");
        sb.AppendLine("Below are the metadata, code metrics, and ACTUAL CODE SNIPPETS for each repository.");
        sb.AppendLine("Do NOT recalculate or change completenessScore or finalScore — use them only as context.");
        sb.AppendLine();
        sb.AppendLine("When reviewing the code, evaluate:");
        sb.AppendLine("- Code structure and organization (classes, functions, separation of concerns)");
        sb.AppendLine("- Naming conventions (variables, methods, classes)");
        sb.AppendLine("- Error handling patterns (try/catch, input validation)");
        sb.AppendLine("- Code readability and comments");
        sb.AppendLine("- Use of language features and best practices");
        sb.AppendLine("- Testing presence and quality (if test files are included)");
        sb.AppendLine();

        var totalLinesUsed = 0;

        for (int i = 0; i < repos.Count; i++)
        {
            var (s, m, codeSnippets) = repos[i];
            sb.AppendLine($"===== Repository {i + 1}: {s.Repo} =====");
            sb.AppendLine($"- Languages       : {string.Join(", ", s.Languages)}");
            sb.AppendLine($"- Total Commits   : {s.TotalCommits}");
            sb.AppendLine($"- Contribution %  : {s.ContributionPercentage}");
            sb.AppendLine($"- Completeness    : {s.CompletenessScore}");
            sb.AppendLine($"- Final Score     : {s.FinalScore}");
            sb.AppendLine($"- Code Files      : {m.CodeFiles}  |  Total LOC : {m.TotalLinesOfCode}  |  Avg LOC/file : {m.AverageLinesPerFile}");
            sb.AppendLine($"- Functions       : {m.FunctionCount}  |  Classes : {m.ClassCount}  |  Loops : {m.LoopCount}");
            sb.AppendLine($"- Conditionals    : {m.ConditionalCount}  |  Error Handling : {m.ErrorHandlingCount}  |  Imports : {m.ImportCount}");
            sb.AppendLine($"- Comment Lines   : {m.CommentLines}  |  Folder Structure : {m.HasStructure}  |  README size : {m.ReadmeSize}");
            sb.AppendLine();

            // NEW: Append actual code snippets for AI review
            if (codeSnippets.Count > 0)
            {
                sb.AppendLine("--- Code Snippets ---");
                var repoLinesUsed = 0;

                foreach (var (path, content) in codeSnippets)
                {
                    if (totalLinesUsed >= maxTotalCodeLines || repoLinesUsed >= maxLinesPerRepo)
                        break;

                    var lines = content.Split('\n');
                    var linesToTake = Math.Min(lines.Length,
                        Math.Min(maxTotalCodeLines - totalLinesUsed, maxLinesPerRepo - repoLinesUsed));

                    sb.AppendLine($"\n// FILE: {path}");
                    sb.AppendLine(string.Join('\n', lines.Take(linesToTake)));

                    totalLinesUsed += linesToTake;
                    repoLinesUsed  += linesToTake;
                }

                if (repoLinesUsed >= maxLinesPerRepo)
                    sb.AppendLine("\n// ... (remaining files truncated to stay within token budget)");

                sb.AppendLine("--- End Code Snippets ---");
            }

            sb.AppendLine();
        }

        sb.AppendLine("Provide the following in a single JSON response:");
        sb.AppendLine("1. repos: an array with a codeQualityScore (0-100) for each repository, in the same order as above.");
        sb.AppendLine("   Base this score primarily on the ACTUAL CODE you reviewed, not just the metrics.");
        sb.AppendLine("2. aiSummary: a concise 4-6 line summary covering:");
        sb.AppendLine("   - Overall code quality and maturity level");
        sb.AppendLine("   - Specific strengths observed in the code (with examples)");
        sb.AppendLine("   - Specific areas to improve (with examples)");
        sb.AppendLine("   - Hiring recommendation (strong/moderate/weak candidate based on code quality)");
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
        string raw, IReadOnlyList<(AnalyzeResponse Summary, CodeMetrics Metrics, List<(string Path, string Content)> CodeSnippets)> repos)
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
