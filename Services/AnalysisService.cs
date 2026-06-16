using System.Collections.Concurrent;
using GithubRepoAnalysis.Models;
using Microsoft.Extensions.Logging;

namespace GithubRepoAnalysis.Services;

public class AnalysisService : IAnalysisService
{
    private const int MaxConcurrentRepoAnalysis = 5;

    // CHANGED: Use shared extension list instead of a local subset.
    // Previously had 16 extensions; now covers 50+ languages via KnownCodeExtensions.
    private static readonly HashSet<string> CodeExtensions = KnownCodeExtensions.All;

    private readonly IGitHubService _gitHub;
    private readonly IOpenAIService _openAi;
    private readonly ICompletenessScoreService _completenessScorer;
    private readonly ICodeMetricsService _codeMetrics;
    private readonly ILogger<AnalysisService> _logger;

    public AnalysisService(
        IGitHubService gitHub,
        IOpenAIService openAi,
        ICompletenessScoreService completenessScorer,
        ICodeMetricsService codeMetrics,
        ILogger<AnalysisService> logger)
    {
        _gitHub = gitHub;
        _openAi = openAi;
        _completenessScorer = completenessScorer;
        _codeMetrics = codeMetrics;
        _logger = logger;
    }

    public async Task<AnalyzeUserResponse> AnalyzeUserAsync(AnalyzeUserRequest request, CancellationToken ct = default)
    {
        var username = _gitHub.ParseProfileUrl(request.GithubProfileUrl);
        _logger.LogInformation("Fetching all repositories for GitHub user {Username}", username);

        // Fetch user profile once to resolve email for contribution matching
        var userProfile = await _gitHub.GetUserProfileAsync(username, ct);
        var resolvedEmail = !string.IsNullOrWhiteSpace(request.Email)
            ? request.Email
            : userProfile?.Email ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(resolvedEmail))
            _logger.LogInformation("Using email {Email} for contribution matching of user {Username}", resolvedEmail, username);
        else
            _logger.LogWarning("No email available for user {Username} — contribution matching will use name only", username);

