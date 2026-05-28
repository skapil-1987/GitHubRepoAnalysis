using GithubRepoAnalysis.Models;

namespace GithubRepoAnalysis.Services;

public interface IExcelExportService
{
    /// <summary>
    /// Generates an Excel (.xlsx) file from a single student's analysis result.
    /// </summary>
    byte[] Export(AnalyzeUserResponse response, string email, string githubProfileUrl);

    /// <summary>
    /// Generates an Excel (.xlsx) file with one sheet per student from bulk input.
    /// </summary>
    byte[] ExportBulk(IReadOnlyList<(AnalyzeUserRequest Request, AnalyzeUserResponse Response)> results);
}
