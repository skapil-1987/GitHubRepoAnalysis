using System.Text;
using System.Text.Json;
using GithubRepoAnalysis.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GithubRepoAnalysis.Services;

public class OpenAIService : IOpenAIService
{
    public const string HttpClientName = "AzureOpenAI";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OpenAIService> _logger;

    public OpenAIService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<OpenAIService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<string> GetInsightsAsync(AnalyzeResponse summary, CancellationToken ct = default)
    {
        var endpoint = _configuration["AzureOpenAI:Endpoint"];
        var apiKey = _configuration["AzureOpenAI:ApiKey"];
        var deployment = _configuration["AzureOpenAI:Deployment"];
        var apiVersion = _configuration["AzureOpenAI:ApiVersion"] ?? "2024-06-01";

        if (string.IsNullOrWhiteSpace(endpoint) ||
            string.IsNullOrWhiteSpace(apiKey) ||
            string.IsNullOrWhiteSpace(deployment))
        {
            _logger.LogInformation("Azure OpenAI not configured; skipping AI insights.");
            return string.Empty;
        }

        var client = _httpClientFactory.CreateClient(HttpClientName);

        var prompt = $@"You are evaluating a student's GitHub repository.
Repository: {summary.Repo}
Languages: {string.Join(", ", summary.Languages)}
Frameworks: {string.Join(", ", summary.FrameworksDetected)}
Total commits: {summary.TotalCommits}
Student contribution: {summary.ContributionPercentage}%
Completeness score: {summary.CompletenessScore}

Provide concise feedback covering:
1. Code quality
2. Complexity summary
3. Strengths and weaknesses";

        var payload = new
        {
            messages = new[]
            {
                new { role = "system", content = "You are an expert code reviewer providing concise feedback." },
                new { role = "user", content = prompt }
            },
            max_tokens = 500,
            temperature = 0.3
        };

        var url = $"{endpoint.TrimEnd('/')}/openai/deployments/{deployment}/chat/completions?api-version={apiVersion}";
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("api-key", apiKey);

        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("Azure OpenAI returned {Status}: {Error}", response.StatusCode, error);
            return string.Empty;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;
    }
}
