using CWM.RoslynNavigator.Analyzers;

namespace CWM.RoslynNavigator.Tests.Analyzers;

public class GlobMatcherTests
{
    [Theory]
    [InlineData("src/Outbox/**", "src/Outbox/Drainer.cs")]
    [InlineData("src/Outbox/**", "src/Outbox/Deep/Nested/Drainer.cs")]
    [InlineData("**/Seeders/**", "src/Api/Seeders/DemoSeeder.cs")]
    [InlineData("src/*/Program.cs", "src/Api/Program.cs")]
    [InlineData("src/**/*Seeder.cs", "src/Api/Data/DemoSeeder.cs")]
    [InlineData("src/Api/Program.cs", "src/Api/Program.cs")]
    [InlineData("src\\Api\\**", "src/Api/Thing.cs")]
    public void IsMatch_MatchingPattern_ReturnsTrue(string pattern, string path) =>
        Assert.True(GlobMatcher.IsMatch(pattern, path), $"'{pattern}' should match '{path}'");

    [Theory]
    [InlineData("src/Outbox/**", "src/Comms/Drainer.cs")]
    [InlineData("src/*/Program.cs", "src/Api/Nested/Program.cs")]
    [InlineData("src/**/*Seeder.cs", "src/Api/Data/SeederFactory.cs")]
    [InlineData("src/Api", "src/Api/Thing.cs")]
    public void IsMatch_NonMatchingPattern_ReturnsFalse(string pattern, string path) =>
        Assert.False(GlobMatcher.IsMatch(pattern, path), $"'{pattern}' should not match '{path}'");
}

public sealed class SuppressionSetTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"cwm-suppression-{Guid.NewGuid():N}");

    public SuppressionSetTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private SuppressionSet Load(string json)
    {
        File.WriteAllText(Path.Combine(_directory, SuppressionSet.ConfigFileName), json);
        return SuppressionSet.Load(_directory);
    }

    [Fact]
    public void Load_NoConfig_HasNoRules()
    {
        var set = SuppressionSet.Load(_directory);

        Assert.False(set.HasRules);
        Assert.Null(set.ConfigPath);
    }

    [Fact]
    public void Load_DisabledDetector_IsReportedDisabled()
    {
        var set = Load("""{ "antipatterns": { "disable": ["AP008"] } }""");

        Assert.True(set.IsDisabled("AP008"));
        Assert.False(set.IsDisabled("AP005"));
        Assert.NotNull(set.ConfigPath);
    }

    [Fact]
    public void PathSuppressionReason_PathRule_SuppressesMatchingPathsOnly()
    {
        var set = Load("""
            { "antipatterns": { "suppress": [
                { "id": "AP005", "paths": ["src/Outbox/**"], "reason": "bounded resilience wrappers" }
            ] } }
            """);

        Assert.Equal("bounded resilience wrappers", set.PathSuppressionReason("AP005", "src/Outbox/Drainer.cs"));
        Assert.Null(set.PathSuppressionReason("AP005", "src/Api/OrderService.cs"));
        Assert.Null(set.PathSuppressionReason("AP010", "src/Outbox/Drainer.cs"));
    }

    [Fact]
    public void PathSuppressionReason_WildcardId_SuppressesEveryDetectorUnderPath()
    {
        var set = Load("""
            { "antipatterns": { "suppress": [
                { "id": "*", "paths": ["src/Legacy/**"], "reason": "frozen legacy module" }
            ] } }
            """);

        Assert.Equal("frozen legacy module", set.PathSuppressionReason("AP005", "src/Legacy/Old.cs"));
        Assert.Equal("frozen legacy module", set.PathSuppressionReason("AP010", "src/Legacy/Old.cs"));
    }

    [Fact]
    public void Load_MalformedConfig_HasNoRules()
    {
        var set = Load("{ this is not json");

        Assert.False(set.HasRules);
    }

    [Fact]
    public void Load_ConfigInAncestorDirectory_IsFound()
    {
        File.WriteAllText(
            Path.Combine(_directory, SuppressionSet.ConfigFileName),
            """{ "antipatterns": { "disable": ["AP009"] } }""");

        var nested = Path.Combine(_directory, "src", "Api");
        Directory.CreateDirectory(nested);

        Assert.True(SuppressionSet.Load(nested).IsDisabled("AP009"));
    }
}
