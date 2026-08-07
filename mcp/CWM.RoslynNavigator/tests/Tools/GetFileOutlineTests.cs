using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using CWM.RoslynNavigator.Tests.Fixtures;
using CWM.RoslynNavigator.Tools;

namespace CWM.RoslynNavigator.Tests.Tools;

public class GetFileOutlineTests(TestSolutionFixture fixture) : IClassFixture<TestSolutionFixture>
{
    [Fact]
    public async Task GetFileOutline_ReturnsNamespaceUsingsAndTypeSkeleton()
    {
        var json = await GetFileOutlineTool.ExecuteAsync(
            fixture.WorkspaceManager, "OrderService.cs", ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<FileOutlineResult>(json)!;

        Assert.Equal("SampleApi/OrderService.cs", result.File);
        Assert.Equal("SampleApi", result.Namespace);
        Assert.Equal(1, result.UsingCount);

        var type = Assert.Single(result.Types);
        Assert.Equal("OrderService", type.Name);
        Assert.Equal("class", type.Kind);
        Assert.True(type.Line > 0);
    }

    [Fact]
    public async Task GetFileOutline_MembersHaveSignaturesAndLines_NoBodies()
    {
        var json = await GetFileOutlineTool.ExecuteAsync(
            fixture.WorkspaceManager, "OrderService.cs", ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<FileOutlineResult>(json)!;

        var members = result.Types[0].Members;
        var getOrder = members.Single(m => m.Signature.Contains("GetOrderAsync"));

        Assert.Equal("method", getOrder.Kind);
        Assert.Contains("Task<Order?> GetOrderAsync(Guid id", getOrder.Signature);
        Assert.True(getOrder.Line > 0);
        Assert.DoesNotContain("GetByIdAsync", string.Join("\n", members.Select(m => m.Signature)));

        Assert.Contains(members, m => m.Kind == "constructor");
        Assert.Contains(members, m => m.Kind == "field");
    }

    [Fact]
    public async Task GetFileOutline_MaxResults_CapsWithTotalFound()
    {
        var json = await GetFileOutlineTool.ExecuteAsync(
            fixture.WorkspaceManager, "OrderService.cs", maxResults: 1, ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<FileOutlineResult>(json)!;

        Assert.Equal(1, result.Count);
        Assert.True(result.TotalFound > 1);
    }

    [Fact]
    public async Task GetFileOutline_UnknownFile_ReturnsFileNotFound()
    {
        var json = await GetFileOutlineTool.ExecuteAsync(
            fixture.WorkspaceManager, "NoSuchFile.cs", ct: TestContext.Current.CancellationToken);
        var error = JsonSerializer.Deserialize<ErrorResponse>(json)!;

        Assert.Equal(ErrorCodes.FileNotFound, error.Error);
    }
}
