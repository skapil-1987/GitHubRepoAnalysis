using GithubRepoAnalysis.Models;

namespace GithubRepoAnalysis.Services;

public interface ICompletenessScoreService
{
    (int Score, CompletenessBreakdown Breakdown) CalculateScore(RepoMetrics metrics);
}
