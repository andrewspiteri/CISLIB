using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Runtime;

namespace CIS.Tests.DiscoveryFixture;

internal static class EventProjectionProcessFixture
{
    internal static int RunBatch(string command, string path)
    {
        int count = (command, Path.GetFileName(path)) switch
        {
            ("event-projection", "event-projection-cases.json") => 32,
            ("event-precedence", "event-precedence-cases.json") => 28,
            ("event-correlation", "event-correlation-cases.json") => 14,
            _ => throw new ArgumentException("Unknown event matrix.", nameof(command))
        };
        byte[] inputBytes = File.ReadAllBytes(path);
        JsonObject catalog = JsonNode.Parse(inputBytes)!.AsObject();
        JsonArray rows = catalog["cases"]!.AsArray();
        if (catalog["schemaVersion"]!.GetValue<int>() != 1 || catalog["caseCount"]!.GetValue<int>() != count || rows.Count != count ||
            rows.Select(row => (string)row!["id"]!).Distinct(StringComparer.Ordinal).Count() != count)
        {
            throw new InvalidDataException("The original complete event matrix is required.");
        }
        var reports = new JsonArray();
        Activity? activity = Activity.Current;
        if (EventProcessing.IsCallbackActive) { throw new InvalidDataException("Event matrices cannot start inside a processing callback."); }
        foreach (JsonNode? node in rows)
        {
            JsonObject row = node!.DeepClone().AsObject();
            FixtureEnvironmentValues previousFacts = ConfigurationFixtureAdapter.Facts;
            var rowFacts = new FixtureEnvironmentValues();
            ConfigurationFixtureAdapter.Facts = rowFacts;
            JsonObject actual;
            try
            {
                switch (command)
                {
                    case "event-correlation":
                        actual = EventCorrelationFixture.Execute(row);
                        EventCorrelationExpectations.Validate(row, actual);
                        break;
                    case "event-precedence":
                        actual = EventPrecedenceFixture.Execute(row);
                        EventProjectionExpectations.Validate(row, catalog["nativeBindings"]!.AsObject(), actual);
                        break;
                    default:
                        actual = EventProjectionFixture.Execute(row);
                        EventProjectionExpectations.Validate(row, catalog["nativeBindings"]!.AsObject(), actual);
                        break;
                }
                if (!ReferenceEquals(rowFacts, ConfigurationFixtureAdapter.Facts)) { throw new InvalidDataException("An event row replaced its private metadata facts."); }
            }
            finally
            {
                // These mutable protocol inputs belong to one row; no prior row's facts reach the next capture.
                ConfigurationFixtureAdapter.Facts = previousFacts;
            }
            if (!ReferenceEquals(previousFacts, ConfigurationFixtureAdapter.Facts)) { throw new InvalidDataException("An event row retained its private metadata facts."); }
            if (!ReferenceEquals(activity, Activity.Current)) { throw new InvalidDataException("An event row retained ambient activity."); }
            if (EventProcessing.IsCallbackActive) { throw new InvalidDataException("An event row retained processing callback state."); }
            if (Directory.EnumerateFileSystemEntries(Directory.GetCurrentDirectory()).Any()) { throw new InvalidDataException("An event row created a physical output artifact."); }
            reports.Add(actual);
        }
        var report = new JsonObject
        {
            ["command"] = command,
            ["catalog"] = Path.GetFileName(path),
            ["reports"] = reports,
            ["observed"] = new JsonObject
            {
                ["lifecycleApiPresent"] = (typeof(EnvironmentOptions).GetMethod("Initialize") is not null),
                ["processId"] = System.Environment.ProcessId,
                ["runtimeDescription"] = RuntimeInformation.FrameworkDescription,
                ["operatingSystem"] = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux"
                    : OperatingSystem.IsMacOS() ? "macos" : "unknown",
                ["fixtureSha256"] = Hash(typeof(EventProjectionProcessFixture).Assembly.Location),
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
        JsonObject catalog = JsonNode.Parse(inputBytes)!.AsObject();
        JsonObject row = catalog["cases"]!.AsArray().Single(row => row!["id"]!.GetValue<string>() == id)!.AsObject();
        JsonObject actual = EventProjectionFixture.Execute(row);
        EventProjectionExpectations.Validate(row, catalog["nativeBindings"]!.AsObject(), actual);
        actual["observed"] = new JsonObject
        {
            ["lifecycleApiPresent"] = (typeof(EnvironmentOptions).GetMethod("Initialize") is not null),
            ["processId"] = System.Environment.ProcessId,
            ["runtimeDescription"] = RuntimeInformation.FrameworkDescription,
            ["operatingSystem"] = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux"
                : OperatingSystem.IsMacOS() ? "macos" : "unknown",
            ["fixtureSha256"] = Hash(typeof(EventProjectionProcessFixture).Assembly.Location),
            ["coreSha256"] = Hash(typeof(LogEventData).Assembly.Location),
            ["inputsSha256"] = Convert.ToHexStringLower(SHA256.HashData(inputBytes))
        };
        Console.WriteLine(actual.ToJsonString());
        return 0;
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
