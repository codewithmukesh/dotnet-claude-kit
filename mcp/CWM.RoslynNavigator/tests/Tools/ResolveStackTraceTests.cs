using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using CWM.RoslynNavigator.Tests.Fixtures;
using CWM.RoslynNavigator.Tools;

namespace CWM.RoslynNavigator.Tests.Tools;

public class ResolveStackTraceTests(TestSolutionFixture fixture) : IClassFixture<TestSolutionFixture>
{
    /// <summary>
    /// Mirrors what OrderService.CancelOrderAsync actually throws, framed the way the
    /// runtime prints it: async state machine on top, framework frames interleaved.
    /// </summary>
    private const string RealisticTrace = """
        System.InvalidOperationException: Order 42 not found
           at SampleApi.OrderService+<CancelOrderAsync>d__4.MoveNext()
           at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
           at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task)
           at SampleApi.OrderService.GetOrderAsync(Guid id, CancellationToken ct)
        """;

    [Fact]
    public async Task ResolveStackTrace_ParsesHeader()
    {
        var json = await ResolveStackTraceTool.ExecuteAsync(
            fixture.WorkspaceManager, RealisticTrace, ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<StackTraceResult>(json)!;

        Assert.Equal("System.InvalidOperationException", result.ExceptionType);
        Assert.Equal("Order 42 not found", result.Message);
    }

    [Fact]
    public async Task ResolveStackTrace_AsyncFrame_ResolvesToSourceMethod()
    {
        var json = await ResolveStackTraceTool.ExecuteAsync(
            fixture.WorkspaceManager, RealisticTrace, ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<StackTraceResult>(json)!;

        var frame = result.Frames[0];
        Assert.True(frame.InSolution, "the async state machine frame must map back to source");
        Assert.Equal("SampleApi.OrderService.CancelOrderAsync", frame.Method);
        Assert.Equal("SampleApi/OrderService.cs", frame.File);
        Assert.True(frame.DeclarationLine > 0);
    }

    [Fact]
    public async Task ResolveStackTrace_FrameworkFrames_AreMarkedOutOfSolution()
    {
        var json = await ResolveStackTraceTool.ExecuteAsync(
            fixture.WorkspaceManager, RealisticTrace, ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<StackTraceResult>(json)!;

        Assert.All(
            result.Frames.Where(f => f.Method.StartsWith("System.")),
            f => Assert.False(f.InSolution));
    }

    [Fact]
    public async Task ResolveStackTrace_PointsAtTopmostSolutionFrame()
    {
        var json = await ResolveStackTraceTool.ExecuteAsync(
            fixture.WorkspaceManager, RealisticTrace, ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<StackTraceResult>(json)!;

        Assert.Equal(0, result.FirstSolutionFrame);
        Assert.Equal(2, result.SolutionFrames);
    }

    [Fact]
    public async Task ResolveStackTrace_SolutionOnly_DropsFrameworkFrames()
    {
        var json = await ResolveStackTraceTool.ExecuteAsync(
            fixture.WorkspaceManager, RealisticTrace, solutionOnly: true,
            ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<StackTraceResult>(json)!;

        Assert.Equal(2, result.Frames.Count);
        Assert.All(result.Frames, f => Assert.True(f.InSolution));
    }

    [Fact]
    public async Task ResolveStackTrace_TraceLineNumber_TakesPrecedenceOverDeclaration()
    {
        const string trace = """
               at SampleApi.OrderService.CancelOrderAsync(Guid id) in /repo/SampleApi/OrderService.cs:line 33
            """;

        var json = await ResolveStackTraceTool.ExecuteAsync(
            fixture.WorkspaceManager, trace, ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<StackTraceResult>(json)!;

        var frame = result.Frames[0];
        Assert.Equal(33, frame.Line);
        Assert.NotEqual(frame.Line, frame.DeclarationLine);
        Assert.Contains("order.Cancel()", frame.Snippet);
    }

    [Fact]
    public async Task ResolveStackTrace_EmptyInput_ReturnsInvalidArgument()
    {
        var json = await ResolveStackTraceTool.ExecuteAsync(
            fixture.WorkspaceManager, "   ", ct: TestContext.Current.CancellationToken);
        var error = JsonSerializer.Deserialize<ErrorResponse>(json)!;

        Assert.Equal(ErrorCodes.InvalidArgument, error.Error);
    }

    [Fact]
    public async Task ResolveStackTrace_NoFramesInInput_ReturnsInvalidArgument()
    {
        var json = await ResolveStackTraceTool.ExecuteAsync(
            fixture.WorkspaceManager, "something went wrong", ct: TestContext.Current.CancellationToken);
        var error = JsonSerializer.Deserialize<ErrorResponse>(json)!;

        Assert.Equal(ErrorCodes.InvalidArgument, error.Error);
    }
}
