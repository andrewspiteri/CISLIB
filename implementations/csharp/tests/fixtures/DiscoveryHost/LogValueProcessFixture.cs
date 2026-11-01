using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Runtime;

namespace CIS.Tests.DiscoveryFixture;

internal static class LogValueProcessFixture
{
    internal static int RunBatch(string command, string inputPath)
    {
        int count = (command, Path.GetFileName(inputPath)) switch
        {
            ("log-value", "event-value-cases.json") => 47,
            ("log-value", "value-normalization-cases.json") => 12,
            ("log-collection", "value-collection-cases.json") => 22,
            ("log-qualification", "value-qualification-cases.json") => 71,
            _ => throw new ArgumentException("Unknown value matrix.", nameof(command))
        };
        byte[] inputBytes = File.ReadAllBytes(inputPath);
        JsonObject document = JsonNode.Parse(inputBytes)!.AsObject();
        JsonArray rows = document["cases"]!.AsArray();
        if (document["schemaVersion"]!.GetValue<int>() != 1 || document["caseCount"]!.GetValue<int>() != count || rows.Count != count ||
            rows.Select(row => (string)row!["id"]!).Distinct(StringComparer.Ordinal).Count() != count)
        {
            throw new InvalidDataException("The original complete value matrix is required.");
        }
        var reports = new JsonArray();
        Activity? activity = Activity.Current;
        if (EventProcessing.IsCallbackActive) { throw new InvalidDataException("Value matrices cannot start inside a processing callback."); }
        foreach (JsonNode? node in rows)
        {
            JsonObject row = node!.DeepClone().AsObject();
            JsonObject actual;
            switch (command)
            {
                case "log-qualification":
                    actual = LogValueQualificationFixtureAdapter.Execute(row);
                    break;
                case "log-collection":
                    actual = LogValueCollectionFixtureAdapter.Execute(row);
                    break;
                default:
                    actual = LogValueFixtureAdapter.Execute(row);
                    LogValueFixtureAdapter.Validate(row, actual);
                    break;
            }
            if (!ReferenceEquals(activity, Activity.Current)) { throw new InvalidDataException("A value row retained ambient activity."); }
            if (EventProcessing.IsCallbackActive) { throw new InvalidDataException("A value row retained processing callback state."); }
            if (Directory.EnumerateFileSystemEntries(Directory.GetCurrentDirectory()).Any()) { throw new InvalidDataException("A value row created a physical output artifact."); }
            reports.Add(actual);
        }
        var report = new JsonObject
        {
            ["command"] = command,
            ["catalog"] = Path.GetFileName(inputPath),
            ["reports"] = reports,
            ["observed"] = new JsonObject
            {
                ["processId"] = System.Environment.ProcessId,
                ["lifecycleApiPresent"] = (typeof(EnvironmentOptions).GetMethod("Initialize") is not null),
                ["runtimeDescription"] = RuntimeInformation.FrameworkDescription,
                ["operatingSystem"] = OperatingSystem.IsWindows() ? "windows" : "linux",
                ["fixtureSha256"] = Hash(typeof(LogValueProcessFixture).Assembly.Location),
                ["coreSha256"] = Hash(typeof(LogValue).Assembly.Location),
                ["inputsSha256"] = Convert.ToHexStringLower(SHA256.HashData(inputBytes))
            }
        };
        Console.WriteLine(report.ToJsonString());
        return 0;
    }

    public static int Run(string inputPath, string id, bool collections = false, bool qualification = false)
    {
        byte[] inputBytes = File.ReadAllBytes(inputPath);
        JsonObject row = ConfigurationFixtureAdapter.Case(JsonNode.Parse(inputBytes)!.AsObject(), id);
        JsonObject result;
        if (qualification)
        {
            result = LogValueQualificationFixtureAdapter.Execute(row);
        }
        else if (collections)
        {
            result = LogValueCollectionFixtureAdapter.Execute(row);
        }
        else
        {
            result = LogValueFixtureAdapter.Execute(row);
            LogValueFixtureAdapter.Validate(row, result);
        }
        result["observed"] = new JsonObject
        {
            ["processId"] = System.Environment.ProcessId,
            ["lifecycleApiPresent"] = (typeof(EnvironmentOptions).GetMethod("Initialize") is not null),
            ["runtimeDescription"] = RuntimeInformation.FrameworkDescription,
            ["operatingSystem"] = OperatingSystem.IsWindows() ? "windows" : "linux",
            ["fixtureSha256"] = Hash(typeof(LogValueProcessFixture).Assembly.Location),
            ["coreSha256"] = Hash(typeof(LogValue).Assembly.Location),
            ["inputsSha256"] = Convert.ToHexStringLower(SHA256.HashData(inputBytes))
        };
        Console.WriteLine(result.ToJsonString());
        return 0;
    }

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
