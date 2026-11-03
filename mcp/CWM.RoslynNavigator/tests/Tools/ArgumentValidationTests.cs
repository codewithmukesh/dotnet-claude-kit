using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using CWM.RoslynNavigator.Tests.Fixtures;

namespace CWM.RoslynNavigator.Tests.Tools;

/// <summary>
/// Unknown enumerated values and filters that match nothing must come back as a structured
/// error. They used to fall through to the default branch, so a typo returned a complete,
/// plausible answer (the whole solution, or an empty "nothing here") to the wrong question.
/// </summary>
[Collection(SampleSolutionCollection.Name)]
public sealed class ArgumentValidationTests(TestSolutionFixture fixture)
{
    public static TheoryData<string, string, object, string> InvalidArguments => new()
    {
        { "detect_antipatterns", "severity", "catastrophic", ErrorCodes.InvalidArgument },
        { "detect_antipatterns", "scope", "tests-only", ErrorCodes.InvalidArgument },
        { "detect_antipatterns", "confidence", "certain", ErrorCodes.InvalidArgument },
        { "detect_circular_dependencies", "scope", "galaxies", ErrorCodes.InvalidArgument },
        { "find_dead_code", "scope", "galaxy", ErrorCodes.InvalidArgument },
        { "find_dead_code", "kind", "field", ErrorCodes.InvalidArgument },
        { "get_diagnostics", "scope", "galaxy", ErrorCodes.InvalidArgument },
        { "get_diagnostics", "severityFilter", "warnings", ErrorCodes.InvalidArgument },
        { "get_di_registrations", "file", "NoSuchFile12345.cs", ErrorCodes.FileNotFound },
        { "get_endpoint_map", "file", "NoSuchFile12345.cs", ErrorCodes.FileNotFound },
        { "get_test_coverage_map", "projectFilter", "NoSuchProject12345", ErrorCodes.InvalidArgument },
    };

    [Theory]
    [MemberData(nameof(InvalidArguments))]
    public async Task Invoke_InvalidArgument_ReturnsStructuredError(
        string tool, string parameter, object value, string expectedError)
    {
        // Act
        var json = await ToolCatalog.Get(tool).InvokeAsync(
            fixture.WorkspaceManager,
            new Dictionary<string, object?> { [parameter] = value },
            TestContext.Current.CancellationToken);

        // Assert
        var error = JsonSerializer.Deserialize<ErrorResponse>(json)!;
        Assert.Equal(expectedError, error.Error);
        Assert.Contains(value.ToString()!, error.Message);
    }

    [Theory]
    [InlineData("find_dead_code", "scope", "Project")]
    [InlineData("get_diagnostics", "severityFilter", "ERROR")]
    [InlineData("detect_antipatterns", "severity", "Error")]
    [InlineData("detect_circular_dependencies", "scope", "Types")]
    public async Task Invoke_EnumValueInOtherCase_IsAccepted(string tool, string parameter, string value)
    {
        // Act
        var json = await ToolCatalog.Get(tool).InvokeAsync(
            fixture.WorkspaceManager,
            new Dictionary<string, object?> { [parameter] = value, ["path"] = "SampleDomain", ["projectFilter"] = "SampleDomain" },
            TestContext.Current.CancellationToken);

        // Assert
        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.TryGetProperty("Error", out _), json);
    }

    [Fact]
    public async Task FindDeadCode_ProjectScopeInUpperCase_ScopesToThatProject()
    {
        // Act — "Project" used to miss the case-sensitive switch and scan the whole solution.
        var json = await ToolCatalog.Get("find_dead_code").InvokeAsync(
            fixture.WorkspaceManager,
            new Dictionary<string, object?> { ["scope"] = "Project", ["path"] = "SampleDomain" },
            TestContext.Current.CancellationToken);

        // Assert
        var result = JsonSerializer.Deserialize<DeadCodeResult>(json)!;
        Assert.NotEmpty(result.Symbols);
        Assert.All(result.Symbols, s => Assert.StartsWith("SampleDomain/", s.File));
    }
}
