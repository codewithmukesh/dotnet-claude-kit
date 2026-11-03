namespace CWM.RoslynNavigator.Bench.Corpus;

/// <summary>Size of a corpus as a reader would judge it: projects declared, C# files and bytes in their directories.</summary>
internal sealed record CorpusStats(int ProjectsDeclared, int CsFiles, long CsBytes)
{
    public static CorpusStats Measure(string solutionPath)
    {
        var scope = SolutionScope.For(solutionPath);
        var files = scope.Files(".cs").ToList();
        var bytes = files.Sum(f => new FileInfo(f).Length);
        return new CorpusStats(scope.ProjectFiles.Count, files.Count, bytes);
    }
}
