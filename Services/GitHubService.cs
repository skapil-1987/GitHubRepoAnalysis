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

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GitHubService> _logger;

    public GitHubService(IHttpClientFactory httpClientFactory, ILogger<GitHubService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
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
}
