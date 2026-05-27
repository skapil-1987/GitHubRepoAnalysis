using System.Text.Json.Serialization;

namespace GithubRepoAnalysis.Models;

public class AnalyzeUserRequest
{
    [JsonPropertyName("studentName")]
    public string StudentName { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("githubProfileUrl")]
    public string GithubProfileUrl { get; set; } = string.Empty;
}

public class AnalyzeUserResponse
{
    [JsonPropertyName("studentName")]
    public string StudentName { get; set; } = string.Empty;

    [JsonPropertyName("githubUsername")]
    public string GithubUsername { get; set; } = string.Empty;

    [JsonPropertyName("totalRepos")]
    public int TotalRepos { get; set; }

    [JsonPropertyName("successCount")]
    public int SuccessCount { get; set; }

    [JsonPropertyName("failureCount")]
    public int FailureCount { get; set; }

    [JsonPropertyName("aiSummary")]
    public string AiSummary { get; set; } = string.Empty;

    [JsonPropertyName("repositories")]
    public List<AnalyzeResponse> Repositories { get; set; } = new();
}

public class AnalyzeResponse
{
    [JsonPropertyName("studentName")]
    public string StudentName { get; set; } = string.Empty;

    [JsonPropertyName("repo")]
    public string Repo { get; set; } = string.Empty;

    [JsonPropertyName("isFork")]
    public bool IsFork { get; set; }

    [JsonPropertyName("contributionPercentage")]
    public double ContributionPercentage { get; set; }

    [JsonPropertyName("languages")]
    public List<string> Languages { get; set; } = new();

    [JsonPropertyName("frameworksDetected")]
    public List<string> FrameworksDetected { get; set; } = new();

    [JsonPropertyName("completenessScore")]
    public int CompletenessScore { get; set; }

    [JsonPropertyName("totalCommits")]
    public int TotalCommits { get; set; }

    [JsonPropertyName("finalScore")]
    public int FinalScore { get; set; }

    [JsonPropertyName("codeQualityScore")]
    public int? CodeQualityScore { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    // Used internally — not exposed in API response
    [JsonIgnore]
    public CompletenessBreakdown? CompletenessBreakdown { get; set; }
}

public class RepoMetrics
{
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime PushedAt { get; set; }
    public int Size { get; set; }
    public int TotalCommits { get; set; }
    public int FileCount { get; set; }
    public int DirectoryCount { get; set; }
    public int CodeFileCount { get; set; }
    public int ReadmeSize { get; set; }
    public List<string> Languages { get; set; } = new();
    public int ContributorCount { get; set; }
    public int BranchCount { get; set; }
    public int PullRequestCount { get; set; }
    public int ReleaseCount { get; set; }
    public bool IsFork { get; set; }
}

public class CompletenessBreakdown
{
    [JsonPropertyName("hasReadme")]
    public bool HasReadme { get; set; }

    [JsonPropertyName("hasProjectFile")]
    public bool HasProjectFile { get; set; }

    [JsonPropertyName("hasSrcDirectory")]
    public bool HasSrcDirectory { get; set; }

    [JsonPropertyName("hasTestDirectory")]
    public bool HasTestDirectory { get; set; }

    [JsonPropertyName("hasMultipleBranches")]
    public bool HasMultipleBranches { get; set; }

    [JsonPropertyName("hasMultipleContributors")]
    public bool HasMultipleContributors { get; set; }

    [JsonPropertyName("hasReleases")]
    public bool HasReleases { get; set; }

    [JsonPropertyName("hasPullRequests")]
    public bool HasPullRequests { get; set; }

    [JsonPropertyName("hasRecentActivity")]
    public bool HasRecentActivity { get; set; }

    [JsonPropertyName("hasMultipleLanguages")]
    public bool HasMultipleLanguages { get; set; }
}

public class CodeMetrics
{
    public int TotalFiles { get; set; }
    public int CodeFiles { get; set; }
    public int TotalLinesOfCode { get; set; }
    public int AverageLinesPerFile { get; set; }
    public int FunctionCount { get; set; }
    public int ClassCount { get; set; }
    public int LoopCount { get; set; }
    public int ConditionalCount { get; set; }
    public int ErrorHandlingCount { get; set; }
    public int ImportCount { get; set; }
    public int CommentLines { get; set; }
    public bool HasStructure { get; set; }
    public int ReadmeSize { get; set; }
}
