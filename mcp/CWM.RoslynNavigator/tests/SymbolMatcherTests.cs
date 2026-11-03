namespace CWM.RoslynNavigator.Tests;

public class SymbolMatcherTests
{
    [Theory]
    [InlineData("GetByIdAsync", "GetByIdAsync", "Substring", "Exact")]
    [InlineData("GetByIdAsync", "getby", "Prefix", "Prefix")]
    [InlineData("GetByIdAsync", "GBIA", "CamelHump", "CamelHump")]
    [InlineData("GetByIdAsync", "GetAsync", "CamelHump", "CamelHump")]
    [InlineData("GetByIdAsync", "IdAsync", "Substring", "Substring")]
    [InlineData("GetByIdAsync", "IdAsync", "CamelHump", "None")]
    [InlineData("GetByIdAsync", "getby", "Exact", "None")]
    [InlineData("GetByIdAsync", "AsyncGet", "CamelHump", "None")]
    [InlineData("HTTPClient", "HC", "CamelHump", "CamelHump")]
    public void Classify_NameAndQuery_ReturnsBestTierWithinMode(string name, string query, string mode, string expected) =>
        Assert.Equal(Enum.Parse<MatchTier>(expected), SymbolMatcher.Classify(name, query, Enum.Parse<MatchMode>(mode)));

    [Fact]
    public void SplitHumps_MixedIdentifier_SplitsOnCaseDigitsAndUnderscores() =>
        Assert.Equal(["Get", "Order", "2", "Async", "x"], SymbolMatcher.SplitHumps("GetOrder2Async_x"));

    [Theory]
    [InlineData("OrderService", "OrderServce", 1)]
    [InlineData("OrderService", "orderservice", 0)]
    [InlineData("OrderService", "Product", 3)]
    public void BoundedEditDistance_DistanceAboveBound_StopsAtTheBound(string a, string b, int expected) =>
        Assert.Equal(expected, SymbolMatcher.BoundedEditDistance(a, b, 2));
}
