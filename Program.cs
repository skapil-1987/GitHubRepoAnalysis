using System.Net.Http.Headers;
using GithubRepoAnalysis.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

// GitHub HttpClient
builder.Services.AddHttpClient(GitHubService.HttpClientName, client =>
{
    client.BaseAddress = new Uri("https://api.github.com/");
    client.DefaultRequestHeaders.UserAgent.Add(
        new ProductInfoHeaderValue("GithubRepoAnalysis", "1.0"));
    client.DefaultRequestHeaders.Accept.Add(
        new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

    var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
    if (!string.IsNullOrWhiteSpace(token))
    {
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
    }
});

// Azure OpenAI HttpClient
builder.Services.AddHttpClient(OpenAIService.HttpClientName);

builder.Services.AddSingleton<IGitHubService, GitHubService>();
builder.Services.AddSingleton<IOpenAIService, OpenAIService>();
builder.Services.AddSingleton<ICompletenessScoreService, CompletenessScoreService>();
builder.Services.AddSingleton<ICodeMetricsService, CodeMetricsService>();
builder.Services.AddSingleton<IAnalysisService, AnalysisService>();
builder.Services.AddSingleton<IExcelExportService, ExcelExportService>();
builder.Services.AddSingleton<IExcelImportService, ExcelImportService>();

builder.Build().Run();
