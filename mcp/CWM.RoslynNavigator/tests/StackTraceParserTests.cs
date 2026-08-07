namespace CWM.RoslynNavigator.Tests;

public class StackTraceParserTests
{
    [Theory]
    // Plain method
    [InlineData("MyApp.Services.OrderService.CreateOrderAsync(String customerId, CancellationToken ct)",
                "MyApp.Services.OrderService.CreateOrderAsync")]
    // Async state machine — the rewrite that makes naive parsing fail
    [InlineData("MyApp.Services.OrderService+<CreateOrderAsync>d__5.MoveNext()",
                "MyApp.Services.OrderService.CreateOrderAsync")]
    // Lambda hoisted into the closure class
    [InlineData("MyApp.Services.OrderService.<>c.<Configure>b__3_0()",
                "MyApp.Services.OrderService.Configure")]
    // Lambda hoisted into a display class (captures locals)
    [InlineData("MyApp.Services.OrderService.<>c__DisplayClass7_0.<Configure>b__1()",
                "MyApp.Services.OrderService.Configure")]
    // Local function resolves to its enclosing method
    [InlineData("MyApp.Services.OrderService.<Process>g__Validate|9_0(Int32 id)",
                "MyApp.Services.OrderService.Process")]
    // Generic method arity
    [InlineData("System.Linq.Enumerable.Select[TSource,TResult](IEnumerable`1 source)",
                "System.Linq.Enumerable.Select")]
    // Nested type separator
    [InlineData("MyApp.Outer+Inner.Run()", "MyApp.Outer.Inner.Run")]
    // Constructors anchor on the type
    [InlineData("MyApp.Services.OrderService..ctor(IOrderRepository repo)",
                "MyApp.Services.OrderService")]
    [InlineData("MyApp.Services.OrderService..cctor()", "MyApp.Services.OrderService")]
    public void NormalizeMethod_UndoesCompilerRewrites(string raw, string expected)
    {
        Assert.Equal(expected, StackTraceParser.NormalizeMethod(raw));
    }

    [Fact]
    public void ParseFrames_ExtractsFileAndLineWhenPdbPresent()
    {
        const string trace = """
            System.InvalidOperationException: Order 42 not found
               at MyApp.Services.OrderService.CancelOrderAsync(Guid id) in C:\src\MyApp\OrderService.cs:line 31
               at MyApp.Api.OrderEndpoints.Cancel(Guid id)
            """;

        var frames = StackTraceParser.ParseFrames(trace);

        Assert.Equal(2, frames.Count);
        Assert.Equal("MyApp.Services.OrderService.CancelOrderAsync", frames[0].QualifiedName);
        Assert.Equal(@"C:\src\MyApp\OrderService.cs", frames[0].File);
        Assert.Equal(31, frames[0].Line);

        // No PDB info on the second frame
        Assert.Null(frames[1].File);
        Assert.Null(frames[1].Line);
    }

    [Fact]
    public void ParseFrames_IgnoresRethrowSeparators()
    {
        const string trace = """
               at MyApp.A.One()
            --- End of stack trace from previous location ---
               at MyApp.B.Two()
            """;

        var frames = StackTraceParser.ParseFrames(trace);

        Assert.Equal(["MyApp.A.One", "MyApp.B.Two"], frames.Select(f => f.QualifiedName));
    }

    [Fact]
    public void ParseHeader_ReadsExceptionTypeAndMessage()
    {
        const string trace = """
            System.InvalidOperationException: Order 42 not found
               at MyApp.Services.OrderService.CancelOrderAsync(Guid id)
            """;

        var header = StackTraceParser.ParseHeader(trace);

        Assert.NotNull(header);
        Assert.Equal("System.InvalidOperationException", header.ExceptionType);
        Assert.Equal("Order 42 not found", header.Message);
    }

    [Fact]
    public void ParseHeader_TraceWithoutHeader_ReturnsNull()
    {
        const string trace = "   at MyApp.Services.OrderService.CancelOrderAsync(Guid id)";

        Assert.Null(StackTraceParser.ParseHeader(trace));
    }

    [Fact]
    public void ParseFrames_NonStackTraceInput_ReturnsNothing()
    {
        Assert.Empty(StackTraceParser.ParseFrames("this is not a stack trace"));
    }
}
