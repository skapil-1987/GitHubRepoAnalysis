using GithubRepoAnalysis.Models;

namespace GithubRepoAnalysis.Services;

public interface IOpenAIService
{
    Task<string> GetInsightsAsync(AnalyzeResponse summary, CancellationToken ct = default);
}
