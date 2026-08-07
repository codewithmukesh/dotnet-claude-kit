using CWM.RoslynNavigator.Tests.Fixtures;

namespace CWM.RoslynNavigator.Tests;

public class SymbolResolverTests(TestSolutionFixture fixture) : IClassFixture<TestSolutionFixture>
{
    [Theory]
    [InlineData("GetByIdAsync", "GetByIdAsync")]
    [InlineData("OrderService.GetOrderAsync", "OrderService.GetOrderAsync")]
    [InlineData("  OrderService.GetOrderAsync  ", "OrderService.GetOrderAsync")]
    [InlineData("GetByIdAsync(Guid, CancellationToken)", "GetByIdAsync")]
    [InlineData("IRepository<Order>.GetByIdAsync", "IRepository.GetByIdAsync")]
    [InlineData("IRepository`1.GetByIdAsync", "IRepository.GetByIdAsync")]
    [InlineData("Outer<T>.Inner<TKey, TValue>.Run(int x)", "Outer.Inner.Run")]
    [InlineData("", "")]
    public void NormalizeRequestedName_StripsNoiseButKeepsSegments(string input, string expected)
    {
        Assert.Equal(expected, SymbolResolver.NormalizeRequestedName(input));
    }

    [Theory]
    [InlineData("GetByIdAsync", "GetByIdAsync")]
    [InlineData("OrderService.GetOrderAsync", "GetOrderAsync")]
    [InlineData("SampleApi.OrderService.GetOrderAsync", "GetOrderAsync")]
    public void GetSimpleName_ReturnsFinalSegment(string input, string expected)
    {
        Assert.Equal(expected, SymbolResolver.GetSimpleName(input));
    }

    [Fact]
    public async Task FindSymbolsByName_BareMemberName_MatchesEveryDeclaringType()
    {
        // GetByIdAsync is declared on both repository interfaces and all three implementations.
        var symbols = await SymbolResolver.FindSymbolsByNameAsync(
            fixture.WorkspaceManager, "GetByIdAsync", ct: TestContext.Current.CancellationToken);

        Assert.True(symbols.Count > 1, $"expected multiple matches, got {symbols.Count}");
    }

    [Fact]
    public async Task FindSymbolsByName_TypeQualified_NarrowsToOneDeclaringType()
    {
        var symbols = await SymbolResolver.FindSymbolsByNameAsync(
            fixture.WorkspaceManager, "IOrderRepository.GetByIdAsync", ct: TestContext.Current.CancellationToken);

        Assert.Single(symbols);
        Assert.Equal("IOrderRepository", symbols[0].ContainingType.Name);
    }

    [Fact]
    public async Task FindSymbolsByName_FullyQualified_Resolves()
    {
        var symbols = await SymbolResolver.FindSymbolsByNameAsync(
            fixture.WorkspaceManager,
            "SampleApi.OrderService.CreateOrderAsync",
            ct: TestContext.Current.CancellationToken);

        Assert.Single(symbols);
        Assert.Equal("CreateOrderAsync", symbols[0].Name);
    }

    [Fact]
    public async Task FindSymbolsByName_QualifiedWithParameterList_Resolves()
    {
        var symbols = await SymbolResolver.FindSymbolsByNameAsync(
            fixture.WorkspaceManager,
            "OrderService.GetOrderAsync(Guid, CancellationToken)",
            ct: TestContext.Current.CancellationToken);

        Assert.Single(symbols);
        Assert.Equal("OrderService", symbols[0].ContainingType.Name);
    }

    [Fact]
    public async Task FindSymbolsByName_QualifierNamesWrongType_ReturnsEmpty()
    {
        // ProductService has no CreateOrderAsync — a bare-name search would have found
        // OrderService's and reported it under the wrong owner.
        var symbols = await SymbolResolver.FindSymbolsByNameAsync(
            fixture.WorkspaceManager,
            "ProductService.CreateOrderAsync",
            ct: TestContext.Current.CancellationToken);

        Assert.Empty(symbols);
    }

    [Fact]
    public async Task FindSymbolsByName_PartialSegment_DoesNotMatch()
    {
        // Suffix matching must align on segment boundaries: "derService" is not "OrderService".
        var symbols = await SymbolResolver.FindSymbolsByNameAsync(
            fixture.WorkspaceManager,
            "derService.GetOrderAsync",
            ct: TestContext.Current.CancellationToken);

        Assert.Empty(symbols);
    }

    [Fact]
    public async Task FindSymbolsByName_QualifiedTypeName_Resolves()
    {
        var symbols = await SymbolResolver.FindSymbolsByNameAsync(
            fixture.WorkspaceManager, "SampleDomain.Order", ct: TestContext.Current.CancellationToken);

        Assert.Single(symbols);
        Assert.Equal("Order", symbols[0].Name);
    }

    [Fact]
    public async Task ResolveSymbol_QualifiedName_ResolvesWithoutFileHint()
    {
        var symbol = await SymbolResolver.ResolveSymbolAsync(
            fixture.WorkspaceManager,
            "IProductRepository.GetByIdAsync",
            ct: TestContext.Current.CancellationToken);

        Assert.NotNull(symbol);
        Assert.Equal("IProductRepository", symbol.ContainingType.Name);
    }
}
