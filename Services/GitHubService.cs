using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using GithubRepoAnalysis.Models;
using Microsoft.Extensions.Logging;

namespace GithubRepoAnalysis.Services;

public class GitHubService : IGitHubService
{
    public const string HttpClientName = "GitHub";

    private static readonly Regex RepoUrlRegex = new(
        @"github\.com[:/](?<owner>[^/]+)/(?<repo>[^/\s\.]+)(\.git)?/?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ProfileUrlRegex = new(
        @"github\.com/(?<username>[^/\s]+)/?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GitHubService> _logger;

    public GitHubService(IHttpClientFactory httpClientFactory, ILogger<GitHubService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public string ParseProfileUrl(string profileUrl)
    {
        if (string.IsNullOrWhiteSpace(profileUrl))
            throw new ArgumentException("GitHub profile URL is required.", nameof(profileUrl));

        var match = ProfileUrlRegex.Match(profileUrl);
        if (!match.Success)
            throw new ArgumentException($"Invalid GitHub profile URL: {profileUrl}", nameof(profileUrl));

        return match.Groups["username"].Value;
    }

    public (string Owner, string Repo) ParseRepoUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("GitHub repository URL is required.", nameof(url));

        var match = RepoUrlRegex.Match(url);
        if (!match.Success)
            throw new ArgumentException($"Invalid GitHub repository URL: {url}", nameof(url));

        return (match.Groups["owner"].Value, match.Groups["repo"].Value);
    }

    public async Task<GitHubRepo?> GetRepositoryAsync(string owner, string repo, CancellationToken ct = default)
        => await GetJsonAsync<GitHubRepo>($"/repos/{owner}/{repo}", ct);

    public async Task<GitHubUser?> GetUserProfileAsync(string username, CancellationToken ct = default)
        => await GetJsonAsync<GitHubUser>($"/users/{username}", ct);

    public async Task<Dictionary<string, long>> GetLanguagesAsync(string owner, string repo, CancellationToken ct = default)
        => await GetJsonAsync<Dictionary<string, long>>($"/repos/{owner}/{repo}/languages", ct) ?? new();

    public async Task<List<GitHubContributor>> GetContributorsAsync(string owner, string repo, CancellationToken ct = default)
        => await GetJsonAsync<List<GitHubContributor>>($"/repos/{owner}/{repo}/contributors?per_page=100", ct) ?? new();

    public async Task<List<GitHubCommit>> GetCommitsAsync(string owner, string repo, int max = 100, CancellationToken ct = default)
    {
        var commits = new List<GitHubCommit>();
        var perPage = Math.Min(100, max);
        var page = 1;

        while (commits.Count < max)
        {
            var pageCommits = await GetJsonAsync<List<GitHubCommit>>(
                $"/repos/{owner}/{repo}/commits?per_page={perPage}&page={page}", ct);

            if (pageCommits == null || pageCommits.Count == 0)
                break;

            commits.AddRange(pageCommits);

            if (pageCommits.Count < perPage)
                break;

            page++;
        }

        return commits.Take(max).ToList();
    }

    public async Task<List<GitHubContentItem>> GetRootContentsAsync(string owner, string repo, CancellationToken ct = default)
        => await GetJsonAsync<List<GitHubContentItem>>($"/repos/{owner}/{repo}/contents", ct) ?? new();

    public async Task<GitHubTreeResponse?> GetFileTreeAsync(string owner, string repo, string branch, CancellationToken ct = default)
        => await GetJsonAsync<GitHubTreeResponse>($"/repos/{owner}/{repo}/git/trees/{branch}?recursive=1", ct);

    public async Task<GitHubReadme?> GetReadmeInfoAsync(string owner, string repo, CancellationToken ct = default)
        => await GetJsonAsync<GitHubReadme>($"/repos/{owner}/{repo}/readme", ct);

    public async Task<int> GetBranchCountAsync(string owner, string repo, CancellationToken ct = default)
        => await GetAllPagesCountAsync<GitHubBranch>($"/repos/{owner}/{repo}/branches", ct);

    public async Task<int> GetPullRequestCountAsync(string owner, string repo, CancellationToken ct = default)
        => await GetAllPagesCountAsync<GitHubPullRequest>($"/repos/{owner}/{repo}/pulls?state=all", ct);

    public async Task<int> GetReleaseCountAsync(string owner, string repo, CancellationToken ct = default)
        => await GetAllPagesCountAsync<GitHubRelease>($"/repos/{owner}/{repo}/releases", ct);

    public async Task<List<GitHubRepo>> GetUserRepositoriesAsync(string username, CancellationToken ct = default)
    {
        var allRepos = new List<GitHubRepo>();
        var page = 1;

        while (true)
        {
            var pageRepos = await GetJsonAsync<List<GitHubRepo>>(
                $"/users/{username}/repos?per_page=100&page={page}&type=public", ct);

            if (pageRepos is null || pageRepos.Count == 0)
                break;

            allRepos.AddRange(pageRepos);

            if (pageRepos.Count < 100)
                break;

            page++;
        }

        _logger.LogInformation("Fetched {Count} public repositories for user {Username}", allRepos.Count, username);
        return allRepos;
    }

    public async Task<string?> GetFileContentAsync(string owner, string repo, string path, CancellationToken ct = default)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"/repos/{owner}/{repo}/contents/{path}");
            request.Headers.Accept.Clear();
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.raw"));

