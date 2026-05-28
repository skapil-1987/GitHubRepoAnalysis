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
    private readonly IExcelExportService _excelExportService;
    private readonly IExcelImportService _excelImportService;
    private readonly ILogger<AnalyzeRepoFunction> _logger;

    public AnalyzeRepoFunction(
        IAnalysisService analysisService,
        IExcelExportService excelExportService,
        IExcelImportService excelImportService,
        ILogger<AnalyzeRepoFunction> logger)
    {
        _analysisService    = analysisService;
        _excelExportService = excelExportService;
        _excelImportService = excelImportService;
        _logger             = logger;
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

    [Function("ExportUser")]
    public async Task<IActionResult> RunExport(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "analyze-user/export")] HttpRequest req,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("ExportUser function invoked.");

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
            var result   = await _analysisService.AnalyzeUserAsync(request, cancellationToken);
            var bytes    = _excelExportService.Export(result, request.Email, request.GithubProfileUrl);
            var fileName = $"{SanitizeFileName(request.StudentName)}_GitHubAnalysis.xlsx";

            return new FileContentResult(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
            {
                FileDownloadName = fileName
            };
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
            _logger.LogError(ex, "Unhandled error exporting user repositories.");
            return new ObjectResult(new { error = "Internal server error." })
            {
                StatusCode = (int)HttpStatusCode.InternalServerError
            };
        }
    }

    private static string SanitizeFileName(string name) =>
        string.Concat(name.Split(Path.GetInvalidFileNameChars())).Replace(' ', '_');

    [Function("BulkExportUsersJson")]
    public async Task<IActionResult> RunBulkExportJson(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "analyze-users/bulk-export-json")] HttpRequest req,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("BulkExportUsersJson function invoked.");

        List<AnalyzeUserRequest>? requests;
        try
        {
            requests = await JsonSerializer.DeserializeAsync<List<AnalyzeUserRequest>>(
                req.Body, JsonOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid JSON payload.");
            return new BadRequestObjectResult(new { error = "Invalid JSON payload. Expected an array of student requests." });
        }

        if (requests is null || requests.Count == 0)
            return new BadRequestObjectResult(new { error = "Request array is empty." });

        // Validate each entry
        var invalid = requests
            .Select((r, i) => (r, i))
            .Where(x => string.IsNullOrWhiteSpace(x.r.StudentName) || string.IsNullOrWhiteSpace(x.r.GithubProfileUrl))
            .ToList();

        if (invalid.Count > 0)
        {
            var indices = string.Join(", ", invalid.Select(x => x.i + 1));
            return new BadRequestObjectResult(new
            {
                error = $"Row(s) {indices} are missing studentName or githubProfileUrl."
            });
        }

        _logger.LogInformation("Processing {Count} students from JSON payload.", requests.Count);

        var results = new List<(AnalyzeUserRequest Request, AnalyzeUserResponse Response)>();

        foreach (var request in requests)
        {
            try
            {
                var response = await _analysisService.AnalyzeUserAsync(request, cancellationToken);
                results.Add((request, response));
                _logger.LogInformation("Completed analysis for {Student}.", request.StudentName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to analyze student {Student} — skipping.", request.StudentName);
            }
        }

        if (results.Count == 0)
            return new ObjectResult(new { error = "All student analyses failed." })
            {
                StatusCode = (int)HttpStatusCode.BadGateway
            };

        var bytes    = _excelExportService.ExportBulk(results);
        var fileName = $"GitHubAnalysis_Bulk_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx";

        return new FileContentResult(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
        {
            FileDownloadName = fileName
        };
    }

    [Function("BulkExportUsers")]
    public async Task<IActionResult> RunBulkExport(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "analyze-users/bulk-export")] HttpRequest req,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("BulkExportUsers function invoked.");

        if (!req.ContentType?.Contains("multipart/form-data", StringComparison.OrdinalIgnoreCase) ?? true)
            return new BadRequestObjectResult(new { error = "Request must be multipart/form-data with an Excel file in the 'file' field." });

        var file = req.Form.Files.GetFile("file");
        if (file is null || file.Length == 0)
            return new BadRequestObjectResult(new { error = "No file uploaded. Send an Excel (.xlsx) file in the 'file' form field." });

        List<AnalyzeUserRequest> requests;
        try
        {
            using var stream = file.OpenReadStream();
            requests = _excelImportService.ReadRequests(stream);
        }
        catch (InvalidOperationException ex)
        {
            return new BadRequestObjectResult(new { error = ex.Message });
        }

        if (requests.Count == 0)
            return new BadRequestObjectResult(new { error = "No valid rows found in the uploaded Excel file." });

        _logger.LogInformation("Processing {Count} students from uploaded Excel.", requests.Count);

        var results = new List<(AnalyzeUserRequest Request, AnalyzeUserResponse Response)>();

        foreach (var request in requests)
        {
            try
            {
                var response = await _analysisService.AnalyzeUserAsync(request, cancellationToken);
                results.Add((request, response));
                _logger.LogInformation("Completed analysis for {Student}.", request.StudentName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to analyze student {Student} — skipping.", request.StudentName);
            }
        }

        if (results.Count == 0)
            return new ObjectResult(new { error = "All student analyses failed." })
            {
                StatusCode = (int)HttpStatusCode.BadGateway
            };

        var bytes    = _excelExportService.ExportBulk(results);
        var fileName = $"GitHubAnalysis_Bulk_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx";

        return new FileContentResult(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
        {
            FileDownloadName = fileName
        };
    }
}
