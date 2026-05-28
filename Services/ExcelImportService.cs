using ClosedXML.Excel;
using GithubRepoAnalysis.Models;
using Microsoft.Extensions.Logging;

namespace GithubRepoAnalysis.Services;

public class ExcelImportService : IExcelImportService
{
    private readonly ILogger<ExcelImportService> _logger;

    public ExcelImportService(ILogger<ExcelImportService> logger)
    {
        _logger = logger;
    }

    public List<AnalyzeUserRequest> ReadRequests(Stream fileStream)
    {
        var requests = new List<AnalyzeUserRequest>();

        // Azure Functions multipart stream is not seekable.
        // ClosedXML requires a seekable stream — copy into MemoryStream first.
        using var memoryStream = new MemoryStream();
        fileStream.CopyTo(memoryStream);
        memoryStream.Position = 0;

        using var workbook  = new XLWorkbook(memoryStream);
        var worksheet       = workbook.Worksheets.First();
        var headerRow       = worksheet.Row(1);

        // Build column index map from header row (case-insensitive)
        var columnMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in headerRow.CellsUsed())
            columnMap[cell.GetString().Trim()] = cell.Address.ColumnNumber;

        // Validate required columns
        var required = new[] { "studentName", "githubProfileUrl" };
        foreach (var col in required)
        {
            if (!columnMap.ContainsKey(col))
                throw new InvalidOperationException(
                    $"Input Excel is missing required column: '{col}'. " +
                    $"Expected columns: studentName, email (optional), githubProfileUrl");
        }

        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;

        for (int rowNum = 2; rowNum <= lastRow; rowNum++)
        {
            var row = worksheet.Row(rowNum);

            var studentName    = GetCellValue(row, columnMap, "studentName");
            var email          = GetCellValue(row, columnMap, "email");
            var githubUrl      = GetCellValue(row, columnMap, "githubProfileUrl");

            // Skip completely empty rows
            if (string.IsNullOrWhiteSpace(studentName) && string.IsNullOrWhiteSpace(githubUrl))
                continue;

            if (string.IsNullOrWhiteSpace(studentName) || string.IsNullOrWhiteSpace(githubUrl))
            {
                _logger.LogWarning("Skipping row {Row} — missing studentName or githubProfileUrl.", rowNum);
                continue;
            }

            requests.Add(new AnalyzeUserRequest
            {
                StudentName      = studentName.Trim(),
                Email            = email.Trim(),
                GithubProfileUrl = githubUrl.Trim()
            });
        }

        _logger.LogInformation("Excel import: read {Count} valid student rows.", requests.Count);
        return requests;
    }

    private static string GetCellValue(IXLRow row, Dictionary<string, int> columnMap, string columnName)
    {
        if (!columnMap.TryGetValue(columnName, out var colNum))
            return string.Empty;

        return row.Cell(colNum).GetString() ?? string.Empty;
    }
}
