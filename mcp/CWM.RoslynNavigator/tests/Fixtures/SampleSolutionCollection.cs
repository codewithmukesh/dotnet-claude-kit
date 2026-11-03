namespace CWM.RoslynNavigator.Tests.Fixtures;

/// <summary>
/// Shares one loaded sample solution across every test class that only reads it. A class
/// fixture per test class meant one MSBuildWorkspace load and BuildHost per class, all
/// starting at once. Tests that mutate workspace state (reloads, build-file changes) create
/// their own <see cref="WorkspaceManager"/> instead of joining this collection.
/// </summary>
[CollectionDefinition(Name)]
public sealed class SampleSolutionCollection : ICollectionFixture<TestSolutionFixture>
{
    public const string Name = "SampleSolution";
}