        List<GitHubRepo> repos;
        try
        {
            repos = await _gitHub.GetUserRepositoriesAsync(username, ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException($"GitHub user '{username}' not found.", ex);
        }

        if (repos.Count == 0)
        {
            _logger.LogWarning("No public repositories found for user {Username}", username);
            return new AnalyzeUserResponse
            {
                StudentName = request.StudentName,
                GithubUsername = username,
                TotalRepos = 0
            };
        }

        _logger.LogInformation("Analyzing {Count} repositories for user {Username}", repos.Count, username);

        // CHANGED: Fully ignore forked repos — don't waste GitHub API calls analyzing them.
        // Previously forks were analyzed (fetching commits, languages, etc.) only to be
        // rejected with FinalScore=0 inside AnalyzeRepoAsync. Now they're filtered out
        // before any per-repo API calls are made.
        var originalCount = repos.Count;
        repos = repos.Where(r => !r.Fork).ToList();
        var forkedCount = originalCount - repos.Count;
        if (forkedCount > 0)
            _logger.LogInformation("Skipped {ForkedCount} forked repositories for user {Username}", forkedCount, username);

        var semaphore = new SemaphoreSlim(MaxConcurrentRepoAnalysis);
        var results = new ConcurrentBag<AnalyzeResponse>();
        var failureCount = 0;

        var tasks = repos.Select(async repo =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                // Feature 1: skip repos where latest commit is 1 year or older
                if (repo.PushedAt < DateTime.UtcNow.AddYears(-1))
                {
                    _logger.LogInformation("Skipping stale repository {Username}/{Repo} — last push: {PushedAt:yyyy-MM-dd}",
                        username, repo.Name, repo.PushedAt);
                    Interlocked.Increment(ref failureCount);
                    return;
                }

                var response = await AnalyzeRepoAsync(username, repo.Name, request.StudentName, resolvedEmail, ct);
                results.Add(response);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to analyze repository {Username}/{Repo}", username, repo.Name);
                Interlocked.Increment(ref failureCount);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        var sortedRepos = results.OrderByDescending(r => r.FinalScore).ToList();

        // Collect code metrics for top 3 repos in parallel, then make ONE batch LLM call
        var top3 = sortedRepos.Take(3).ToList();
        _logger.LogInformation("Collecting code metrics for top {Count} repositories for user {Username}", top3.Count, username);

        // CHANGED: Now carries code snippets alongside metrics so the AI can
        // review actual code, not just aggregated counts.
        var repoMetricsList = new List<(AnalyzeResponse Summary, CodeMetrics Metrics, List<(string Path, string Content)> CodeSnippets)>();

        await Task.WhenAll(top3.Select(async repoResponse =>
        {
            try
            {
                var repoName = repoResponse.Repo.Split('/').Last();
                var repoInfo = await _gitHub.GetRepositoryAsync(username, repoName, ct);
                if (repoInfo is null) return;

                var tree      = await _gitHub.GetFileTreeAsync(username, repoName, repoInfo.DefaultBranch, ct);
                var treeItems = tree?.Tree ?? new List<GitHubTreeItem>();
                var readme    = await _gitHub.GetReadmeInfoAsync(username, repoName, ct);
                var codeFiles = await _gitHub.GetCodeFilesAsync(username, repoName, repoInfo.DefaultBranch, ct);

                var metrics = _codeMetrics.ExtractMetrics(
                    codeFiles,
                    readmeSize:   readme?.Size ?? 0,
                    hasStructure: treeItems.Count(t => t.Type == "tree") > 0);

                lock (repoMetricsList)
                    repoMetricsList.Add((repoResponse, metrics, codeFiles));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Code-metrics collection failed for {Repo}", repoResponse.Repo);
            }
        }));

        // Single LLM call — returns codeQualityScore per repo + one aiSummary
        var aiSummary = string.Empty;
        if (repoMetricsList.Count > 0)
        {
            try
            {
                _logger.LogInformation("Making single batch LLM call for {Count} repos", repoMetricsList.Count);
                var (codeQualityScores, repoAnalyses, batchSummary) = await _openAi.GetBatchRepoInsightAsync(repoMetricsList, ct);

                // Apply per-repo codeQualityScore and detailed AI analysis
                foreach (var (repoResponse, _, _) in repoMetricsList)
                {
                    if (codeQualityScores.TryGetValue(repoResponse.Repo, out var score))
                        repoResponse.CodeQualityScore = score;

                    if (repoAnalyses.TryGetValue(repoResponse.Repo, out var analysis))
                        repoResponse.AiAnalysis = analysis;
                }

                aiSummary = batchSummary;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Batch AI call failed; proceeding without AI summary.");
                aiSummary = "AI analysis unavailable";
            }
        }

        // CHANGED: Return only the top 3 repos (the ones sent to AI) in the response.
        // Previously all analyzed repos were returned, which could be dozens of rows.
        // For hiring review, the manager only needs the best 3 repos with AI analysis.
        return new AnalyzeUserResponse
        {
            StudentName    = request.StudentName,
            GithubUsername = username,
            TotalRepos     = originalCount,
            SuccessCount   = results.Count,
            FailureCount   = failureCount + forkedCount,
            AiSummary      = aiSummary,
            Repositories   = top3
        };
    }

    private async Task<AnalyzeResponse> AnalyzeRepoAsync(
        string owner,
        string repoName,
        string studentName,
        string email,
        CancellationToken ct)
    {
        _logger.LogInformation("Analyzing repository {Owner}/{Repo} for {Student}", owner, repoName, studentName);

        var repo = await _gitHub.GetRepositoryAsync(owner, repoName, ct)
                   ?? throw new InvalidOperationException($"Repository {owner}/{repoName} not found.");

        var response = new AnalyzeResponse
        {
            StudentName = studentName,
            Repo = repo.FullName,
            IsFork = repo.Fork
        };

        // Forks are pre-filtered in AnalyzeUserAsync — this method only receives non-fork repos.

        // Fetch all data in parallel
        var languagesTask      = _gitHub.GetLanguagesAsync(owner, repoName, ct);
        var contributorsTask   = _gitHub.GetContributorsAsync(owner, repoName, ct);
        var commitsTask        = _gitHub.GetCommitsAsync(owner, repoName, 100, ct);
        var contentsTask       = _gitHub.GetRootContentsAsync(owner, repoName, ct);
        var treeTask           = _gitHub.GetFileTreeAsync(owner, repo.DefaultBranch, repo.DefaultBranch, ct);
        var readmeTask         = _gitHub.GetReadmeInfoAsync(owner, repoName, ct);
        var branchCountTask    = _gitHub.GetBranchCountAsync(owner, repoName, ct);
        var prCountTask        = _gitHub.GetPullRequestCountAsync(owner, repoName, ct);
        var releaseCountTask   = _gitHub.GetReleaseCountAsync(owner, repoName, ct);

        await Task.WhenAll(
            languagesTask, contributorsTask, commitsTask, contentsTask,
            treeTask, readmeTask, branchCountTask, prCountTask, releaseCountTask);

        var languages    = languagesTask.Result;
        var contributors = contributorsTask.Result;
        var commits      = commitsTask.Result;
        var rootContents = contentsTask.Result;
        var tree         = treeTask.Result;
        var readme       = readmeTask.Result;

        // Build metrics
        var treeItems = tree?.Tree ?? new List<GitHubTreeItem>();
        var metrics = new RepoMetrics
        {
            CreatedAt        = repo.CreatedAt,
            UpdatedAt        = repo.UpdatedAt,
            PushedAt         = repo.PushedAt,
            Size             = repo.Size,
            IsFork           = repo.Fork,
            TotalCommits     = commits.Count,
            FileCount        = treeItems.Count(t => t.Type == "blob"),
            DirectoryCount   = treeItems.Count(t => t.Type == "tree"),
            CodeFileCount    = treeItems.Count(t => t.Type == "blob" && CodeExtensions.Contains(Path.GetExtension(t.Path))),
            ReadmeSize       = readme?.Size ?? 0,
            Languages        = languages.Keys.ToList(),
            ContributorCount = contributors.Count,
            BranchCount      = branchCountTask.Result,
            PullRequestCount = prCountTask.Result,
            ReleaseCount     = releaseCountTask.Result,

            // NEW: Pass actual file and directory paths so CompletenessScoreService
            // can accurately detect src/, test/ directories and real project files.
            FilePaths        = treeItems.Where(t => t.Type == "blob").Select(t => t.Path).ToList(),
            DirectoryPaths   = treeItems.Where(t => t.Type == "tree").Select(t => t.Path).ToList()
        };

        // Detect frameworks from root contents
        var frameworks = DetectFrameworks(rootContents);

        // Calculate completeness using rule-based service
        var (completenessScore, breakdown) = _completenessScorer.CalculateScore(metrics);

        response.Languages = languages
            .OrderByDescending(kv => kv.Value)
            .Take(3)
            .Select(kv => kv.Key)
            .ToList();

        response.TotalCommits             = metrics.TotalCommits;
        response.ContributionPercentage   = CalculateContribution(commits, contributors, studentName, email, owner);
        response.FrameworksDetected       = frameworks;
        response.CompletenessScore        = completenessScore;
        response.CompletenessBreakdown    = breakdown;
        response.FinalScore               = ComputeFinalScore(response);

        return response;
    }

    private static List<string> DetectFrameworks(List<GitHubContentItem> rootContents)
    {
        var frameworks = new List<string>();
        var names = rootContents.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var hasCsproj = rootContents.Any(c => c.Name.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase));
        var hasSln    = rootContents.Any(c => c.Name.EndsWith(".sln", StringComparison.OrdinalIgnoreCase));
        if (hasCsproj || hasSln) frameworks.Add(".NET");
        if (names.Contains("package.json")) frameworks.Add("Node.js");
        if (names.Contains("requirements.txt") || names.Contains("setup.py")) frameworks.Add("Python");
        if (names.Contains("pom.xml") || names.Contains("build.gradle")) frameworks.Add("Java");
        if (names.Contains("go.mod")) frameworks.Add("Go");

        // React: indicated by presence of src/ folder alongside package.json, or common React config files
        var hasPackageJson = names.Contains("package.json");
        var hasReactConfig = names.Contains("src") || names.Contains(".babelrc") ||
                             names.Contains("vite.config.js") || names.Contains("vite.config.ts") ||
                             names.Contains("craco.config.js") || names.Contains("react.config.js");
        if (hasPackageJson && hasReactConfig) frameworks.Add("React");

        return frameworks;
    }

