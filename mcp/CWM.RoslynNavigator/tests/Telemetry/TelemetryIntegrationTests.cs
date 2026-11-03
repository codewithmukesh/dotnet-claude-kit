using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using CWM.RoslynNavigator.Telemetry;
using CWM.RoslynNavigator.Tests.Fixtures;
using CWM.RoslynNavigator.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CWM.RoslynNavigator.Tests.Telemetry;

/// <summary>
/// Drives real tool responses through the telemetry path against the sample solution, so
/// the measurement is exercised end to end rather than against hand-written JSON.
/// </summary>
[Collection(SampleSolutionCollection.Name)]
public sealed class TelemetryIntegrationTests(TestSolutionFixture fixture)
    : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "cwm-tel-" + Guid.NewGuid().ToString("N"));

    private ToolTelemetry CreateTelemetry(bool enabled = true) =>
        new(new TelemetryOptions(enabled, _root),
            fixture.WorkspaceManager,
            NullLoggerFactory.Instance.CreateLogger<ToolTelemetry>());

    /// <summary>Drains the queue the way TelemetryWriter would, without hosting it.</summary>
    private static List<CallRecord> Drain(ToolTelemetry telemetry)
    {
        var records = new List<CallRecord>();
        while (telemetry.Queue.TryRead(out var record)) records.Add(record);
        return records;
    }

    [Fact]
    public async Task Record_RealFindSymbolResponse_CapturesNonZeroBaseline()
    {
        var telemetry = CreateTelemetry();

        var json = await FindSymbolTool.ExecuteAsync(
            fixture.WorkspaceManager, "OrderService", "class",
            ct: TestContext.Current.CancellationToken);

        telemetry.Record("find_symbol", json, transportError: false, elapsedMs: 12);

        var record = Assert.Single(Drain(telemetry));
        Assert.Equal("find_symbol", record.Tool);
        Assert.True(record.Ok);
        Assert.Equal(TelemetryStates.Ok, record.State);
        Assert.Equal(1, record.Files);
        Assert.True(record.BaselineTokens > 0, "OrderService.cs has real size on disk");
        Assert.True(record.ResponseTokens > 0);
        Assert.True(record.Saved > 0, "reading the file costs more than the symbol record");
        Assert.Equal(12, record.ElapsedMs);
    }

    [Fact]
    public async Task Record_SecondCallForSameFile_ClaimsNoFurtherSavings()
    {
        var telemetry = CreateTelemetry();

        for (var i = 0; i < 2; i++)
        {
            var json = await FindSymbolTool.ExecuteAsync(
                fixture.WorkspaceManager, "OrderService", "class",
                ct: TestContext.Current.CancellationToken);
            telemetry.Record("find_symbol", json, transportError: false, elapsedMs: 5);
        }

        var records = Drain(telemetry);

        Assert.Equal(2, records.Count);
        Assert.True(records[0].Saved > 0);

        // The file was already in context by then; charging it again would inflate the total.
        Assert.Equal(0, records[1].BaselineTokens);
        Assert.Equal(0, records[1].Saved);
    }

    [Fact]
    public async Task Record_ErrorResponse_IsCountedButClaimsNothing()
    {
        var telemetry = CreateTelemetry();

        var json = await FindReferencesTool.ExecuteAsync(
            fixture.WorkspaceManager, "ZZZDoesNotExist",
            ct: TestContext.Current.CancellationToken);

        telemetry.Record("find_references", json, transportError: false, elapsedMs: 3);

        var record = Assert.Single(Drain(telemetry));
        Assert.Equal(TelemetryStates.Error, record.State);
        Assert.Equal(ErrorCodes.SymbolNotFound, record.ErrorCode);
        Assert.False(record.Ok);
        Assert.Equal(0, record.Saved);
    }

    [Fact]
    public async Task Record_FirstCallRacedWorkspaceLoad_StillCapturesRepoScale()
    {
        // Reproduces what a real client does: the first tool call arrives while the
        // workspace is still loading, so scale reads as zero. If that zero is latched,
        // repo.json claims the repository has no source in it for the whole session and
        // the per-call cap is silently disabled.
        var telemetry = CreateTelemetry();

        telemetry.Record("find_symbol",
            JsonSerializer.Serialize(new StatusResponse("Loading", "Workspace is loading.")),
            transportError: false, elapsedMs: 2);

        var loading = Assert.Single(Drain(telemetry));
        Assert.Equal(TelemetryStates.Loading, loading.State);

        // A later call, workspace now ready.
        var json = await FindSymbolTool.ExecuteAsync(
            fixture.WorkspaceManager, "OrderService", "class",
            ct: TestContext.Current.CancellationToken);
        telemetry.Record("find_symbol", json, transportError: false, elapsedMs: 6);

        Drain(telemetry);

        var repoJson = await File.ReadAllTextAsync(
            Path.Combine(telemetry.Store.RepoDirectory!, "repo.json"),
            TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(repoJson);

        Assert.True(doc.RootElement.GetProperty("projectCount").GetInt32() > 0);
        Assert.True(doc.RootElement.GetProperty("totalSourceBytes").GetInt64() > 0);
    }

    [Fact]
    public void Record_WhenDisabled_WritesNothing()
    {
        var telemetry = CreateTelemetry(enabled: false);

        telemetry.Record("find_symbol", "{\"Count\":1}", transportError: false, elapsedMs: 1);

        Assert.Empty(Drain(telemetry));
        Assert.False(Directory.Exists(_root), "disabled telemetry must not touch the disk");
    }

    [Fact]
    public async Task Store_RoundTripsRecordsThroughDisk()
    {
        var telemetry = CreateTelemetry();

        var json = await FindSymbolTool.ExecuteAsync(
            fixture.WorkspaceManager, "Order", "class",
            ct: TestContext.Current.CancellationToken);
        telemetry.Record("find_symbol", json, transportError: false, elapsedMs: 7);

        telemetry.Store.Append(Drain(telemetry));
        var readBack = telemetry.Store.ReadAll();

        var record = Assert.Single(readBack);
        Assert.Equal("find_symbol", record.Tool);
        Assert.Equal(7, record.ElapsedMs);
        Assert.True(record.BaselineTokens > 0);
    }

    [Fact]
    public async Task GetTokenSavings_AfterRecordedCalls_ReportsSavingsAndMethodology()
    {
        var telemetry = CreateTelemetry();

        foreach (var name in new[] { "OrderService", "ProductService", "Order" })
        {
            var json = await FindSymbolTool.ExecuteAsync(
                fixture.WorkspaceManager, name, "class",
                ct: TestContext.Current.CancellationToken);
            telemetry.Record("find_symbol", json, transportError: false, elapsedMs: 4);
        }

        telemetry.Store.Append(Drain(telemetry));

        var services = new ServiceCollection().AddSingleton(telemetry).BuildServiceProvider();

        var result = JsonSerializer.Deserialize<TokenSavingsResult>(
            await GetTokenSavingsTool.ExecuteAsync(
                services, fixture.WorkspaceManager, ct: TestContext.Current.CancellationToken))!;

        Assert.True(result.Enabled);
        Assert.Equal(3, result.TotalCalls);
        Assert.True(result.SavedTokens > 0);
        Assert.True(result.RepoTokens > 0);

        var tool = Assert.Single(result.ByTool);
        Assert.Equal("find_symbol", tool.Tool);
        Assert.Equal(3, tool.Calls);

        // The methodology must travel with the number, and must not overstate its precision.
        Assert.NotNull(result.Method);
        Assert.False(result.Method.DivisorCalibrated);
        Assert.Contains("understates", result.Method.Baseline);
        Assert.Contains("uncalibrated", result.Message);
    }

    [Fact]
    public async Task GetTokenSavings_WhenDisabled_SaysSoAndReportsNothing()
    {
        var services = new ServiceCollection()
            .AddSingleton(CreateTelemetry(enabled: false))
            .BuildServiceProvider();

        var result = JsonSerializer.Deserialize<TokenSavingsResult>(
            await GetTokenSavingsTool.ExecuteAsync(
                services, fixture.WorkspaceManager, ct: TestContext.Current.CancellationToken))!;

        Assert.False(result.Enabled);
        Assert.Equal(0, result.TotalCalls);
        // The off-state message must tell the caller both how to turn it on and that doing
        // so does not send anything anywhere.
        Assert.Contains(TelemetryOptions.EnvironmentVariable, result.Message);
        Assert.Contains("transmitted", result.Message);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }
}
