namespace CWM.RoslynNavigator.Tests;

/// <summary>
/// Paths compare case-insensitively only where the file system does: Windows and macOS.
/// On Linux <c>Src/Foo.cs</c> and <c>src/Foo.cs</c> are different files.
/// </summary>
public sealed class PathComparisonTests
{
    [Theory]
    [InlineData(true, false, true)]   // Windows
    [InlineData(false, true, true)]   // macOS
    [InlineData(false, false, false)] // Linux and every other Unix
    public void IsCaseInsensitive_ByOperatingSystem(bool isWindows, bool isMacOS, bool expected) =>
        Assert.Equal(expected, PathComparison.IsCaseInsensitive(isWindows, isMacOS));

    [Fact]
    public void IgnoreCase_MatchesCurrentOperatingSystem()
    {
        var expected = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

        Assert.Equal(expected, PathComparison.IgnoreCase);
        Assert.Equal(expected ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal, PathComparison.Comparison);
        Assert.Same(expected ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal, PathComparison.Comparer);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void Comparer_DifferentCase_SameKeyOnlyWhenCaseInsensitive(bool ignoreCase, bool expectSame)
    {
        var set = new HashSet<string>(PathComparison.ComparerFor(ignoreCase)) { "/repo/Src/Orders.csproj" };

        Assert.Equal(expectSame, set.Contains("/repo/src/orders.csproj"));
    }

    [Theory]
    [InlineData("/repo/src/Orders", "/repo/src", true, true)]
    [InlineData("/repo/src/Orders", "/repo/src", false, true)]
    [InlineData("/repo/Src/Orders", "/repo/src", true, true)]
    [InlineData("/repo/Src/Orders", "/repo/src", false, false)]
    [InlineData("/repo/src2", "/repo/src", true, false)]
    public void IsSameOrUnder_RespectsCaseRule(string path, string root, bool ignoreCase, bool expected)
    {
        var sep = Path.DirectorySeparatorChar;
        Assert.Equal(expected, WorkspaceManager.IsSameOrUnder(
            path.Replace('/', sep), root.Replace('/', sep), PathComparison.ComparisonFor(ignoreCase)));
    }
}
