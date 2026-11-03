using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using CWM.RoslynNavigator.Bench.Corpus;

namespace CWM.RoslynNavigator.Bench.Measurement;

/// <summary>What a search returned and what it cost.</summary>
/// <param name="TotalMatches">Matching lines before the head limit.</param>
/// <param name="OutputLines">Lines an agent would actually receive after the head limit.</param>
/// <param name="OutputBytes">UTF-8 size of those lines in <c>path:line:text</c> form.</param>
internal sealed record GrepResult(
    string Pattern,
    string Glob,
    int TotalMatches,
    int MatchedFiles,
    int OutputLines,
    long OutputBytes,
    double ElapsedMs);

/// <summary>
/// An in-process stand-in for <c>rg -n --no-heading</c>: parallel regex over every matching
/// file in the solution's project directories, skipping bin/obj/.git/node_modules.
/// In-process so the benchmark runs anywhere without ripgrep installed. Native ripgrep may
/// well be faster, so treat the measured search time as an upper bound for the manual route.
///
/// <para>Output is cut at a head limit (default 250 lines — Claude Code's Grep default),
/// which keeps the manual-search token count conservative: an agent never sees more.</para>
/// </summary>
internal static class GrepScanner
{
    private static readonly string[] SkippedDirectories = ["bin", "obj", ".git", "node_modules", ".vs"];

    public static GrepResult Search(SolutionScope scope, string pattern, string glob, int headLimit)
    {
        var started = Stopwatch.GetTimestamp();
        var regex = new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);
        var extension = glob.TrimStart('*');

        var root = scope.SolutionDirectory;
        var files = scope.Files(extension).ToList();
        var hits = new ConcurrentBag<(string File, int Line, string Text)>();

        Parallel.ForEach(files, file =>
        {
            var lineNumber = 0;
            foreach (var line in File.ReadLines(file))
            {
                lineNumber++;
                if (regex.IsMatch(line)) hits.Add((file, lineNumber, line));
            }
        });

        var ordered = hits
            .OrderBy(h => h.File, StringComparer.Ordinal)
            .ThenBy(h => h.Line)
            .ToList();

        long bytes = 0;
        var emitted = 0;
        foreach (var hit in ordered)
        {
            if (emitted == headLimit) break;
            var relative = Path.GetRelativePath(root, hit.File).Replace('\\', '/');
            bytes += Encoding.UTF8.GetByteCount($"{relative}:{hit.Line}:{hit.Text}\n");
            emitted++;
        }

        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var matchedFiles = ordered.Select(h => h.File).Distinct(StringComparer.Ordinal).Count();
        return new GrepResult(pattern, glob, ordered.Count, matchedFiles, emitted, bytes, elapsed);
    }

    public static IEnumerable<string> EnumerateFiles(string root, string extension)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();

            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateDirectories(directory);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var child in children)
            {
                var name = Path.GetFileName(child);
                if (!Array.Exists(SkippedDirectories, s => s.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    pending.Push(child);
            }

            // Filter explicitly: Win32 wildcard matching lets "*.cs" also match ".csproj".
            foreach (var file in Directory.EnumerateFiles(directory, "*" + extension))
            {
                if (file.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) yield return file;
            }
        }
    }
}
