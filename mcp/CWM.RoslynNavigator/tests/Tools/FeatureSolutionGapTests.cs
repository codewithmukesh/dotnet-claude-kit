using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using CWM.RoslynNavigator.Tests.Fixtures;
using CWM.RoslynNavigator.Tools;

namespace CWM.RoslynNavigator.Tests.Tools;

/// <summary>
/// Overrides and DI registrations end to end through the tools: SampleSolution has neither,
/// so find_overrides' and get_di_registrations' result and truncation paths were only ever
/// reached through syntax-level unit tests.
/// </summary>
public sealed class FeatureSolutionGapTests(FeatureSolutionFixture fixture) : IClassFixture<FeatureSolutionFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WorkspaceManager Workspace => fixture.WorkspaceManager;

    [Fact]
    public async Task FindOverrides_AbstractMethod_ReturnsEveryOverride()
    {
        // Act
        var json = await FindOverridesTool.ExecuteAsync(Workspace, "Area", "Shape", ct: Ct);

        // Assert
        var result = JsonSerializer.Deserialize<OverridesResult>(json)!;
        Assert.Equal(["Circle", "Square", "Triangle"], result.Overrides.Select(o => o.ContainingType).Order());
        Assert.False(result.Truncated);
    }

    [Fact]
    public async Task FindOverrides_MaxResultsBelowTotal_TruncatesAndReportsTotal()
    {
        // Act
        var json = await FindOverridesTool.ExecuteAsync(Workspace, "Area", "Shape", maxResults: 1, ct: Ct);

        // Assert
        var result = JsonSerializer.Deserialize<OverridesResult>(json)!;
        ToolGapTests.AssertTruncated(result.Overrides.Count, result.Count, result.TotalFound, result.Truncated, result.Limit, total: 3);
    }

    [Fact]
    public async Task GetDiRegistrations_Solution_ReturnsEveryRegistration()
    {
        // Act
        var json = await GetDiRegistrationsTool.ExecuteAsync(Workspace, ct: Ct);

        // Assert
        var result = JsonSerializer.Deserialize<DiRegistrationsResult>(json)!;
        Assert.Equal(["scoped", "singleton", "transient"], result.Registrations.Select(r => r.Lifetime).Order());
        Assert.Equal(3, result.TotalFound);
        Assert.False(result.Truncated);
    }

    [Fact]
    public async Task GetDiRegistrations_MaxResultsBelowTotal_TruncatesAndReportsTotal()
    {
        // Act
        var json = await GetDiRegistrationsTool.ExecuteAsync(Workspace, maxResults: 1, ct: Ct);

        // Assert
        var result = JsonSerializer.Deserialize<DiRegistrationsResult>(json)!;
        ToolGapTests.AssertTruncated(result.Registrations.Count, result.Count, result.TotalFound, result.Truncated, result.Limit, total: 3);
    }

    [Fact]
    public async Task GetDiRegistrations_FileFilterMatchingRegistrationFile_ReturnsThem()
    {
        // Act
        var json = await GetDiRegistrationsTool.ExecuteAsync(Workspace, file: "Startup.cs", ct: Ct);

        // Assert
        Assert.Equal(3, JsonSerializer.Deserialize<DiRegistrationsResult>(json)!.TotalFound);
    }

    [Fact]
    public async Task GetDiRegistrations_FileFilterOnExistingFileWithoutRegistrations_ReturnsEmptyNotError()
    {
        // Act — Shapes.cs exists, it just registers nothing: empty is the truthful answer.
        var json = await GetDiRegistrationsTool.ExecuteAsync(Workspace, file: "Shapes.cs", ct: Ct);

        // Assert
        var result = JsonSerializer.Deserialize<DiRegistrationsResult>(json)!;
        Assert.Empty(result.Registrations);
        Assert.Equal(0, result.TotalFound);
    }

    [Fact]
    public async Task GetEndpointMap_FileFilterOnExistingFileWithoutEndpoints_ReturnsEmptyNotError()
    {
        // Act
        var json = await GetEndpointMapTool.ExecuteAsync(Workspace, file: "Startup.cs", ct: Ct);

        // Assert
        var result = JsonSerializer.Deserialize<EndpointMapResult>(json)!;
        Assert.Empty(result.Endpoints);
    }
}
