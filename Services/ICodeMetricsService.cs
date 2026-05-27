using GithubRepoAnalysis.Models;

namespace GithubRepoAnalysis.Services;

public interface ICodeMetricsService
{
    CodeMetrics ExtractMetrics(IEnumerable<(string Path, string Content)> codeFiles, int readmeSize, bool hasStructure);
}
