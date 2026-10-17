using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

// Physical process observations shared by logging fixture tests.
internal static class FixtureProcessChecks
{
    internal static string Family => OperatingSystem.IsWindows() ? "windows" : "linux";
    internal static string Lane => OperatingSystem.IsWindows() ? "windows-x64" : "linux-ubuntu2404-x64";

    internal static string Find(string name)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string path = Path.Combine(directory.FullName, "conformance", "logging", name);
            if (File.Exists(path)) { return path; }
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Cannot find logging fixture inputs: " + name);
    }

    internal static JsonArray Cases(string name, int count, bool declaresCount = true)
    {
        JsonObject document = JsonNode.Parse(File.ReadAllText(Find(name)))!.AsObject();
        JsonArray rows = document["cases"]!.AsArray();
        Assert.AreEqual(1, document["schemaVersion"]!.GetValue<int>());
        if (declaresCount || document.ContainsKey("caseCount")) { Assert.AreEqual(count, document["caseCount"]!.GetValue<int>()); }
        Assert.AreEqual(count, rows.Count);
        Assert.AreEqual(count, rows.Select(row => (string)row!["id"]!).Distinct(StringComparer.Ordinal).Count());
        return rows;
    }

    internal static async Task<(JsonObject Report, int ProcessId)> RunAsync(string[] arguments, string directory, TestContext context, string? inputs = null, IReadOnlyDictionary<string, string?>? environment = null, TimeSpan? timeout = null, bool physicalStreams = false, int minimumStdoutBytes = 0, int minimumStderrBytes = 0, bool flatReport = false)
    {
        string host = Path.Combine(AppContext.BaseDirectory, "discovery-fixture", "CIS.Core.DiscoveryFixture.dll");
        string core = Path.Combine(Path.GetDirectoryName(host)!, "CIS.Core.dll");
        string hostHash = Hash(host), coreHash = Hash(core), inputHash = inputs is null ? "" : Hash(inputs);
        string parentDirectory = Directory.GetCurrentDirectory();
        Dictionary<string, string?> parent = ParentEnvironment();
        string[] command = ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", host, .. arguments];
        ProcessResult child = await ProcessRunner.RunAsync("dotnet", command, directory, timeout ?? TimeSpan.FromSeconds(15), environment, new UTF8Encoding(false, true));
        Assert.AreEqual(parentDirectory, Directory.GetCurrentDirectory());
        Dictionary<string, string?> after = ParentEnvironment();
        CollectionAssert.AreEquivalent(parent.Keys.ToArray(), after.Keys.ToArray());
        Assert.IsTrue(parent.All(pair => after[pair.Key] == pair.Value), "Parent environment changed.");
        Assert.AreEqual(0, child.ExitCode, child.StandardOutput + child.StandardError);
        Assert.IsTrue(child.StandardOutputBytes.Length >= minimumStdoutBytes, "Stdout pipe pressure was not exercised.");
        Assert.IsTrue(child.StandardErrorBytes.Length >= minimumStderrBytes, "Stderr pipe pressure was not exercised.");
        JsonObject report;
        if (physicalStreams)
        {
            report = ReadPhysicalReport(child);
        }
        else
        {
            Assert.AreEqual(0, child.StandardErrorBytes.Length);
            report = JsonNode.Parse(child.StandardOutput)!.AsObject();
        }
        JsonNode observed = flatReport ? report : report["observed"]!;
        Assert.AreEqual(Family, (string?)observed[flatReport ? "os" : "operatingSystem"]);
        Assert.AreEqual(".NET 10.0.12", (string?)observed[flatReport ? "runtime" : "runtimeDescription"]);
        Assert.AreEqual(child.ProcessId, observed[flatReport ? "pid" : "processId"]!.GetValue<int>());
        Assert.AreEqual(hostHash, (string?)observed["fixtureSha256"]);
        Assert.AreEqual(coreHash, (string?)observed["coreSha256"]);
        Assert.AreEqual(hostHash, Hash(host));
        Assert.AreEqual(coreHash, Hash(core));
        if (inputs is not null)
        {
            Assert.AreEqual(inputHash, (string?)observed["inputsSha256"]);
            Assert.AreEqual(inputHash, Hash(inputs));
        }
        context.WriteLine("Physical logging fixture command: " + string.Join(" ", command));
        context.WriteLine("Physical child PID: " + child.ProcessId);
        context.WriteLine(child.StandardOutput);
        if (physicalStreams) { context.WriteLine(child.StandardError); }
        return (report, child.ProcessId);
    }

    internal static JsonObject ReadPhysicalReport(ProcessResult child)
    {
        ReadOnlySpan<byte> stdout = child.StandardOutputBytes.Span;
        int end = stdout.Length;
        while (end > 0 && stdout[end - 1] is 13 or 10) { end--; }
        int start = stdout[..end].LastIndexOf((byte)10) + 1;
        JsonObject report = JsonNode.Parse(stdout[start..])!.AsObject();
        report["stdout"] = Convert.ToBase64String(stdout[..start]);
        report["stderr"] = Convert.ToBase64String(child.StandardErrorBytes.Span);
        return report;
    }

    internal static void ExplicitNull(JsonObject actual, string key)
    {
        Assert.IsTrue(actual.ContainsKey(key), "Missing null member: " + key);
        Assert.IsNull(actual[key], "Expected null: " + key);
    }

    internal static void Subset(JsonObject expected, JsonNode actual)
    {
        foreach ((string key, JsonNode? value) in expected)
        {
            Assert.IsTrue(actual.AsObject().ContainsKey(key), "Missing value: " + key);
            if (value is JsonObject child) { Subset(child, actual[key]!); }
            else { Assert.IsTrue(JsonNode.DeepEquals(value, actual[key]), "Value differs: " + key); }
        }
    }

    internal static string[] Issues(JsonNode issues) => issues.AsArray().Select(issue =>
        (string)issue!["code"]! + ":" + (string)issue["key"]!).ToArray();
    private static Dictionary<string, string?> ParentEnvironment() => Environment.GetEnvironmentVariables().Keys.Cast<string>().ToDictionary(key => key, Environment.GetEnvironmentVariable, StringComparer.Ordinal);
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    internal static void ExcludeStrings(JsonNode? node, params string[] forbidden)
    {
        if (node is JsonObject map)
        {
            foreach ((string key, JsonNode? value) in map)
            {
                Check(key);
                ExcludeStrings(value, forbidden);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (JsonNode? value in array) { ExcludeStrings(value, forbidden); }
        }
        else if (node is JsonValue value && value.TryGetValue(out string? text)) { Check(text); }
        void Check(string text) => Assert.IsFalse(forbidden.Any(token => text.Contains(token, StringComparison.Ordinal)), "Private sentinel appeared in the decoded report.");
    }
}
