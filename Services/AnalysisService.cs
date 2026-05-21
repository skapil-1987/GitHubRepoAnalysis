using GithubRepoAnalysis.Models;
using Microsoft.Extensions.Logging;

namespace GithubRepoAnalysis.Services;

public class AnalysisService : IAnalysisService
{
    private readonly IGitHubService _gitHub;
    private readonly IOpenAIService _openAi;
    private readonly ILogger<AnalysisService> _logger;

    public AnalysisService(IGitHubService gitHub, IOpenAIService openAi, ILogger<AnalysisService> logger)
    {
        _gitHub = gitHub;
        _openAi = openAi;
        _logger = logger;
    }

    public async Task<AnalyzeResponse> AnalyzeAsync(AnalyzeRequest request, CancellationToken ct = default)
    {
        var (owner, repoName) = _gitHub.ParseRepoUrl(request.GithubRepoUrl);
        _logger.LogInformation("Analyzing repository {Owner}/{Repo} for {Student}", owner, repoName, request.StudentName);

        var repo = await _gitHub.GetRepositoryAsync(owner, repoName, ct)
                   ?? throw new InvalidOperationException($"Repository {owner}/{repoName} not found.");

        var response = new AnalyzeResponse
        {
            StudentName = request.StudentName,
            Repo = repo.FullName,
            IsFork = repo.Fork
        };

        if (repo.Fork)
        {
            response.Message = "Repository is a fork. Rejected: original work is mandatory.";
            response.FinalScore = 0;
            return response;
        }

        var languagesTask = _gitHub.GetLanguagesAsync(owner, repoName, ct);
        var contributorsTask = _gitHub.GetContributorsAsync(owner, repoName, ct);
        var commitsTask = _gitHub.GetCommitsAsync(owner, repoName, 100, ct);
        var contentsTask = _gitHub.GetRootContentsAsync(owner, repoName, ct);

        await Task.WhenAll(languagesTask, contributorsTask, commitsTask, contentsTask);

        var languages = languagesTask.Result;
        var contributors = contributorsTask.Result;
        var commits = commitsTask.Result;
        var rootContents = contentsTask.Result;

        response.Languages = languages
            .OrderByDescending(kv => kv.Value)
            .Take(3)
            .Select(kv => kv.Key)
            .ToList();

        response.TotalCommits = commits.Count;
        response.ContributionPercentage = CalculateContribution(commits, contributors, request);

        var (frameworks, completeness) = await DetectFrameworksAndCompletenessAsync(owner, repoName, rootContents, ct);
        response.FrameworksDetected = frameworks;
        response.CompletenessScore = completeness;

        response.FinalScore = ComputeFinalScore(response);

        try
        {
           // response.AiInsights = await _openAi.GetInsightsAsync(response, ct);

        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI insights unavailable.");
            response.AiInsights = string.Empty;
        }

        return response;
    }

    private static double CalculateContribution(
        List<GitHubCommit> commits,
        List<GitHubContributor> contributors,
        AnalyzeRequest request)
    {
        if (commits.Count == 0) return 0;

        var email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        var name = request.StudentName?.Trim().ToLowerInvariant() ?? string.Empty;

        var studentCommits = commits.Count(c =>
        {
            var login = c.Author?.Login?.ToLowerInvariant() ?? string.Empty;
            var commitEmail = c.Commit?.Author?.Email?.ToLowerInvariant() ?? string.Empty;
            var commitName = c.Commit?.Author?.Name?.ToLowerInvariant() ?? string.Empty;

            return (!string.IsNullOrEmpty(email) && commitEmail == email)
                || (!string.IsNullOrEmpty(name) && (login.Contains(name) || commitName.Contains(name)));
        });

        return Math.Round(studentCommits * 100.0 / commits.Count, 2);
    }

    private async Task<(List<string> Frameworks, int Completeness)> DetectFrameworksAndCompletenessAsync(
        string owner,
        string repoName,
        List<GitHubContentItem> rootContents,
        CancellationToken ct)
    {
        var frameworks = new List<string>();
        var names = rootContents.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var dirs = rootContents.Where(c => c.Type == "dir").Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // .NET detection
        var hasCsproj = rootContents.Any(c => c.Name.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase));
        var hasSln = rootContents.Any(c => c.Name.EndsWith(".sln", StringComparison.OrdinalIgnoreCase));
        if (hasCsproj || hasSln)
            frameworks.Add(".NET");

        // React detection via package.json
        if (names.Contains("package.json"))
        {
            var pkg = await _gitHub.GetFileContentAsync(owner, repoName, "package.json", ct);
            if (!string.IsNullOrEmpty(pkg) &&
                pkg.Contains("\"react\"", StringComparison.OrdinalIgnoreCase))
            {
                frameworks.Add("React");
            }
        }

        // Completeness
        var score = 0;
        if (names.Any(n => n.Equals("README.md", StringComparison.OrdinalIgnoreCase))) score += 40;
        if (names.Contains("package.json") || hasCsproj || hasSln) score += 30;
        if (dirs.Contains("src")) score += 20;
        if (dirs.Contains("tests") || dirs.Contains("test")) score += 10;

        return (frameworks, Math.Min(score, 100));
    }

    private static int ComputeFinalScore(AnalyzeResponse r)
    {
        // Contribution weight: 30%
        var contribution = Math.Min(r.ContributionPercentage, 100) * 0.30;

        // Tech stack bonus: 20% (full if any framework detected)
        var techStack = r.FrameworksDetected.Count > 0 ? 20.0 : 0.0;

        // Completeness: 20%
        var completeness = r.CompletenessScore * 0.20;

        // Activity (commits): 30% — scale 100 commits to full points
        var activity = Math.Min(r.TotalCommits, 100) * 0.30;

        var total = contribution + techStack + completeness + activity;
        return (int)Math.Round(Math.Min(total, 100));
    }
}
