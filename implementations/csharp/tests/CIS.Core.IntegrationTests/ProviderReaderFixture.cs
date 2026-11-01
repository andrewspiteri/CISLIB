using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

// Only reader controls share these captures. Functional process tests still launch their own children.
internal static class ProviderReaderFixture
{
    private sealed record Scenario(string Family, string Mode, string Culture, string UICulture, string EnvironmentSha256);
    private sealed record ImmutableCapture(string ReportJson, string ConsoleText, int ProcessId, string Evidence);

    private static readonly ConcurrentDictionary<Scenario, Lazy<Task<ImmutableCapture>>> Captures = new();

    internal static async Task<ProviderReportCapture> GetAsync(string family, string mode, TestContext context)
    {
        Assert.IsTrue(ProviderReaderVerifier.Modes(family).Contains(mode, StringComparer.Ordinal), family + "/" + mode);
        var scenario = new Scenario(family, mode, CultureInfo.CurrentCulture.Name, CultureInfo.CurrentUICulture.Name, EnvironmentHash());
        ImmutableCapture capture = await Captures.GetOrAdd(scenario, static key =>
            new Lazy<Task<ImmutableCapture>>(() => CaptureAsync(key), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        context.WriteLine("Shared physical reader fixture: " + family + "/" + mode +
            "; culture=" + scenario.Culture + "; uiCulture=" + scenario.UICulture +
            "; environment SHA-256=" + scenario.EnvironmentSha256);
        context.WriteLine(capture.Evidence);
        // A mutable JsonNode never enters the shared value; every caller owns this independent report.
        return new(JsonNode.Parse(capture.ReportJson)!.AsObject(), capture.ConsoleText, capture.ProcessId, capture.Evidence);
    }

    private static async Task<ImmutableCapture> CaptureAsync(Scenario scenario)
    {
        Assert.AreEqual(scenario.EnvironmentSha256, EnvironmentHash(), "Provider fixture configuration changed before capture.");
        ProviderReportCapture captured = await ProviderReportProcess.RunAsync(scenario.Family, scenario.Mode);
        ProviderReaderVerifier.Validate(scenario.Family, scenario.Mode, captured.Report, captured.ConsoleText, captured.ProcessId);
        Assert.AreEqual(scenario.EnvironmentSha256, EnvironmentHash(), "Provider fixture configuration changed during capture.");
        return new(captured.Report.ToJsonString(), captured.ConsoleText, captured.ProcessId, captured.Evidence);
    }

    private static string EnvironmentHash()
    {
        // RunAsync clears CORE_* and otherwise inherits this configuration, including native locale settings.
        var inherited = Environment.GetEnvironmentVariables().Keys.Cast<string>()
            .Where(key => !key.StartsWith("CORE_", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(key => new KeyValuePair<string, string?>(key, Environment.GetEnvironmentVariable(key)));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(inherited))));
    }
}
