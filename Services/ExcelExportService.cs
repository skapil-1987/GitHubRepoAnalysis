using ClosedXML.Excel;
using GithubRepoAnalysis.Models;

namespace GithubRepoAnalysis.Services;

public class ExcelExportService : IExcelExportService
{
    public byte[] Export(AnalyzeUserResponse response, string email, string githubProfileUrl)
    {
        using var workbook = new XLWorkbook();
        AddStudentSheet(workbook, response, email, githubProfileUrl);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public byte[] ExportBulk(IReadOnlyList<(AnalyzeUserRequest Request, AnalyzeUserResponse Response)> results)
    {
        using var workbook = new XLWorkbook();

        var sorted = results.OrderBy(r => r.Request.StudentName, StringComparer.OrdinalIgnoreCase).ToList();

        // Summary sheet added first so it appears as the first tab
        WriteSummarySheet(workbook, sorted);

        foreach (var (req, res) in sorted)
            AddStudentSheet(workbook, res, req.Email, req.GithubProfileUrl);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void WriteSummarySheet(
        XLWorkbook workbook,
        IReadOnlyList<(AnalyzeUserRequest Request, AnalyzeUserResponse Response)> results)
    {
        var ws  = workbook.Worksheets.Add("Summary");
        var headerFill    = XLColor.FromHtml("#2E75B6");
        var acceptedFill  = XLColor.FromHtml("#C6EFCE"); // green
        var rejectedFill  = XLColor.FromHtml("#FFC7CE"); // red
        var reviewFill    = XLColor.FromHtml("#FFEB9C"); // yellow

        // ── Title ────────────────────────────────────────────────────────────
        var title = ws.Cell(1, 1);
        title.Value = "Internship Acceptance Summary";
        title.Style.Font.Bold      = true;
        title.Style.Font.FontSize  = 14;
        title.Style.Font.FontColor = XLColor.White;
        title.Style.Fill.BackgroundColor = headerFill;
        ws.Range(1, 1, 1, 3).Merge();

        // ── Column headers ────────────────────────────────────────────────────
        var headers = new[] { "Student Name", "Code Quality Score", "Acceptance Status" };
        for (int col = 0; col < headers.Length; col++)
        {
            var cell = ws.Cell(2, col + 1);
            cell.Value = headers[col];
            cell.Style.Font.Bold           = true;
            cell.Style.Font.FontColor      = XLColor.White;
            cell.Style.Fill.BackgroundColor = headerFill;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        // ── Data rows ─────────────────────────────────────────────────────────
        int row = 3;
        foreach (var (req, res) in results)
        {
            // Use average codeQualityScore across repos that have one
            var scores = res.Repositories
                .Where(r => r.CodeQualityScore.HasValue)
                .Select(r => r.CodeQualityScore!.Value)
                .ToList();

            var avgScore    = scores.Count > 0 ? (int)Math.Round(scores.Average()) : (int?)null;
            var (status, fill) = DetermineAcceptance(avgScore);

            ws.Cell(row, 1).Value = res.StudentName;
            ws.Cell(row, 1).Style.Fill.BackgroundColor = fill;

            if (avgScore.HasValue)
            {
                ws.Cell(row, 2).Value = avgScore.Value;
                ws.Cell(row, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }
            else
            {
                ws.Cell(row, 2).Value = "N/A";
                ws.Cell(row, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }
            ws.Cell(row, 2).Style.Fill.BackgroundColor = fill;

            ws.Cell(row, 3).Value = status;
            ws.Cell(row, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(row, 3).Style.Font.Bold             = true;
            ws.Cell(row, 3).Style.Fill.BackgroundColor  = fill;

            row++;
        }

        ws.Columns().AdjustToContents(15, 60);
    }

    /// <summary>
    /// Determines internship acceptance based on average code quality score.
    /// Score >= 70  → Accepted  (green)
    /// Score 50-69  → Under Review (yellow)
    /// Score < 50   → Rejected  (red)
    /// N/A          → Under Review (yellow)
    /// </summary>
    private static (string Status, XLColor Fill) DetermineAcceptance(int? score)
    {
        if (!score.HasValue)
            return ("Under Review", XLColor.FromHtml("#FFEB9C"));

        return score.Value switch
        {
            >= 70 => ("Accepted",     XLColor.FromHtml("#C6EFCE")),
            >= 50 => ("Under Review", XLColor.FromHtml("#FFEB9C")),
            _     => ("Rejected",     XLColor.FromHtml("#FFC7CE"))
        };
    }

    private static void AddStudentSheet(XLWorkbook workbook, AnalyzeUserResponse response, string email, string githubProfileUrl)
    {

        // Sanitize sheet name — Excel disallows: \ / * ? : [ ]
        var sheetName = SanitizeSheetName(response.StudentName);
        var ws = workbook.Worksheets.Add(sheetName);

        var row = 1;

        // ── Metadata section ─────────────────────────────────────────────────
        WriteMetadataSection(ws, ref row, response, email, githubProfileUrl);

        row++; // blank row between metadata and table

        // ── Table header ─────────────────────────────────────────────────────
        WriteTableHeader(ws, row);
        row++;

        // ── Table rows ───────────────────────────────────────────────────────
        foreach (var repo in response.Repositories)
        {
            WriteRepoRow(ws, row, repo);
            row++;
        }

        // ── Formatting ───────────────────────────────────────────────────────
        ws.Columns().AdjustToContents(10, 80); // min 10, max 80 char width
    }


    // ── Private helpers ──────────────────────────────────────────────────────

    private static void WriteMetadataSection(
        IXLWorksheet ws, ref int row,
        AnalyzeUserResponse response,
        string email,
        string githubProfileUrl)
    {
        var headerFill  = XLColor.FromHtml("#2E75B6");
        var labelFill   = XLColor.FromHtml("#D6E4F0");

        // Title row
        var titleCell = ws.Cell(row, 1);
        titleCell.Value = "Student GitHub Analysis Report";
        titleCell.Style.Font.Bold = true;
        titleCell.Style.Font.FontSize = 14;
        titleCell.Style.Font.FontColor = XLColor.White;
        titleCell.Style.Fill.BackgroundColor = headerFill;
        ws.Range(row, 1, row, 2).Merge();
        row++;

        var metadata = new (string Label, object? Value)[]
        {
            ("Name",           response.StudentName),
            ("Email",          string.IsNullOrWhiteSpace(email) ? "" : email),
            ("GitHub URL",     githubProfileUrl),
            ("Total Repos",    response.TotalRepos),
            ("Analyzed",       response.SuccessCount),
            ("Skipped/Failed", response.FailureCount),
            ("AI Summary",     response.AiSummary),
        };

        foreach (var (label, value) in metadata)
        {
            var labelCell = ws.Cell(row, 1);
            labelCell.Value = label;
            labelCell.Style.Font.Bold = true;
            labelCell.Style.Fill.BackgroundColor = labelFill;

            var valueCell = ws.Cell(row, 2);
            if (value is int i)        valueCell.Value = i;
            else                       valueCell.Value = value?.ToString() ?? "";

            if (label == "AI Summary")
            {
                valueCell.Style.Alignment.WrapText = true;
                ws.Row(row).Height = 60;
            }

            row++;
        }
    }

    private static readonly string[] Headers =
    [
        "Repo", "Is Fork", "Contribution %", "Languages", "Frameworks",
        "Total Commits", "Completeness Score", "Code Quality Score", "Final Score",
        "Strengths", "Areas to Improve", "Repo Verdict", "Message"
    ];

    private static void WriteTableHeader(IXLWorksheet ws, int row)
    {
        var fill  = XLColor.FromHtml("#2E75B6");

        for (int col = 0; col < Headers.Length; col++)
        {
            var cell = ws.Cell(row, col + 1);
            cell.Value = Headers[col];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = fill;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }
    }

    private static void WriteRepoRow(IXLWorksheet ws, int row, AnalyzeResponse repo)
    {
        var altFill = row % 2 == 0 ? XLColor.FromHtml("#EBF3FB") : XLColor.White;

        void Set(int col, object? val, bool wrap = false)
        {
            var cell = ws.Cell(row, col);
            if (val is int i)         cell.Value = i;
            else if (val is double d) cell.Value = d;
            else if (val is bool b)   cell.Value = b ? "Yes" : "No";
            else                      cell.Value = val?.ToString() ?? "";

            cell.Style.Fill.BackgroundColor = altFill;
            if (wrap) cell.Style.Alignment.WrapText = true;
        }

        Set(1,  repo.Repo);
        Set(2,  repo.IsFork);
        Set(3,  repo.ContributionPercentage);
        Set(4,  string.Join(", ", repo.Languages));
        Set(5,  string.Join(", ", repo.FrameworksDetected));
        Set(6,  repo.TotalCommits);
        Set(7,  repo.CompletenessScore);
        Set(8,  repo.CodeQualityScore.HasValue ? (object)repo.CodeQualityScore.Value : "N/A");
        Set(9,  repo.FinalScore);
        Set(10, repo.AiAnalysis != null ? string.Join("; ", repo.AiAnalysis.Strengths)      : "", wrap: true);
        Set(11, repo.AiAnalysis != null ? string.Join("; ", repo.AiAnalysis.AreasToImprove) : "", wrap: true);
        Set(12, repo.AiAnalysis?.RepoVerdict ?? "");
        Set(13, repo.Message ?? "");
    }

    private static string SanitizeSheetName(string name)
    {
        var invalid = new[] { '\\', '/', '*', '?', ':', '[', ']' };
        foreach (var c in invalid)
            name = name.Replace(c, '_');

        // Excel sheet name max length is 31
        return name.Length > 31 ? name[..31] : name;
    }
}
