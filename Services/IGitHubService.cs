using GithubRepoAnalysis.Models;

namespace GithubRepoAnalysis.Services;

public interface IGitHubService
{
    (string Owner, string Repo) ParseRepoUrl(string url);
    string ParseProfileUrl(string profileUrl);
    Task<GitHubUser?> GetUserProfileAsync(string username, CancellationToken ct = default);
    Task<GitHubRepo?> GetRepositoryAsync(string owner, string repo, CancellationToken ct = default);
    Task<Dictionary<string, long>> GetLanguagesAsync(string owner, string repo, CancellationToken ct = default);
    Task<List<GitHubContributor>> GetContributorsAsync(string owner, string repo, CancellationToken ct = default);
    Task<List<GitHubCommit>> GetCommitsAsync(string owner, string repo, int max = 100, CancellationToken ct = default);
    Task<List<GitHubContentItem>> GetRootContentsAsync(string owner, string repo, CancellationToken ct = default);
    Task<string?> GetFileContentAsync(string owner, string repo, string path, CancellationToken ct = default);
    Task<List<GitHubRepo>> GetUserRepositoriesAsync(string username, CancellationToken ct = default);
    Task<GitHubTreeResponse?> GetFileTreeAsync(string owner, string repo, string branch, CancellationToken ct = default);
    Task<GitHubReadme?> GetReadmeInfoAsync(string owner, string repo, CancellationToken ct = default);
    Task<int> GetBranchCountAsync(string owner, string repo, CancellationToken ct = default);
    Task<int> GetPullRequestCountAsync(string owner, string repo, CancellationToken ct = default);
    Task<int> GetReleaseCountAsync(string owner, string repo, CancellationToken ct = default);
    Task<List<(string Path, string Content)>> GetCodeFilesAsync(string owner, string repo, string branch, CancellationToken ct = default);
}
