namespace CWM.RoslynNavigator.Bench.Server;

/// <summary>
/// One tool call as the client saw it. <see cref="ElapsedMs"/> is the client-side round trip
/// over stdio — what an agent actually waits for, including JSON-RPC framing.
/// </summary>
/// <param name="Status">ok, error, not-ready, timeout, transport-error, or n/a (tool absent from this build).</param>
internal sealed record ToolCallResult(
    string Status,
    double ElapsedMs,
    string? Payload,
    int Bytes,
    string? ErrorCode,
    string? ErrorMessage)
{
    public bool Answered => Status is "ok";

    public static ToolCallResult NotAvailable { get; } = new("n/a", 0, null, 0, null, "Tool not exposed by this build.");
}
