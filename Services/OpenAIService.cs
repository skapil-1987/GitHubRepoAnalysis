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
    public async Task<(IReadOnlyDictionary<string, int?> CodeQualityScores, IReadOnlyDictionary<string, RepoAiAnalysis> RepoAnalyses, string AiSummary)> GetBatchRepoInsightAsync(
        IReadOnlyList<(AnalyzeResponse Summary, CodeMetrics Metrics, List<(string Path, string Content)> CodeSnippets)> repos,
        CancellationToken ct = default)
    {
        var empty = (
            CodeQualityScores: (IReadOnlyDictionary<string, int?>)new Dictionary<string, int?>(),
            RepoAnalyses: (IReadOnlyDictionary<string, RepoAiAnalysis>)new Dictionary<string, RepoAiAnalysis>(),
            AiSummary: "AI analysis unavailable");

        var endpoint   = _configuration["AzureOpenAI:Endpoint"];
        var apiKey     = _configuration["AzureOpenAI:ApiKey"];

        if (string.IsNullOrWhiteSpace(endpoint) ||
            string.IsNullOrWhiteSpace(apiKey))
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
            max_tokens  = 8000,
            temperature = 0.2
        };

        var client = _httpClientFactory.CreateClient(HttpClientName);

        // Endpoint is the complete URL including deployment and api-version,
        // e.g. https://xxx.cognitiveservices.azure.com/openai/deployments/gpt-4.1-mini/chat/completions?api-version=2025-01-01-preview
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
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

            var parsed = ParseBatchResponse(raw, repos);
            return (parsed.CodeQualityScores, parsed.RepoAnalyses, parsed.AiSummary);
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
        sb.AppendLine();
        sb.AppendLine("1. repos: an array with DETAILED analysis for each repository (same order as above). Each entry must include:");
        sb.AppendLine("   - codeQualityScore (0-100): based primarily on the ACTUAL CODE you reviewed");
        sb.AppendLine("   - strengths: array of 2-4 specific strengths with concrete examples from the code (e.g., file names, patterns observed)");
        sb.AppendLine("   - areasToImprove: array of 2-4 specific improvements with concrete examples");
        sb.AppendLine("   - technicalStack: one-line summary of technologies/frameworks/patterns used in this repo");
        sb.AppendLine("   - codePatterns: one-line description of design patterns or architectural approach observed (e.g., MVC, layered, monolithic)");
        sb.AppendLine("   - repoVerdict: one-line hiring-relevant verdict for this repo (e.g., 'Shows solid OOP fundamentals with room for error handling improvement')");
        sb.AppendLine();
        sb.AppendLine("2. aiSummary: a technical summary (8-12 lines) for a TECHNICAL MANAGER reviewing this candidate. Include:");
        sb.AppendLine("   - Overall skill assessment with proficiency level (beginner/intermediate/advanced)");
        sb.AppendLine("   - Technical strengths across all repos (languages, patterns, architecture)");
        sb.AppendLine("   - Technical gaps or concerns (missing testing, poor error handling, no separation of concerns, etc.)");
        sb.AppendLine("   - Code maturity indicators (comments, project structure, dependency management, git hygiene)");
        sb.AppendLine("   - Hiring recommendation: Strong Hire / Hire / Lean Hire / No Hire — with justification");
        sb.AppendLine("   - Suggested interview focus areas based on observed gaps");
        sb.AppendLine();
        sb.AppendLine("Return ONLY valid JSON in this exact format:");
        sb.AppendLine("{");
        sb.AppendLine("  \"repos\": [");
        sb.AppendLine("    {");
        sb.AppendLine("      \"repo\": \"<full repo name>\",");
        sb.AppendLine("      \"codeQualityScore\": <number>,");
        sb.AppendLine("      \"strengths\": [\"...\", \"...\"],");
        sb.AppendLine("      \"areasToImprove\": [\"...\", \"...\"],");
        sb.AppendLine("      \"technicalStack\": \"<string>\",");
        sb.AppendLine("      \"codePatterns\": \"<string>\",");
        sb.AppendLine("      \"repoVerdict\": \"<string>\"");
        sb.AppendLine("    }");
        sb.AppendLine("  ],");
        sb.AppendLine("  \"aiSummary\": \"<string>\"");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static (IReadOnlyDictionary<string, int?> CodeQualityScores, IReadOnlyDictionary<string, RepoAiAnalysis> RepoAnalyses, string AiSummary) ParseBatchResponse(
        string raw, IReadOnlyList<(AnalyzeResponse Summary, CodeMetrics Metrics, List<(string Path, string Content)> CodeSnippets)> repos)
    {
        var scores   = new Dictionary<string, int?>();
        var analyses = new Dictionary<string, RepoAiAnalysis>();
        var summary  = "AI analysis unavailable";

        try
        {
            var json = raw.Trim().TrimStart('`');
            if (json.StartsWith("json", StringComparison.OrdinalIgnoreCase))
                json = json[4..].Trim();
            json = json.TrimEnd('`').Trim();

            using var doc  = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("repos", out var reposEl) && reposEl.ValueKind == JsonValueKind.Array)
            {
                var repoArray = reposEl.EnumerateArray().ToList();
                for (int i = 0; i < repoArray.Count && i < repos.Count; i++)
                {
                    var el = repoArray[i];
                    var repoName = repos[i].Summary.Repo;

                    // Code quality score
                    int? score = el.TryGetProperty("codeQualityScore", out var scoreEl)
                                 && scoreEl.TryGetInt32(out var s)
                        ? Math.Clamp(s, 0, 100)
                        : null;
                    scores[repoName] = score;

                    // Per-repo detailed analysis
                    var analysis = new RepoAiAnalysis
                    {
                        Strengths = ParseStringArray(el, "strengths"),
                        AreasToImprove = ParseStringArray(el, "areasToImprove"),
                        TechnicalStack = el.TryGetProperty("technicalStack", out var ts) ? ts.GetString() ?? "" : "",
                        CodePatterns = el.TryGetProperty("codePatterns", out var cp) ? cp.GetString() ?? "" : "",
                        RepoVerdict = el.TryGetProperty("repoVerdict", out var rv) ? rv.GetString() ?? "" : ""
                    };
                    analyses[repoName] = analysis;
                }
            }

            if (root.TryGetProperty("aiSummary", out var summaryEl))
                summary = summaryEl.GetString() ?? summary;
        }
        catch
        {
            // Return whatever was parsed so far
        }

        return (scores, analyses, summary);
    }

    private static List<string> ParseStringArray(JsonElement el, string propertyName)
    {
        if (!el.TryGetProperty(propertyName, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return new List<string>();

        return arr.EnumerateArray()
            .Where(a => a.ValueKind == JsonValueKind.String)
            .Select(a => a.GetString() ?? "")
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();
    }
}
