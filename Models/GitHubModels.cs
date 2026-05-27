using System.Text.Json.Serialization;

namespace GithubRepoAnalysis.Models;

public class GitHubRepo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("full_name")]
    public string FullName { get; set; } = string.Empty;

    [JsonPropertyName("fork")]
    public bool Fork { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("default_branch")]
    public string DefaultBranch { get; set; } = "main";

    [JsonPropertyName("stargazers_count")]
    public int Stars { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; }

    [JsonPropertyName("pushed_at")]
    public DateTime PushedAt { get; set; }

    [JsonPropertyName("size")]
    public int Size { get; set; }
}

public class GitHubContributor
{
    [JsonPropertyName("login")]
    public string Login { get; set; } = string.Empty;

    [JsonPropertyName("contributions")]
    public int Contributions { get; set; }
}

public class GitHubCommit
{
    [JsonPropertyName("sha")]
    public string Sha { get; set; } = string.Empty;

    [JsonPropertyName("commit")]
    public CommitDetail Commit { get; set; } = new();

    [JsonPropertyName("author")]
    public CommitAuthorUser? Author { get; set; }
}

public class CommitDetail
{
    [JsonPropertyName("author")]
    public CommitAuthor Author { get; set; } = new();

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}

public class CommitAuthor
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;
}

public class CommitAuthorUser
{
    [JsonPropertyName("login")]
    public string Login { get; set; } = string.Empty;
}

public class GitHubContentItem
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
}

public class GitHubTreeResponse
{
    [JsonPropertyName("tree")]
    public List<GitHubTreeItem> Tree { get; set; } = new();

    [JsonPropertyName("truncated")]
    public bool Truncated { get; set; }
}

public class GitHubTreeItem
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public int? Size { get; set; }
}

public class GitHubReadme
{
    [JsonPropertyName("size")]
    public int Size { get; set; }
}

public class GitHubBranch
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public class GitHubUser
{
    [JsonPropertyName("login")]
    public string Login { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }
}

public class GitHubPullRequest
{
    [JsonPropertyName("number")]
    public int Number { get; set; }
}

public class GitHubRelease
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
}
