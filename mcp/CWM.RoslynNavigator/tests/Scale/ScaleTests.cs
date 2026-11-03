namespace CWM.RoslynNavigator.Tests.Scale;

/// <summary>
/// Measures the 50-project baseline once per run. Budgets are relative to it so the tests
/// hold on a fast laptop and a slow CI runner alike: what they catch is a tool whose cost
/// grows faster than the solution, not a slow machine.
/// </summary>
public sealed class ScaleBaselineFixture : IAsyncLifetime
{
    public const int BaselineProjects = 50;

    public ScaleMeasurement Baseline { get; private set; } = null!;

    public async ValueTask InitializeAsync() =>
        Baseline = await ScaleMeasurement.MeasureAsync(BaselineProjects, CancellationToken.None);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ScaleCollection : ICollectionFixture<ScaleBaselineFixture>
{
    public const string Name = "Scale";
}

/// <summary>
/// Large-solution behaviour of the heavy tools. Excluded from the default run
/// (tests/default.runsettings and CI's test filter leave Category=Scale out); a dedicated CI job runs them on pushes
/// to main and on demand. Locally: <c>dotnet test --settings tests/scale.runsettings</c>.
/// </summary>
[Collection(ScaleCollection.Name)]
[Trait("Category", "Scale")]
public sealed class ScaleTests(ScaleBaselineFixture fixture)
{
    /// <summary>Time may grow up to 1.2× linearly with project count (10× projects → 12×).</summary>
    private const double TimeGrowthAllowance = 1.2;

    /// <summary>
    /// Baselines below this are clamped up to it: a 3 ms baseline × 12 is still noise, and a
    /// relative budget on noise is a flaky test.
    /// </summary>
    private static readonly TimeSpan BaselineFloor = TimeSpan.FromMilliseconds(250);

    /// <summary>Every heavy tool's response stays under this however large the solution.</summary>
    private const int ResponseBytesCeiling = 128 * 1024;

    /// <summary>
    /// Known super-linear tools and the growth exponent they are held to instead of 1.
    /// find_dead_code runs a solution-wide SymbolFinder search per candidate that survives
    /// the name pre-filter: candidates grow with N and each search walks N projects, so it is
    /// O(N²) (measured: 471 ms at 50 projects, 1.3 s at 200, 9.3 s at 500). Held to quadratic
    /// so anything worse still fails; drop the entry once it is indexed.
    /// </summary>
    private static readonly Dictionary<string, double> KnownGrowthExponents = new(StringComparer.Ordinal)
    {
        ["find_dead_code"] = 2.0,
    };

    [Theory]
    [InlineData(200, 1_024L)]
    [InlineData(500, 2_048L)]
    public async Task HeavyTools_LargeSolution_StayWithinRelativeBudgets(int projects, long managedMegabytesCeiling)
    {
        // Arrange
        var baseline = fixture.Baseline;
        var scale = (double)projects / ScaleBaselineFixture.BaselineProjects;

        // Act
        var measured = await ScaleMeasurement.MeasureAsync(projects, TestContext.Current.CancellationToken);

        // Assert
        var failures = new List<string>();
        foreach (var tool in measured.Tools)
        {
            var reference = Max(baseline[tool.Tool].Elapsed, BaselineFloor);
            var growth = Math.Pow(scale, KnownGrowthExponents.GetValueOrDefault(tool.Tool, 1.0));
            var budget = reference * growth * TimeGrowthAllowance;
            if (tool.Elapsed > budget)
                failures.Add($"{tool.Tool}: {tool.Elapsed.TotalMilliseconds:F0} ms > budget {budget.TotalMilliseconds:F0} ms ({scale}× projects from {baseline[tool.Tool].Elapsed.TotalMilliseconds:F0} ms)");
            if (tool.ResponseBytes > ResponseBytesCeiling)
                failures.Add($"{tool.Tool}: {tool.ResponseBytes} bytes > {ResponseBytesCeiling}");
        }

        var loadBudget = Max(baseline.Load + baseline.WarmUp, BaselineFloor) * scale * TimeGrowthAllowance;
        if (measured.Load + measured.WarmUp > loadBudget)
            failures.Add($"load+warm-up {(measured.Load + measured.WarmUp).TotalSeconds:F1}s > budget {loadBudget.TotalSeconds:F1}s");

        var managedMegabytes = measured.ManagedBytesAfterWarmUp / 1_048_576;
        if (managedMegabytes > managedMegabytesCeiling)
            failures.Add($"managed heap after warm-up {managedMegabytes} MB > {managedMegabytesCeiling} MB");

        Assert.True(failures.Count == 0,
            $"{string.Join("\n", failures)}\n\nBaseline:\n{baseline.Describe()}\n\nMeasured:\n{measured.Describe()}");
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}
