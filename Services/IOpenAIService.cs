using GithubRepoAnalysis.Models;

namespace GithubRepoAnalysis.Services;

public interface IOpenAIService
{
    /// <summary>
    /// Single batched LLM call for up to 3 repos.
    /// Returns a codeQualityScore per repo (keyed by repo full name) AND one summarised aiSummary.
    /// Used by the multi-repo endpoint — 1 LLM call total.
    /// </summary>
    /// <summary>
    /// CHANGED: Now accepts code snippets per repo so the AI can review actual code
    /// alongside aggregated metrics for a more accurate quality assessment.
    /// </summary>
    Task<(IReadOnlyDictionary<string, int?> CodeQualityScores, IReadOnlyDictionary<string, RepoAiAnalysis> RepoAnalyses, string AiSummary)> GetBatchRepoInsightAsync(
        IReadOnlyList<(AnalyzeResponse Summary, CodeMetrics Metrics, List<(string Path, string Content)> CodeSnippets)> repos,
        CancellationToken ct = default);
}
