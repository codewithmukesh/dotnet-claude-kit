using System.Globalization;

namespace CWM.RoslynNavigator.Bench;

/// <summary>
/// "verb --key value --key value" parsing. Repeatable keys (--corpus, --server) keep every
/// value. Deliberately tiny: the bench has three verbs and no need for a parser package.
/// </summary>
internal sealed class CommandLine
{
    private readonly Dictionary<string, List<string>> _values = new(StringComparer.Ordinal);

    private CommandLine(string verb) => Verb = verb;

    public string Verb { get; }

    public static CommandLine Parse(string[] args)
    {
        if (args.Length == 0) return new CommandLine("help");

        var parsed = new CommandLine(args[0]);
        for (var i = 1; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Unexpected argument '{args[i]}'.");

            var key = args[i][2..];
            var value = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : "true";
            if (!parsed._values.TryGetValue(key, out var list)) parsed._values[key] = list = [];
            list.Add(value);
        }

        return parsed;
    }

    public IReadOnlyList<string> All(string key) => _values.TryGetValue(key, out var list) ? list : [];

    public string? Optional(string key) => All(key).LastOrDefault();

    public string Required(string key) =>
        Optional(key) ?? throw new ArgumentException($"Missing --{key}.");

    public double Number(string key, double fallback) =>
        Optional(key) is { } text ? double.Parse(text, CultureInfo.InvariantCulture) : fallback;

    public bool Flag(string key) => Optional(key) is "true";
}
