using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using CWM.RoslynNavigator.Tests.Fixtures;
using CWM.RoslynNavigator.Tools;

namespace CWM.RoslynNavigator.Tests;

/// <summary>
/// Navigation tools across a workspace of two solutions that share SampleDomain. The bug
/// class: a tool that walks only the primary solution, or returns paths relative to the
/// wrong root, silently drops every answer living in the second solution.
/// </summary>
public sealed class MultiSolutionToolTests(MultiSolutionFixture fixture) : IClassFixture<MultiSolutionFixture>
{
    private const string Dispatcher = "SecondSolution/ShippingService/ShipmentDispatcher.cs";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WorkspaceManager Workspace => fixture.WorkspaceManager;

    [Fact]
    public async Task FindCallers_SharedInterfaceMethod_IncludesCallerFromSecondSolution()
    {
        // Act
        var json = await FindCallersTool.ExecuteAsync(Workspace, "GetByIdAsync", "IOrderRepository", ct: Ct);

        // Assert
        var result = JsonSerializer.Deserialize<CallersResult>(json)!;
        Assert.Contains(result.Callers, c => c is { ContainingType: "ShipmentDispatcher", Method: "DispatchAsync", File: Dispatcher });
        Assert.Contains(result.Callers, c => c.File.StartsWith("SampleSolution/SampleApi/", StringComparison.Ordinal));
        Assert.Equal(4, result.TotalFound);
    }

    [Fact]
    public async Task FindImplementations_SharedInterface_ReturnsRootRelativePathsOnce()
    {
        // Act — SampleDomain is declared by both solutions but loaded once.
        var json = await FindImplementationsTool.ExecuteAsync(Workspace, "IOrderRepository", ct: Ct);

        // Assert
        var result = JsonSerializer.Deserialize<ImplementationsResult>(json)!;
        Assert.Equal(
            ["SampleSolution/SampleInfrastructure/CachedOrderRepository.cs", "SampleSolution/SampleInfrastructure/InMemoryOrderRepository.cs"],
            result.Implementations.Select(i => i.File).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task GetSymbolAt_CallInSecondSolution_ResolvesToDefinitionInSharedProject()
    {
        // Act
        var json = await GetSymbolAtTool.ExecuteAsync(Workspace, Dispatcher, line: 12, text: "GetByIdAsync", ct: Ct);

        // Assert
        var result = JsonSerializer.Deserialize<SymbolAtResult>(json)!;
        Assert.Equal("GetByIdAsync", result.Name);
        Assert.False(result.IsDeclaration);
        Assert.Equal("SampleSolution/SampleDomain/IOrderRepository.cs", result.DefinitionFile);
    }

    [Fact]
    public async Task AnalyzeChangeImpact_SymbolUsedOnlyInSecondSolution_ReportsThatProject()
    {
        // Act — Order.Ship is called only by ShipmentDispatcher.
        var json = await AnalyzeChangeImpactTool.ExecuteAsync(Workspace, "Order.Ship", ct: Ct);

        // Assert
        var result = JsonSerializer.Deserialize<ChangeImpactResult>(json)!;
        var project = Assert.Single(result.Projects);
        Assert.Equal("ShippingService", project.Name);
        Assert.Equal(Dispatcher, Assert.Single(result.Files).File);
        Assert.True(result.CrossesAssemblyBoundary);
    }
}
