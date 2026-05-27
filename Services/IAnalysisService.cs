using GithubRepoAnalysis.Models;

namespace GithubRepoAnalysis.Services;

public interface IAnalysisService
{
    Task<AnalyzeUserResponse> AnalyzeUserAsync(AnalyzeUserRequest request, CancellationToken ct = default);
}
