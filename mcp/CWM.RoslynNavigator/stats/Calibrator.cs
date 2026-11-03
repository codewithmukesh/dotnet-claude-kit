using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace CWM.RoslynNavigator.Stats;

internal sealed record CalibrationResult(
    int Files,
    long TotalBytes,
    long TotalTokens,
    double BytesPerToken,
    double RSquared);

/// <summary>
/// Derives the bytes-per-token divisor from measured token counts.
///
/// <para>Everything else in this system is arithmetic over real file sizes; the divisor is
/// the one input that is currently a guess, and it scales every savings figure linearly. A
/// claim built on a guessed divisor is not one we can defend, so this exists to replace it
/// with a measurement anyone can reproduce.</para>
///
/// <para>Uses the Anthropic <c>count_tokens</c> endpoint, which returns exact counts and is
/// free. Runs manually and only from this CLI — the MCP server never calls it and stays
/// fully offline.</para>
/// </summary>
internal sealed class Calibrator(HttpClient http, string apiKey)
{
    private const string Endpoint = "https://api.anthropic.com/v1/messages/count_tokens";
    private const string ApiVersion = "2023-06-01";

    /// <summary>Cheapest model is fine — the tokenizer is what is being measured.</summary>
    private const string Model = "claude-haiku-4-5-20251001";

    /// <summary>
    /// Very small files are dominated by fixed per-message overhead and would skew the fit;
    /// very large ones waste the sample budget.
    /// </summary>
    private const int MinBytes = 500;
    private const int MaxBytes = 60_000;

    public async Task<CalibrationResult> RunAsync(
        IReadOnlyList<string> files,
        Action<string>? progress = null,
        CancellationToken ct = default)
    {
        var points = new List<(double Bytes, double Tokens)>();
        long totalBytes = 0, totalTokens = 0;

        for (var i = 0; i < files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var path = files[i];
            string text;
            try
            {
                text = await File.ReadAllTextAsync(path, ct);
            }
            catch (IOException)
            {
                continue;
            }

            var bytes = Encoding.UTF8.GetByteCount(text);
            if (bytes is < MinBytes or > MaxBytes) continue;

            var tokens = await CountTokensAsync(text, ct);
            if (tokens <= 0) continue;

            points.Add((bytes, tokens));
            totalBytes += bytes;
            totalTokens += tokens;

            if ((i + 1) % 10 == 0)
                progress?.Invoke($"  {points.Count} sampled ({i + 1}/{files.Count} examined)");
        }

        if (points.Count == 0)
            return new CalibrationResult(0, 0, 0, 0, 0);

        // The divisor is the ratio of the totals, not the mean of per-file ratios: a single
        // small file should not weigh as much as a large one.
        var bytesPerToken = (double)totalBytes / totalTokens;

        return new CalibrationResult(
            points.Count, totalBytes, totalTokens, bytesPerToken, RSquared(points, bytesPerToken));
    }

    /// <summary>
    /// How well a single divisor explains the sample. A low value means C# token density
    /// varies too much for one constant, which is itself a finding worth reporting.
    /// </summary>
    private static double RSquared(List<(double Bytes, double Tokens)> points, double bytesPerToken)
    {
        var meanTokens = points.Average(p => p.Tokens);
        var residual = points.Sum(p => Math.Pow(p.Tokens - p.Bytes / bytesPerToken, 2));
        var total = points.Sum(p => Math.Pow(p.Tokens - meanTokens, 2));

        return total == 0 ? 0 : 1 - residual / total;
    }

    private async Task<int> CountTokensAsync(string text, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new
        {
            model = Model,
            messages = new[] { new { role = "user", content = text } }
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", ApiVersion);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return 0;

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));

        return document.RootElement.TryGetProperty("input_tokens", out var tokens)
            && tokens.TryGetInt32(out var value)
                ? value
                : 0;
    }

    /// <summary>
    /// Samples C# files deterministically from a seed, so a calibration run can be repeated
    /// and checked by someone else.
    /// </summary>
    public static IReadOnlyList<string> SampleFiles(string root, int count, int seed = 20260807)
    {
        if (!Directory.Exists(root)) return [];

        var candidates = Directory
            .EnumerateFiles(root, "*.cs", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System
            })
            .Where(p => !IsBuildOutput(p))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var random = new Random(seed);
        return candidates.OrderBy(_ => random.Next()).Take(count).ToList();
    }

    private static bool IsBuildOutput(string path)
    {
        foreach (var segment in path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("obj", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
