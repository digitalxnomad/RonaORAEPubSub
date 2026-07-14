using System.Diagnostics;
using FluentAssertions;
using Xunit;

namespace PubSubApp.Tests;

/// Exercises the app as a process. The exit-code contract lives in Program.Main, which loads
/// config and initialises logging into shared state, so there is no seam to call it in-process.
/// Launches via `dotnet PubSubApp.dll` rather than PubSubApp.exe so these run on the Linux CI
/// runner as well as on Windows.
public class ExitCodeTests
{
    private const int Success = 0;
    private const int ValidationRejected = 1;
    private const int BadInvocation = 2;

    [Fact]
    public void ValidSample_ExitsZero()
    {
        SampleCase sample = SampleCase.Discover().Single(c => c.Name == "test_retailevent.json");

        Run("--test", sample.InputPath).Should().Be(Success);
    }

    [Fact]
    public void NonOraePayload_ExitsValidationRejected()
    {
        // tactill orders are a different format entirely: no schemaVersion/messageType.
        string tactill = Path.Combine(SampleCase.SamplesRoot,
            "Mother Baby SKU & UOM", "Mother Baby SKU & UOM", "tactill order.json");

        Run("--test", tactill).Should().Be(ValidationRejected,
            "a payload rejected by ORAE validation must be distinguishable from success");
    }

    [Fact]
    public void MissingPathArgument_ExitsBadInvocation()
    {
        Run("--test").Should().Be(BadInvocation);
    }

    /// appsettings.json resolves from the binary's directory, not the caller's, so the app
    /// must run from any cwd. It previously died with an unhandled FileNotFoundException.
    [Fact]
    public void RunFromUnrelatedWorkingDirectory_ExitsZero()
    {
        SampleCase sample = SampleCase.Discover().Single(c => c.Name == "test_retailevent.json");

        RunIn(Path.GetTempPath(), "--test", sample.InputPath).Should().Be(Success);
    }

    [Fact]
    public void NonexistentInputFile_ExitsBadInvocation()
    {
        Run("--test", Path.Combine(SampleCase.SamplesRoot, "no_such_file.json"))
            .Should().Be(BadInvocation);
    }

    private static int Run(params string[] args) => RunIn(Path.GetDirectoryName(AppDll())!, args);

    private static int RunIn(string workingDirectory, params string[] args)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add(AppDll());
        foreach (string arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)!;
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit(milliseconds: 60_000).Should().BeTrue("PubSubApp --test should not hang");

        return process.ExitCode;
    }

    /// The app under test builds alongside this one; mirror the test project's own bin layout.
    private static string AppDll()
    {
        var testBin = new DirectoryInfo(AppContext.BaseDirectory); // .../PubSubApp.Tests/bin/<cfg>/<tfm>
        string tfm = testBin.Name;
        string configuration = testBin.Parent!.Name;
        string repoRoot = testBin.Parent!.Parent!.Parent!.Parent!.FullName;

        string dll = Path.Combine(repoRoot, "PubSubApp", "bin", configuration, tfm, "PubSubApp.dll");
        File.Exists(dll).Should().BeTrue($"expected PubSubApp build output at {dll}");
        return dll;
    }
}
