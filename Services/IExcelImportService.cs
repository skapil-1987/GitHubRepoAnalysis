using GithubRepoAnalysis.Models;

namespace GithubRepoAnalysis.Services;

public interface IExcelImportService
{
    /// <summary>
    /// Reads the uploaded Excel file and returns one AnalyzeUserRequest per row.
    /// Expected columns (case-insensitive): studentName, email, githubProfileUrl
    /// </summary>
    List<AnalyzeUserRequest> ReadRequests(Stream fileStream);
}