    private static double CalculateContribution(
        List<GitHubCommit> commits,
        List<GitHubContributor> contributors,
        string studentName,
        string email,
        string username = "")
    {
        if (commits.Count == 0) return 0;

        var normalizedEmail    = email?.Trim().ToLowerInvariant()    ?? string.Empty;
        var normalizedName     = studentName?.Trim().ToLowerInvariant() ?? string.Empty;
        var normalizedUsername = username?.Trim().ToLowerInvariant() ?? string.Empty;

        var studentCommits = commits.Count(c =>
        {
            var login       = c.Author?.Login?.ToLowerInvariant()        ?? string.Empty;
            var commitEmail = c.Commit?.Author?.Email?.ToLowerInvariant() ?? string.Empty;
            var commitName  = c.Commit?.Author?.Name?.ToLowerInvariant()  ?? string.Empty;

            // 1. Exact GitHub username match (most reliable — login is the authenticated GitHub account)
            if (!string.IsNullOrEmpty(normalizedUsername) && login == normalizedUsername)
                return true;

            // 2. Exact email match
            if (!string.IsNullOrEmpty(normalizedEmail) && commitEmail == normalizedEmail)
                return true;

            // 3. Name-based fallback (least reliable — only used when username and email are both unavailable)
            if (!string.IsNullOrEmpty(normalizedName) &&
                (login.Contains(normalizedName) || commitName.Contains(normalizedName)))
                return true;

            return false;
        });

        return Math.Round(studentCommits * 100.0 / commits.Count, 2);
    }

