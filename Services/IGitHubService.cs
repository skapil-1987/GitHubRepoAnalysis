using GithubRepoAnalysis.Models;

namespace GithubRepoAnalysis.Services;

public interface IGitHubService
{
    (string Owner, string Repo) ParseRepoUrl(string url);
    Task<GitHubRepo?> GetRepositoryAsync(string owner, string repo, CancellationToken ct = default);
    Task<Dictionary<string, long>> GetLanguagesAsync(string owner, string repo, CancellationToken ct = default);
    Task<List<GitHubContributor>> GetContributorsAsync(string owner, string repo, CancellationToken ct = default);
    Task<List<GitHubCommit>> GetCommitsAsync(string owner, string repo, int max = 100, CancellationToken ct = default);
    Task<List<GitHubContentItem>> GetRootContentsAsync(string owner, string repo, CancellationToken ct = default);
    Task<string?> GetFileContentAsync(string owner, string repo, string path, CancellationToken ct = default);
}
