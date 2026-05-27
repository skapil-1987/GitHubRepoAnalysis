using GithubRepoAnalysis.Models;

namespace GithubRepoAnalysis.Services;

public interface IOpenAIService
{
    /// <summary>
    /// Single batched LLM call for up to 3 repos.
    /// Returns a codeQualityScore per repo (keyed by repo full name) AND one summarised aiSummary.
    /// Used by the multi-repo endpoint — 1 LLM call total.
    /// </summary>
    Task<(IReadOnlyDictionary<string, int?> CodeQualityScores, string AiSummary)> GetBatchRepoInsightAsync(
        IReadOnlyList<(AnalyzeResponse Summary, CodeMetrics Metrics)> repos, CancellationToken ct = default);
}
