using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

internal static class LifecycleProcessChecks
{
    internal static IEnumerable<object[]> Modes(string command, string[] modes)
    {
        JsonArray cases = LogValueProcessTests.Read(command + "-cases.json")["cases"]!.AsArray();
        Assert.AreEqual(modes.Length, cases.Count);
        CollectionAssert.AreEquivalent(modes, cases.Select(row => (string)row!["id"]!).ToArray());
        return modes.Select(mode => new object[] { mode });
    }

    internal static async Task<JsonObject> Run(string command, string mode, TestContext context)
    {
        using var root = new TemporaryDirectory();
        var environment = Environment.GetEnvironmentVariables().Keys.Cast<string>()
            .Where(key => key.StartsWith("CORE_", StringComparison.OrdinalIgnoreCase)).ToDictionary(key => key, _ => (string?)null, StringComparer.Ordinal);
        if (command == "stop-start" && mode == "env") { environment["CORE_LOG_SHUTDOWN_TIMEOUT_MS"] = "250"; }
        (JsonObject report, int pid) = await FixtureProcessChecks.RunAsync([command, mode, root.Path], root.Path, context,
            environment: environment, timeout: TimeSpan.FromSeconds(60), flatReport: true);
        Validate(command, mode, report, pid);
        if (command == "stop-drain" && mode == "normal-dual")
        {
            string path = Path.Combine(root.Path, "owned.jsonl");
            CollectionAssert.AreEqual(Convert.FromBase64String((string)report["fileBytes"]!), File.ReadAllBytes(path));
            using var lease = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        return report;
    }

    internal static void Validate(string command, string mode, JsonObject report, int pid)
    {
        switch (command)
        {
            case "stop-start": StopStartExpectations.Validate(mode, report, pid); break;
            case "stop-drain": StopDrainExpectations.Validate(mode, report, pid); break;
            case "stop-join": StopJoinExpectations.Validate(mode, report, pid); break;
            default: throw new ArgumentOutOfRangeException(nameof(command));
        }
    }
}
