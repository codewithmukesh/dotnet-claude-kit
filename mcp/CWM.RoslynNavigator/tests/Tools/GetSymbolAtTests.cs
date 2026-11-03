using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using CWM.RoslynNavigator.Tests.Fixtures;
using CWM.RoslynNavigator.Tools;

namespace CWM.RoslynNavigator.Tests.Tools;

[Collection(SampleSolutionCollection.Name)]
public class GetSymbolAtTests(TestSolutionFixture fixture)
{
    private const string OrderService = "SampleApi/OrderService.cs";
    private const string GetByIdAsyncId =
        "M:SampleDomain.IOrderRepository.GetByIdAsync(System.Guid,System.Threading.CancellationToken)";

    [Fact]
    public async Task GetSymbolAt_ByText_ResolvesTheCalledMethod()
    {
        var result = await GetAsync(OrderService, 19, text: "GetByIdAsync");

        Assert.Equal("GetByIdAsync", result.Name);
        Assert.Equal("method", result.Kind);
        Assert.Equal("SampleDomain.IOrderRepository", result.ContainingType);
        // Roslyn appends "~ReturnType" to method declaration IDs; the prefix is the C# doc ID.
        Assert.StartsWith(GetByIdAsyncId, result.SymbolId);
        Assert.Equal("SampleDomain/IOrderRepository.cs", result.DefinitionFile);
        Assert.False(result.IsDeclaration);
        Assert.Equal("System.Threading.Tasks.Task<SampleDomain.Order?>", result.DeclaredType);
    }

    [Fact]
    public async Task GetSymbolAt_ByColumn_MatchesByText()
    {
        var column = "        return await _repository.GetByIdAsync(id, ct);".IndexOf("GetByIdAsync", StringComparison.Ordinal) + 1;

        var result = await GetAsync(OrderService, 19, column: column);

        Assert.StartsWith(GetByIdAsyncId, result.SymbolId);
        Assert.Equal(column, result.Column);
    }

    [Fact]
    public async Task GetSymbolAt_LocalAfterNullCheck_ReportsDeclaredAndFlowNullability()
    {
        // var order = await ... ?? throw ...;  order.Cancel();
        var result = await GetAsync(OrderService, 33, text: "order");

        Assert.Equal("local", result.Kind);
        Assert.Null(result.SymbolId);
        Assert.Equal("SampleDomain.Order?", result.DeclaredType);
        Assert.Equal("nullable", result.DeclaredNullability);
        Assert.Equal("not-null", result.ExpressionNullability);
        Assert.Equal(31, result.DefinitionLine);
    }

    [Fact]
    public async Task GetSymbolAt_ParameterDeclaration_IsDeclaration()
    {
        var result = await GetAsync(OrderService, 17, text: "id");

        Assert.Equal("parameter", result.Kind);
        Assert.True(result.IsDeclaration);
        Assert.Equal("System.Guid", result.DeclaredType);
    }

    [Fact]
    public async Task GetSymbolAt_NoTextOrColumn_UsesFirstBoundIdentifier()
    {
        var result = await GetAsync(OrderService, 33);

        Assert.Equal("order", result.Token);
    }

    [Fact]
    public async Task GetSymbolAt_MetadataSymbol_ReportsAssembly()
    {
        var result = await GetAsync(OrderService, 17, text: "Guid");

        Assert.Equal("metadata", result.DefinitionFile);
        Assert.Equal("T:System.Guid", result.SymbolId);
        Assert.NotNull(result.Assembly);
    }

    [Theory]
    [InlineData("SampleApi/NoSuchFile.cs", 1, "x", ErrorCodes.FileNotFound)]
    [InlineData(OrderService, 9999, "x", ErrorCodes.InvalidArgument)]
    [InlineData(OrderService, 19, "NotOnThisLine", ErrorCodes.SymbolNotFound)]
    public async Task GetSymbolAt_BadInput_ReturnsStructuredError(string file, int line, string text, string code)
    {
        var json = await GetSymbolAtTool.ExecuteAsync(
            fixture.WorkspaceManager, file, line, text, ct: TestContext.Current.CancellationToken);

        Assert.Equal(code, JsonSerializer.Deserialize<ErrorResponse>(json)!.Error);
    }

