using System.Net;
using System.Text.Json;
using GithubRepoAnalysis.Models;
using GithubRepoAnalysis.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace GithubRepoAnalysis.Functions;

public class AnalyzeRepoFunction
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IAnalysisService _analysisService;
    private readonly ILogger<AnalyzeRepoFunction> _logger;

    public AnalyzeRepoFunction(IAnalysisService analysisService, ILogger<AnalyzeRepoFunction> logger)
    {
        _analysisService = analysisService;
        _logger = logger;
    }

    [Function("AnalyzeUser")]
    public async Task<IActionResult> RunUser(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "analyze-user")] HttpRequest req,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("AnalyzeUser function invoked.");

        AnalyzeUserRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<AnalyzeUserRequest>(
                req.Body, JsonOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid JSON payload.");
            return new BadRequestObjectResult(new { error = "Invalid JSON payload." });
        }

        if (request is null ||
            string.IsNullOrWhiteSpace(request.GithubProfileUrl) ||
            string.IsNullOrWhiteSpace(request.StudentName))
        {
            return new BadRequestObjectResult(new
            {
                error = "studentName and githubProfileUrl are required."
            });
        }

        try
        {
            var result = await _analysisService.AnalyzeUserAsync(request, cancellationToken);
            return new OkObjectResult(result);
        }
        catch (InvalidOperationException ex)
        {
            return new NotFoundObjectResult(new { error = ex.Message });
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "GitHub API request failed.");
            return new ObjectResult(new { error = "GitHub API request failed.", details = ex.Message })
            {
                StatusCode = (int)HttpStatusCode.BadGateway
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error analyzing user repositories.");
            return new ObjectResult(new { error = "Internal server error." })
            {
                StatusCode = (int)HttpStatusCode.InternalServerError
            };
        }
    }
}
