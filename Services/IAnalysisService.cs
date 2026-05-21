using GithubRepoAnalysis.Models;

namespace GithubRepoAnalysis.Services;

public interface IAnalysisService
{
    Task<AnalyzeResponse> AnalyzeAsync(AnalyzeRequest request, CancellationToken ct = default);
}
