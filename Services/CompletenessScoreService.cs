using GithubRepoAnalysis.Models;

namespace GithubRepoAnalysis.Services;

public class CompletenessScoreService : ICompletenessScoreService
{
    // Rule weights — total = 100
    private const int ReadmeWeight           = 20;
    private const int ProjectFileWeight      = 15;
    private const int SrcDirectoryWeight     = 10;
    private const int TestDirectoryWeight    = 10;
    private const int MultiBranchWeight      = 10;
    private const int MultiContributorWeight = 10;
    private const int ReleasesWeight         = 5;
    private const int PullRequestsWeight     = 5;
    private const int RecentActivityWeight   = 5;
    private const int MultiLanguageWeight    = 5;

    public (int Score, CompletenessBreakdown Breakdown) CalculateScore(RepoMetrics metrics)
    {
        var breakdown = new CompletenessBreakdown
        {
            HasReadme               = metrics.ReadmeSize > 0,
            HasProjectFile          = metrics.FileCount > 0 && metrics.CodeFileCount > 0,
            HasSrcDirectory         = metrics.DirectoryCount > 0,
            HasTestDirectory        = metrics.FileCount > 0,
            HasMultipleBranches     = metrics.BranchCount > 1,
            HasMultipleContributors = metrics.ContributorCount > 1,
            HasReleases             = metrics.ReleaseCount > 0,
            HasPullRequests         = metrics.PullRequestCount > 0,
            HasRecentActivity       = metrics.PushedAt >= DateTime.UtcNow.AddMonths(-6),
            HasMultipleLanguages    = metrics.Languages.Count > 1
        };

        var score = 0;
        if (breakdown.HasReadme)               score += ReadmeWeight;
        if (breakdown.HasProjectFile)          score += ProjectFileWeight;
        if (breakdown.HasSrcDirectory)         score += SrcDirectoryWeight;
        if (breakdown.HasTestDirectory)        score += TestDirectoryWeight;
        if (breakdown.HasMultipleBranches)     score += MultiBranchWeight;
        if (breakdown.HasMultipleContributors) score += MultiContributorWeight;
        if (breakdown.HasReleases)             score += ReleasesWeight;
        if (breakdown.HasPullRequests)         score += PullRequestsWeight;
        if (breakdown.HasRecentActivity)       score += RecentActivityWeight;
        if (breakdown.HasMultipleLanguages)    score += MultiLanguageWeight;

        return (Math.Max(0, Math.Min(score, 100)), breakdown);
    }
}
