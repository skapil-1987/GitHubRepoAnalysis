using System.Text.Json.Serialization;

namespace GithubRepoAnalysis.Models;

public class AnalyzeRequest
{
    [JsonPropertyName("studentName")]
    public string StudentName { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("githubRepoUrl")]
    public string GithubRepoUrl { get; set; } = string.Empty;
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

    [JsonPropertyName("aiInsights")]
    public string AiInsights { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}
