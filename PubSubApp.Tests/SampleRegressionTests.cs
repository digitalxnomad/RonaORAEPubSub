using System.Text.Json;
using FluentAssertions;
using PubSubApp.Models;
using PubSubApp.Validation;
using Xunit;

namespace PubSubApp.Tests;

public class SampleRegressionTests
{
    /// Set to 1 to rewrite every baseline from current mapper output instead of asserting.
    /// Review the resulting diff before committing — this makes any behavior change look intentional.
    private static bool UpdateBaselines =>
        Environment.GetEnvironmentVariable("PUBSUB_UPDATE_BASELINES") == "1";

    public static TheoryData<string> Cases()
    {
        var data = new TheoryData<string>();
        foreach (SampleCase sample in SampleCase.Discover())
            data.Add(sample.Name);
        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void MapRetailEventToRecordSet_Sample_MatchesBaseline(string caseName)
    {
        SampleCase sample = Load(caseName);
        string actual = JsonSerializer.Serialize(Map(sample), SampleCase.SerializerOptions);

        if (UpdateBaselines)
        {
            File.WriteAllText(sample.BaselinePath, actual);
            return;
        }

        string expected = File.ReadAllText(sample.BaselinePath);
        Normalize(actual).Should().Be(
            Normalize(expected),
            "mapper output for {0} should match {1}. If this change is intended, re-run with PUBSUB_UPDATE_BASELINES=1 and review the diff.",
            sample.Name,
            Path.GetFileName(sample.BaselinePath));
    }

    /// Mirrors the ORAE compliance gate Program.cs runs before mapping: a sample that fails
    /// here would be rejected in production even if its mapped output still matched the baseline.
    [Theory]
    [MemberData(nameof(Cases))]
    public void ValidateOraeCompliance_Sample_ReturnsNoErrors(string caseName)
    {
        SampleCase sample = Load(caseName);
        RetailEvent retailEvent = Parse(sample);

        OraeValidator.ValidateOraeCompliance(retailEvent).Should().BeEmpty();
    }

    /// Mirrors the output gate Program.cs runs after mapping (field lengths, required fields).
    [Theory]
    [MemberData(nameof(Cases))]
    public void ValidateRecordSetOutput_Sample_ReturnsNoErrors(string caseName)
    {
        SampleCase sample = Load(caseName);

        RecordSetValidator.ValidateRecordSetOutput(Map(sample)).Should().BeEmpty();
    }

    [Fact]
    public void Discover_FindsAtLeastOneCase()
    {
        SampleCase.Discover().Should().NotBeEmpty(
            "regression coverage silently drops to zero if sample discovery breaks");
    }

    private static SampleCase Load(string caseName) =>
        SampleCase.Discover().Single(c => c.Name == caseName);

    private static RetailEvent Parse(SampleCase sample) =>
        new RetailEventMapper().ReadRecordSetFromString(File.ReadAllText(sample.InputPath));

    private static RecordSet Map(SampleCase sample)
    {
        var mapper = new RetailEventMapper();
        RetailEvent retailEvent = mapper.ReadRecordSetFromString(File.ReadAllText(sample.InputPath));
        return mapper.MapRetailEventToRecordSet(retailEvent);
    }

    private static string Normalize(string json) =>
        json.Replace("\r\n", "\n").TrimEnd();
}