    public static TheoryData<string> SymbolTools =>
        ["find_references", "find_callers", "get_symbol_detail", "get_symbol_source", "analyze_change_impact", "find_implementations"];

    [Theory]
    [MemberData(nameof(SymbolTools))]
    public async Task SymbolId_FromGetSymbolAt_ResolvesInSymbolTools(string tool)
    {
        var ct = TestContext.Current.CancellationToken;
        var id = (await GetAsync(OrderService, 19, text: "GetByIdAsync")).SymbolId!;

        var json = tool switch
        {
            "find_references" => await FindReferencesTool.ExecuteAsync(fixture.WorkspaceManager, id, ct: ct),
            "find_callers" => await FindCallersTool.ExecuteAsync(fixture.WorkspaceManager, id, className: "Ignored", ct: ct),
            "get_symbol_detail" => await GetSymbolDetailTool.ExecuteAsync(fixture.WorkspaceManager, id, ct: ct),
            "get_symbol_source" => await GetSymbolSourceTool.ExecuteAsync(fixture.WorkspaceManager, id, ct: ct),
            "analyze_change_impact" => await AnalyzeChangeImpactTool.ExecuteAsync(fixture.WorkspaceManager, id, ct: ct),
            "find_implementations" => await FindImplementationsTool.ExecuteAsync(fixture.WorkspaceManager, "T:SampleDomain.IOrderRepository", ct: ct),
            _ => throw new ArgumentOutOfRangeException(nameof(tool))
        };

        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.TryGetProperty("Error", out var error), $"{tool}: {error}");
    }

    [Fact]
    public async Task SymbolId_WithoutReturnTypeSuffix_StillResolves()
    {
        // The plain C# documentation-comment form, as an agent would write it by hand.
        var json = await GetSymbolDetailTool.ExecuteAsync(
            fixture.WorkspaceManager, GetByIdAsyncId, ct: TestContext.Current.CancellationToken);

        Assert.Equal("SampleDomain/IOrderRepository.cs", JsonSerializer.Deserialize<SymbolDetail>(json)!.File);
    }

    [Fact]
    public async Task SymbolId_UnknownId_ReturnsSymbolNotFound()
    {
        var json = await FindReferencesTool.ExecuteAsync(
            fixture.WorkspaceManager, "M:SampleDomain.Nope.Missing", ct: TestContext.Current.CancellationToken);

        Assert.Equal(ErrorCodes.SymbolNotFound, JsonSerializer.Deserialize<ErrorResponse>(json)!.Error);
    }

    [Fact]
    public async Task FindSymbol_Results_CarrySymbolIdThatRoundTrips()
    {
        var ct = TestContext.Current.CancellationToken;
        var found = JsonSerializer.Deserialize<SymbolSearchResult>(
            await FindSymbolTool.ExecuteAsync(fixture.WorkspaceManager, "GetByIdAsync", "method", ct: ct))!;

        Assert.All(found.Symbols, s => Assert.StartsWith("M:", s.SymbolId));

        // The bare name is ambiguous; each ID names exactly one symbol.
        foreach (var symbol in found.Symbols)
        {
            var detail = JsonSerializer.Deserialize<SymbolDetail>(
                await GetSymbolDetailTool.ExecuteAsync(fixture.WorkspaceManager, symbol.SymbolId!, ct: ct))!;
            Assert.Equal(symbol.File, detail.File);
            Assert.Equal(symbol.Line, detail.Line);
        }
    }

    [Fact]
    public async Task AmbiguousMatch_Candidates_CarrySymbolIds()
    {
        var error = JsonSerializer.Deserialize<ErrorResponse>(
            await FindReferencesTool.ExecuteAsync(fixture.WorkspaceManager, "GetByIdAsync",
                ct: TestContext.Current.CancellationToken))!;

        Assert.All(error.Candidates!, c => Assert.StartsWith("M:", c.SymbolId));
    }

    private async Task<SymbolAtResult> GetAsync(string file, int line, string? text = null, int? column = null)
    {
        var json = await GetSymbolAtTool.ExecuteAsync(
            fixture.WorkspaceManager, file, line, text, column, ct: TestContext.Current.CancellationToken);
        Assert.DoesNotContain("\"Error\"", json);
        return JsonSerializer.Deserialize<SymbolAtResult>(json)!;
    }
}
