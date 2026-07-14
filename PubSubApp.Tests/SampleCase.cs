using System.Text.Json;
using System.Text.Json.Serialization;

namespace PubSubApp.Tests;

/// A sample input paired with its committed baseline: samples/<dir>/<name>.json + output_<name>.json.
public sealed record SampleCase(string Name, string InputPath, string BaselinePath)
{
    /// Must stay identical to the options Program.cs serializes test-mode output with,
    /// otherwise baselines and actual output differ on formatting alone.
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string SamplesRoot { get; } = FindSamplesRoot();

    public static IReadOnlyList<SampleCase> Discover()
    {
        var cases = new List<SampleCase>();

        foreach (string input in Directory.EnumerateFiles(SamplesRoot, "*.json", SearchOption.AllDirectories))
        {
            string fileName = Path.GetFileName(input);
            if (fileName.StartsWith("output_", StringComparison.OrdinalIgnoreCase))
                continue;

            string baseline = Path.Combine(Path.GetDirectoryName(input)!, "output_" + fileName);
            if (!File.Exists(baseline))
                continue;

            string name = Path.GetRelativePath(SamplesRoot, input).Replace('\\', '/');
            cases.Add(new SampleCase(name, input, baseline));
        }

        return cases.OrderBy(c => c.Name, StringComparer.Ordinal).ToList();
    }

    private static string FindSamplesRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "RonaORAEPubSub.sln")))
                return Path.Combine(dir.FullName, "samples");
        }

        throw new InvalidOperationException(
            $"Could not locate the repo root (RonaORAEPubSub.sln) walking up from {AppContext.BaseDirectory}.");
    }
}
