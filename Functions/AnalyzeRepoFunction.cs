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

    [Function("AnalyzeRepo")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "analyze")] HttpRequest req,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("AnalyzeRepo function invoked.");

        AnalyzeRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<AnalyzeRequest>(
                req.Body, JsonOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid JSON payload.");
            return new BadRequestObjectResult(new { error = "Invalid JSON payload." });
        }

        if (request is null ||
            string.IsNullOrWhiteSpace(request.GithubRepoUrl) ||
            string.IsNullOrWhiteSpace(request.StudentName))
        {
            return new BadRequestObjectResult(new
            {
                error = "studentName and githubRepoUrl are required."
            });
        }

        try
        {
            var result = await _analysisService.AnalyzeAsync(request, cancellationToken);
            return new OkObjectResult(result);
        }
        catch (ArgumentException ex)
        {
            return new BadRequestObjectResult(new { error = ex.Message });
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
            _logger.LogError(ex, "Unhandled error analyzing repository.");
            return new ObjectResult(new { error = "Internal server error." })
            {
                StatusCode = (int)HttpStatusCode.InternalServerError
            };
        }
    }
}
