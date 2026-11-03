using System.Text;
using System.Text.Json;

namespace CWM.RoslynNavigator.Telemetry;

/// <summary>What a tool response tells us about the call that produced it.</summary>
internal readonly record struct ResponseFacts(
    IReadOnlyList<string> Files,
    int Results,
    CallOutcome Outcome,
    string? ErrorCode);

internal enum CallOutcome
{
    /// <summary>Answered from a ready workspace.</summary>
    Ok,

    /// <summary>Structured failure — see <see cref="ResponseFacts.ErrorCode"/>.</summary>
    Error,

    /// <summary>Workspace was still loading. Excluded from savings, counted separately.</summary>
    NotReady
}

/// <summary>
/// Reads the facts telemetry needs straight out of a tool's JSON response.
///
/// <para>This is what makes measurement free of the tools themselves: every navigation
/// response already names the files its answer came from, which is exactly the spec's
/// baseline definition. No tool method changes, and a tool added later is measured the day
/// it ships rather than the day someone remembers to instrument it.</para>
///
/// <para>Uses <see cref="Utf8JsonReader"/> — a single forward pass over the UTF-8 bytes with
/// no document tree allocated. This runs on every call, so it must not be the reason a call
/// got slower.</para>
/// </summary>
internal static class ResponseInspector
{
    /// <summary>
    /// Property names that hold a source path. <c>TestFile</c> is included because
    /// get_test_coverage_map's answer genuinely derives from the test file too.
    /// </summary>
    private static readonly string[] PathProperties = ["File", "TestFile"];

    /// <summary>Placeholder written when a symbol has no resolvable location.</summary>
    private const string UnknownPath = "unknown";

    public static ResponseFacts Inspect(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new ResponseFacts([], 0, CallOutcome.Ok, null);

        var files = new List<string>();
        var results = 0;
        var outcome = CallOutcome.Ok;
        string? errorCode = null;

        try
        {
            var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
            string? property = null;

            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.PropertyName:
                        property = reader.GetString();
                        continue;

                    case JsonTokenType.String when property is not null:
                        // Root-level markers distinguish the three outcomes. StatusResponse
                        // carries State; ErrorResponse carries Error.
                        if (reader.CurrentDepth == 1)
                        {
                            if (property == "Error")
                            {
                                outcome = CallOutcome.Error;
                                errorCode = reader.GetString();
                            }
                            else if (property == "State")
                            {
                                outcome = CallOutcome.NotReady;
                            }
                        }

                        if (Array.IndexOf(PathProperties, property) >= 0)
                        {
                            var value = reader.GetString();
                            if (!string.IsNullOrEmpty(value)
                                && !value.Equals(UnknownPath, StringComparison.Ordinal))
                            {
                                files.Add(value);
                            }
                        }
                        break;

                    case JsonTokenType.Number when property == "Count" && reader.CurrentDepth == 1:
                        if (reader.TryGetInt32(out var count)) results = count;
                        break;
                }

                property = null;
            }
        }
        catch (JsonException)
        {
            // A response that will not parse tells us nothing, but it must not break the
            // call that produced it. Record what we have.
            return new ResponseFacts(files, results, outcome, errorCode);
        }

        // Tools without a Count field (get_symbol_source, get_file_outline) still produced
        // an answer; treat the paths they named as the result count.
        if (results == 0 && files.Count > 0)
            results = files.Count;

        return new ResponseFacts(files, results, outcome, errorCode);
    }
}
