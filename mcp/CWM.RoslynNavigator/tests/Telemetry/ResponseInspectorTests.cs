using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using CWM.RoslynNavigator.Telemetry;

namespace CWM.RoslynNavigator.Tests.Telemetry;

/// <summary>
/// The inspector is what lets measurement happen without touching tool code, so it is
/// tested against the real response records rather than hand-written JSON — if a response
/// shape changes, these fail rather than silently measuring nothing.
/// </summary>
public class ResponseInspectorTests
{
    [Fact]
    public void Inspect_SymbolSearchResult_ExtractsFilesAndCount()
    {
        var json = JsonSerializer.Serialize(new SymbolSearchResult(
            [
                new SymbolLocation("Order", "class", "Domain/Order.cs", 5, "Shop", false),
                new SymbolLocation("Order", "class", "Api/Order.cs", 9, "Shop.Api", false)
            ],
            Count: 2, TotalFound: 2, Truncated: false, Limit: 50));

        var facts = ResponseInspector.Inspect(json);

        Assert.Equal(["Domain/Order.cs", "Api/Order.cs"], facts.Files);
        Assert.Equal(2, facts.Results);
        Assert.Equal(CallOutcome.Ok, facts.Outcome);
    }

    [Fact]
    public void Inspect_ErrorResponse_ReportsErrorAndCode()
    {
        var json = JsonSerializer.Serialize(new ErrorResponse(
            ErrorCodes.SymbolNotFound, "No symbol named 'Nope' exists."));

        var facts = ResponseInspector.Inspect(json);

        Assert.Equal(CallOutcome.Error, facts.Outcome);
        Assert.Equal(ErrorCodes.SymbolNotFound, facts.ErrorCode);
        Assert.Empty(facts.Files);
    }

    [Fact]
    public void Inspect_StatusResponse_ReportsNotReady()
    {
        var json = JsonSerializer.Serialize(new StatusResponse("Loading", "Still loading."));

        var facts = ResponseInspector.Inspect(json);

        Assert.Equal(CallOutcome.NotReady, facts.Outcome);
    }

    [Fact]
    public void Inspect_TestCoverageMap_IncludesTestFilePaths()
    {
        var json = JsonSerializer.Serialize(new TestCoverageMapResult(
            [new TestCoverageEntry("OrderService", "Api/OrderService.cs", true, "Tests/OrderServiceTests.cs")],
            TotalTypes: 1, TestedTypes: 1, Percentage: 100));

        var facts = ResponseInspector.Inspect(json);

        // The answer genuinely derives from both files, so both belong in the baseline.
        Assert.Contains("Api/OrderService.cs", facts.Files);
        Assert.Contains("Tests/OrderServiceTests.cs", facts.Files);
    }

    [Fact]
    public void Inspect_UnknownPlaceholder_IsNotCountedAsAFile()
    {
        var json = JsonSerializer.Serialize(new SymbolSearchResult(
            [new SymbolLocation("X", "class", "unknown", 0, "N", false)],
            Count: 1, TotalFound: 1, Truncated: false, Limit: 50));

        var facts = ResponseInspector.Inspect(json);

        Assert.Empty(facts.Files);
    }

    [Fact]
    public void Inspect_NestedFileProperties_AreFound()
    {
        // ChangeImpact nests paths one level down, inside Files[].
        var json = JsonSerializer.Serialize(new ChangeImpactResult(
            "Shop.Order", "class", "public", true, 3, 1, 2, 0, 0, 0,
            [new ImpactProject("Api", 3, false)],
            [new ImpactFile("Api/A.cs", 2, false), new ImpactFile("Api/B.cs", 1, false)],
            [], 0, false, 50, "low", "contained"));

        var facts = ResponseInspector.Inspect(json);

        Assert.Contains("Api/A.cs", facts.Files);
        Assert.Contains("Api/B.cs", facts.Files);
    }

    [Fact]
    public void Inspect_ResponseWithoutCount_FallsBackToFileCount()
    {
        var json = JsonSerializer.Serialize(new SymbolSourceResult(
            "GetOrderAsync", "method", "Api/OrderService.cs", 10, 20, "...", false));

        var facts = ResponseInspector.Inspect(json);

        Assert.Equal(1, facts.Results);
        Assert.Equal(["Api/OrderService.cs"], facts.Files);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{\"unterminated\": ")]
    public void Inspect_UnusableInput_DoesNotThrow(string? json)
    {
        var facts = ResponseInspector.Inspect(json);

        Assert.Empty(facts.Files);
    }
}
