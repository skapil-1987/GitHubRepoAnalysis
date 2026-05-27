using GithubRepoAnalysis.Models;

namespace GithubRepoAnalysis.Services;

public class CompletenessScoreService : ICompletenessScoreService
{
    // Rule weights — total = 100
    // CHANGED: Rebalanced weights — reduced ReadmeWeight, added CommitDepthWeight (5)
    // to reward repos with meaningful commit history.
    private const int ReadmeWeight           = 15;
    private const int ProjectFileWeight      = 15;
    private const int SrcDirectoryWeight     = 10;
    private const int TestDirectoryWeight    = 10;
    private const int MultiBranchWeight      = 10;
    private const int MultiContributorWeight = 10;
    private const int ReleasesWeight         = 5;
    private const int PullRequestsWeight     = 5;
    private const int RecentActivityWeight   = 5;
    private const int MultiLanguageWeight    = 5;
    private const int CommitDepthWeight      = 5;  // NEW: rewards repos with >=10 commits
    private const int CodeFileRatioWeight    = 5;   // NEW: rewards repos where code files > 50% of total files

    // CHANGED: Known project file names — used to accurately detect whether a repo
    // has a real build/project configuration instead of just checking FileCount > 0.
    private static readonly HashSet<string> ProjectFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "package.json", "pom.xml", "build.gradle", "go.mod", "cargo.toml",
        "requirements.txt", "setup.py", "pyproject.toml", "gemfile",
        "makefile", "cmakelists.txt", "docker-compose.yml", "dockerfile"
    };

    private static readonly HashSet<string> ProjectFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".csproj", ".sln", ".fsproj", ".vbproj"
    };

    // CHANGED: Known test directory name segments — used to detect actual test folders
    // instead of the previous check which was always true (FileCount > 0).
    private static readonly string[] TestDirectorySegments =
    {
        "test", "tests", "__tests__", "spec", "specs", "xunit", "nunit", "junit"
    };

    // CHANGED: Known source directory name segments — used to detect actual src folders
    // instead of just checking DirectoryCount > 0.
    private static readonly string[] SrcDirectorySegments =
    {
        "src", "source", "lib", "app", "core"
    };

    public (int Score, CompletenessBreakdown Breakdown) CalculateScore(RepoMetrics metrics)
    {
        var dirs  = metrics.DirectoryPaths;
        var files = metrics.FilePaths;

        var breakdown = new CompletenessBreakdown
        {
            HasReadme = metrics.ReadmeSize > 0,

            // CHANGED: Check for actual project/build files (e.g. .csproj, package.json)
            // instead of generic FileCount > 0 which was always true.
            HasProjectFile = files.Any(f =>
                ProjectFileNames.Contains(Path.GetFileName(f)) ||
                ProjectFileExtensions.Contains(Path.GetExtension(f))),

            // CHANGED: Check for real src/lib/app directories
            // instead of DirectoryCount > 0 which was always true.
            HasSrcDirectory = dirs.Any(d =>
                SrcDirectorySegments.Any(seg =>
                    d.Split('/').Any(part => part.Equals(seg, StringComparison.OrdinalIgnoreCase)))),

            // CHANGED: Check for real test/spec directories
            // instead of FileCount > 0 which was always true and gave 10 free points.
            HasTestDirectory = dirs.Any(d =>
                TestDirectorySegments.Any(seg =>
                    d.Split('/').Any(part => part.Equals(seg, StringComparison.OrdinalIgnoreCase)))),

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

        // NEW: Reward repos with meaningful commit history (>=10 commits)
        if (metrics.TotalCommits >= 10)        score += CommitDepthWeight;

        // NEW: Reward repos where code files make up >50% of total files
        if (metrics.FileCount > 0 && metrics.CodeFileCount * 100 / metrics.FileCount > 50)
            score += CodeFileRatioWeight;

        return (Math.Max(0, Math.Min(score, 100)), breakdown);
    }
}
