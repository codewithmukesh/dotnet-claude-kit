using Microsoft.CodeAnalysis;

namespace CWM.RoslynNavigator.Tests;

public class DocumentResolverTests
{
    private static Solution BuildSolution()
    {
        var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;

        foreach (var (name, file) in new[]
        {
            ("App", "/repo/App/Program.cs"),
            ("App2", "/repo/App2/Program.cs"),
            ("Lib", "/repo/Lib/MyProgram.cs")
        })
        {
            var projectId = ProjectId.CreateNewId(name);
            solution = solution.AddProject(projectId, name, name, LanguageNames.CSharp)
                .AddDocument(DocumentId.CreateNewId(projectId), Path.GetFileName(file), "class C {}", filePath: file);
        }

        return solution;
    }

    [Theory]
    [InlineData("App/Program.cs")]
    [InlineData("./App/Program.cs")]
    [InlineData(".\\App\\Program.cs")]
    [InlineData("/repo/App/Program.cs")]
    [InlineData("app/program.cs")]
    public void FindDocuments_PathVariants_ResolveToSingleDocument(string path)
    {
        var matches = DocumentResolver.FindDocuments(BuildSolution(), path);

        var match = Assert.Single(matches);
        Assert.Equal("/repo/App/Program.cs", match.FilePath);
    }

    [Fact]
    public void FindDocuments_BareNameInTwoProjects_ReturnsBoth_ButNotPartialNameMatches()
    {
        var matches = DocumentResolver.FindDocuments(BuildSolution(), "Program.cs");

        Assert.Equal(["/repo/App/Program.cs", "/repo/App2/Program.cs"], matches.Select(d => d.FilePath));
    }

    [Theory]
    [InlineData("Missing.cs")]
    [InlineData("./")]
    [InlineData("")]
    public void FindDocuments_NoMatch_ReturnsEmpty(string path)
    {
        Assert.Empty(DocumentResolver.FindDocuments(BuildSolution(), path));
    }
}
