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
        Assert.IsTrue(Encoding.UTF8.GetByteCount(child.StandardOutput) >= minimumStdoutBytes, "Stdout pipe pressure was not exercised.");
        Assert.IsTrue(Encoding.UTF8.GetByteCount(child.StandardError) >= minimumStderrBytes, "Stderr pipe pressure was not exercised.");
        JsonObject report;
        if (physicalStreams)
        {
            string text = child.StandardOutput.TrimEnd('\r', '\n');
            int start = text.LastIndexOf('\n') + 1;
            report = JsonNode.Parse(text[start..])!.AsObject();
            report["stdout"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(child.StandardOutput[..start]));
            report["stderr"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(child.StandardError));
        }
        else
        {
            Assert.AreEqual("", child.StandardError);
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
}
