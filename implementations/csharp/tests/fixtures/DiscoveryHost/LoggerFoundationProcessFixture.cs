using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Runtime;

namespace CIS.Tests.DiscoveryFixture;

internal static class LoggerFoundationProcessFixture
{
    // These descriptor matrices create their own runtime, outputs and clock per row.
    // Keep the real process/environment boundary once per family, without starting
    // another CLR for each combination of otherwise identical physical inputs.
    internal static int RunBatch(string command, string path)
    {
        byte[] inputBytes = File.ReadAllBytes(path);
        JsonArray rows = JsonNode.Parse(inputBytes)!["cases"]!.AsArray();
        var reports = new JsonArray();
        Activity? activity = Activity.Current;
        if (EventProcessing.IsCallbackActive) { throw new InvalidDataException("Logger matrices cannot start inside a processing callback."); }
        foreach (JsonNode? node in rows)
        {
            JsonObject row = node!.AsObject();
            JsonObject actual;
            switch (command)
            {
                case "logger-foundation": actual = LoggerFoundationFixture.Execute(row); LoggerFoundationExpectations.Validate(row, actual); break;
                case "logger-outcome": actual = LoggerOutcomeFixture.Execute(row); LoggerOutcomeExpectations.Validate(row, actual); break;
                case "logger-decision": actual = LoggerDecisionFixture.Execute(row); LoggerDecisionExpectations.Validate(row, actual); break;
                case "logger-capture": actual = LoggerCaptureFixture.Execute(row); LoggerCaptureExpectations.Validate(row, actual); break;
                case "presentation": actual = PresentationFixture.Execute(row); PresentationExpectations.Validate(row, actual); break;
                default: throw new ArgumentException("Unknown logger matrix.", nameof(command));
            }
            if (!ReferenceEquals(activity, Activity.Current)) { throw new InvalidDataException("A logger row retained ambient activity."); }
            if (EventProcessing.IsCallbackActive) { throw new InvalidDataException("A logger row retained processing callback state."); }
            if (Directory.EnumerateFileSystemEntries(Directory.GetCurrentDirectory()).Any()) { throw new InvalidDataException("A logger row created a physical output artifact."); }
            reports.Add(actual);
        }
        var report = new JsonObject
        {
            ["command"] = command,
            ["reports"] = reports,
            ["observed"] = new JsonObject
            {
                ["processId"] = System.Environment.ProcessId,
                ["runtimeDescription"] = RuntimeInformation.FrameworkDescription,
                ["operatingSystem"] = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : "unknown",
                ["fixtureSha256"] = Hash(typeof(LoggerFoundationProcessFixture).Assembly.Location),
                ["coreSha256"] = Hash(typeof(LogEventData).Assembly.Location),
                ["inputsSha256"] = Convert.ToHexStringLower(SHA256.HashData(inputBytes))
            }
        };
        Console.WriteLine(report.ToJsonString());
        return 0;
    }

    internal static int Run(string path, string id)
    {
        byte[] inputBytes = File.ReadAllBytes(path);
        JsonObject row = JsonNode.Parse(inputBytes)!["cases"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == id)!.AsObject();
        JsonObject actual = LoggerFoundationFixture.Execute(row); LoggerFoundationExpectations.Validate(row, actual);
        actual["observed"] = new JsonObject
        {
            ["processId"] = System.Environment.ProcessId,
            ["runtimeDescription"] = RuntimeInformation.FrameworkDescription,
            ["operatingSystem"] = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : "unknown",
            ["fixtureSha256"] = Hash(typeof(LoggerFoundationProcessFixture).Assembly.Location),
            ["coreSha256"] = Hash(typeof(LogEventData).Assembly.Location),
            ["inputsSha256"] = Convert.ToHexStringLower(SHA256.HashData(inputBytes))
        };
        Console.WriteLine(actual.ToJsonString()); return 0;
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