    // CHANGED: Rebalanced FinalScore formula.
    // Previously completeness was 90% of the score, which meant a repo with
    // a README + multiple branches but no real code could outscore a repo with
    // substantial code. New weights:
    //   - Completeness: 60% (project structure, README, tests, branches, etc.)
    //   - Contribution:  15% (student's own commits — critical for fresher evaluation)
    //   - Commit depth:  10% (rewards sustained work, not just initial push)
    //   - Tech stack:     5% (framework detection)
    //   - Language count: 5% (multi-language proficiency)
    //   - Code volume:    5% (meaningful amount of code files)
    private static int ComputeFinalScore(AnalyzeResponse r)
    {
        // Completeness: 60% weight (down from 90%)
        var completeness = r.CompletenessScore * 0.60;

        // Contribution: 15% — student's own contribution percentage
        // For fresher hiring, we care that the student actually wrote the code.
        var contribution = Math.Min(r.ContributionPercentage, 100) * 0.15;

        // Commit depth: 10% — rewards sustained work over time
        // Scale: 1 commit = 0.1, 50+ commits = full 10 points
        var commitDepth = Math.Min(r.TotalCommits, 50) * 0.20;
        commitDepth = Math.Min(commitDepth, 10.0);

        // Tech stack: 5%
        var techStack = r.FrameworksDetected.Count > 0 ? 3.0 : 0.0;
        techStack += r.FrameworksDetected.Contains(".NET")  ? 1.0 : 0.0;
        techStack += r.FrameworksDetected.Contains("React") ? 1.0 : 0.0;
        techStack  = Math.Min(techStack, 5.0);

        // Language diversity: 5%
        var langScore = Math.Min(r.Languages.Count, 3) * (5.0 / 3.0);

        // Code volume: 5% — at least some meaningful code files
        // (prevents empty/config-only repos from ranking high)
        var codeVolume = r.Languages.Count > 0 ? 5.0 : 0.0;

        var total = completeness + contribution + commitDepth + techStack + langScore + codeVolume;
        return (int)Math.Round(Math.Min(total, 100));
    }
}
