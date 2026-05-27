using System.Text.RegularExpressions;
using GithubRepoAnalysis.Models;

namespace GithubRepoAnalysis.Services;

public class CodeMetricsService : ICodeMetricsService
{
    // Language-agnostic keyword patterns
    private static readonly Regex FunctionPattern    = new(@"\b(function|def|void|public|private|protected|static)\b", RegexOptions.Compiled);
    private static readonly Regex ClassPattern       = new(@"\b(class|struct|interface)\b", RegexOptions.Compiled);
    private static readonly Regex LoopPattern        = new(@"\b(for|while|foreach)\b", RegexOptions.Compiled);
    private static readonly Regex ConditionalPattern = new(@"\b(if|else|switch|case)\b", RegexOptions.Compiled);
    private static readonly Regex ErrorPattern       = new(@"\b(try|catch|throw|finally|except)\b", RegexOptions.Compiled);
    private static readonly Regex ImportPattern      = new(@"\b(import|require|using|include)\b", RegexOptions.Compiled);
    private static readonly Regex CommentPattern     = new(@"(^\s*(//|#|/\*))", RegexOptions.Compiled | RegexOptions.Multiline);

    public CodeMetrics ExtractMetrics(
        IEnumerable<(string Path, string Content)> codeFiles,
        int readmeSize,
        bool hasStructure)
    {
        var metrics = new CodeMetrics
        {
            ReadmeSize   = readmeSize,
            HasStructure = hasStructure
        };

        var fileList = codeFiles.ToList();
        metrics.CodeFiles  = fileList.Count;
        metrics.TotalFiles = fileList.Count;

        foreach (var (_, content) in fileList)
        {
            var lines = content.Split('\n');
            metrics.TotalLinesOfCode   += lines.Length;
            metrics.FunctionCount      += FunctionPattern.Matches(content).Count;
            metrics.ClassCount         += ClassPattern.Matches(content).Count;
            metrics.LoopCount          += LoopPattern.Matches(content).Count;
            metrics.ConditionalCount   += ConditionalPattern.Matches(content).Count;
            metrics.ErrorHandlingCount += ErrorPattern.Matches(content).Count;
            metrics.ImportCount        += ImportPattern.Matches(content).Count;
            metrics.CommentLines       += lines.Count(l => CommentPattern.IsMatch(l));
        }

        metrics.AverageLinesPerFile = metrics.CodeFiles > 0
            ? metrics.TotalLinesOfCode / metrics.CodeFiles
            : 0;

        return metrics;
    }
}