            using var response = await client.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch file {Path} from {Owner}/{Repo}", path, owner, repo);
            return null;
        }
    }

    private async Task<int> GetAllPagesCountAsync<T>(string basePath, CancellationToken ct)
    {
        var count = 0;
        var page = 1;
        var separator = basePath.Contains('?') ? "&" : "?";

        while (true)
        {
            var items = await GetJsonAsync<List<T>>($"{basePath}{separator}per_page=100&page={page}", ct);
            if (items is null || items.Count == 0) break;
            count += items.Count;
            if (items.Count < 100) break;
            page++;
        }

        return count;
    }

    private async Task<T?> GetJsonAsync<T>(string path, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        try
        {
            using var response = await client.GetAsync(path, ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return default;

            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            return await JsonSerializer.DeserializeAsync<T>(stream, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GitHub API call failed for {Path}", path);
            throw;
        }
    }

    private static readonly HashSet<string> CodeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".py", ".js", ".ts", ".java", ".cpp", ".html", ".css"
    };

    private static readonly string[] ExcludedPrefixes =
    {
        "node_modules/", "bin/", "obj/", ".git/"
    };

    public async Task<List<(string Path, string Content)>> GetCodeFilesAsync(
        string owner, string repo, string branch, CancellationToken ct = default)
    {
        const int maxFiles        = 10;
        const int maxLinesPerFile = 250;

        var result = new List<(string, string)>();

        try
        {
            var tree = await GetFileTreeAsync(owner, repo, branch, ct);
            if (tree is null) return result;

            var candidates = tree.Tree
                .Where(item =>
                    item.Type == "blob" &&
                    CodeExtensions.Contains(Path.GetExtension(item.Path)) &&
                    !ExcludedPrefixes.Any(p => item.Path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                .Take(maxFiles)
                .ToList();

            foreach (var item in candidates)
            {
                try
                {
                    var rawUrl = $"https://raw.githubusercontent.com/{owner}/{repo}/{branch}/{item.Path}";
                    var client = _httpClientFactory.CreateClient(HttpClientName);
                    var content = await client.GetStringAsync(rawUrl, ct);

                    // Truncate to max lines
                    var lines = content.Split('\n');
                    if (lines.Length > maxLinesPerFile)
                        content = string.Join('\n', lines.Take(maxLinesPerFile));

                    result.Add((item.Path, content));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not fetch content for {Path}", item.Path);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetCodeFilesAsync failed for {Owner}/{Repo}", owner, repo);
        }

        return result;
    }
}
