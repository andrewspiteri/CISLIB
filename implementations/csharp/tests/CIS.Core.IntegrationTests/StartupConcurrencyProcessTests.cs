using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class StartupConcurrencyProcessTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow("valid-2")]
    [DataRow("valid-16")]
    [DataRow("valid-32")]
    [DataRow("mixed-16")]
    [DataRow("invalid-then-valid")]
    [DataRow("held-valid")]
    [DataRow("held-invalid")]
    public async Task ConcurrentValidInvalidAndHeldAttemptsPublishOneCompleteStableSnapshot(string scenario)
    {
        using var owned = new TemporaryDirectory();
        string before = Path.Combine(owned.Path, "before"); string after = Path.Combine(owned.Path, "after");
        Directory.CreateDirectory(before); Directory.CreateDirectory(after);
        string host = Host();
        ProcessResult child = await ProcessRunner.RunAsync("dotnet", ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", host, "startup-concurrency", scenario, after], before, TimeSpan.FromSeconds(20), Variables());
        JsonNode result = Read(child);
        JsonNode observed = result["observed"]!;
        JsonArray attempts = result["attempts"]!.AsArray();
        int count = scenario == "valid-2" ? 2 : scenario == "valid-32" ? 32 : scenario.StartsWith("held-", StringComparison.Ordinal) ? 2 : scenario == "invalid-then-valid" ? 17 : 16;
        Assert.AreEqual(count, attempts.Count);
        Assert.AreEqual(1, observed["successfulPublishers"]!.GetValue<int>());
        Assert.AreEqual(1, attempts.Count(attempt => attempt!["success"]!.GetValue<bool>()));
        int winner = result["publishedCandidate"]!.GetValue<int>();
        if (scenario == "mixed-16") { Assert.AreEqual(1, winner % 2); }
        if (scenario == "invalid-then-valid") { Assert.AreEqual(16, winner); Assert.IsFalse(observed["initializedBeforeRetry"]!.GetValue<bool>()); }
        if (scenario == "held-valid") { Assert.AreEqual(0, winner); }
        if (scenario == "held-invalid") { Assert.AreEqual(1, winner); }
        foreach (JsonNode? attempt in attempts)
        {
            int index = attempt!["index"]!.GetValue<int>();
            string? error = attempt["error"]?.GetValue<string>();
            if (index == winner) { Assert.IsTrue(attempt["success"]!.GetValue<bool>()); Assert.IsNull(error); }
            else if (scenario == "invalid-then-valid" || (scenario == "held-invalid" && index == 0)) { Assert.AreEqual("CoreConfigurationException", error); }
            else if (scenario == "mixed-16" && index % 2 == 0) { Assert.IsTrue(error is "CoreConfigurationException" or "InvalidOperationException"); }
            else { Assert.AreEqual("InvalidOperationException", error); }
            CollectionAssert.AreEqual(error == "CoreConfigurationException" ? new[] { "OutOfRange:LoggingSetup.QueueCapacity" } : Array.Empty<string>(), attempt["issues"]!.AsArray().Select(value => value!.GetValue<string>()).ToArray());
        }
        if (scenario.StartsWith("held-", StringComparison.Ordinal))
        {
            Assert.IsTrue(observed["blockedReader"]!.GetValue<bool>()); Assert.IsTrue(observed["blockedContender"]!.GetValue<bool>());
            Assert.IsTrue(observed["readerReturnedWinner"]!.GetValue<bool>() || (scenario == "held-invalid" && observed["readerError"]!.GetValue<string>() == "InvalidOperationException"));
        }
        else { Assert.AreEqual(256, observed["successfulReaderChecks"]!.GetValue<int>() + observed["uninitializedReaderChecks"]!.GetValue<int>()); }
        JsonNode snapshot = result["snapshot"]!;
        Assert.AreEqual(scenario == "held-valid" ? "Outer.App" : "Candidate." + winner, snapshot["ApplicationName"]!.GetValue<string>());
        Assert.AreEqual("build-" + winner, snapshot["BuildVersion"]!.GetValue<string>());
        Assert.AreEqual("release-" + winner, snapshot["ServiceVersion"]!.GetValue<string>());
        Assert.AreEqual("environment-" + winner, snapshot["EnvironmentName"]!.GetValue<string>());
        Assert.AreEqual(winner.ToString(System.Globalization.CultureInfo.InvariantCulture), snapshot["CommitId"]!.GetValue<string>());
        Assert.AreEqual("id-" + winner, snapshot["BuildId"]!.GetValue<string>());
        JsonNode logging = snapshot["Logging"]!;
        Assert.AreEqual(32 + winner, logging["QueueCapacity"]!.GetValue<int>());
        Assert.AreEqual(Path.Combine(before, "logs", "candidate-" + winner + ".jsonl"), logging["FilePath"]!.GetValue<string>());
        Assert.AreEqual(2 + winner % 4, logging["FileRetainedFiles"]!.GetValue<int>());
        Assert.IsTrue(observed["storedProjectionStable"]!.GetValue<bool>());
        Assert.AreEqual(before, observed["beforeDirectory"]!.GetValue<string>()); Assert.AreEqual(after, observed["afterDirectory"]!.GetValue<string>());
        Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(before).Count()); Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(after).Count());
    }

    [TestMethod]
    [DataRow("reentrant-valid")]
    [DataRow("reentrant-invalid")]
    [DataRow("required-failure")]
    public async Task MetadataReentryCannotPublishAndAllFailurePathsAllowAValidRetry(string scenario)
    {
        using var owned = new TemporaryDirectory();
        ProcessResult child = await ProcessRunner.RunAsync("dotnet", ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", Host(), "startup-reentrancy", scenario], owned.Path, TimeSpan.FromSeconds(15), Variables());
        JsonNode result = Read(child);
        Assert.IsFalse(result["pendingInitialized"]!.GetValue<bool>());
        Assert.AreEqual("InvalidOperationException", result["pendingCurrentError"]!.GetValue<string>());
        Assert.AreEqual("setup", result["nestedNullParameter"]!.GetValue<string>());
        Assert.IsFalse(result["nestedSucceeded"]!.GetValue<bool>()); Assert.AreEqual("InvalidOperationException", result["nestedError"]!.GetValue<string>());
        bool valid = scenario == "reentrant-valid";
        Assert.AreEqual(valid, result["outerSucceeded"]!.GetValue<bool>());
        Assert.AreEqual(valid, result["initializedAfterAttempt"]!.GetValue<bool>());
        Assert.AreEqual(!valid, result["retrySucceeded"]!.GetValue<bool>()); Assert.IsNull(result["retryError"]);
        Assert.AreEqual(valid ? null : scenario == "required-failure" ? "ControlledRequiredSourceException" : "CoreConfigurationException", result["outerError"]?.GetValue<string>());
        Assert.AreEqual(scenario == "required-failure", result["originalRequiredFailurePreserved"]!.GetValue<bool>());
        CollectionAssert.AreEqual(scenario == "reentrant-invalid" ? new[] { "OutOfRange:LoggingSetup.QueueCapacity" } : Array.Empty<string>(), result["issues"]!.AsArray().Select(value => value!.GetValue<string>()).ToArray());
        Assert.AreEqual(1, result["successfulInitializers"]!.GetValue<int>()); Assert.IsTrue(result["winnerReferenceStable"]!.GetValue<bool>());
        Assert.AreEqual(valid ? "Outer.App" : "Retry.App", result["current"]!["ApplicationName"]!.GetValue<string>());
        Assert.AreEqual(valid ? 2 : scenario == "required-failure" ? 1 : 2, result["callbackReads"]!.GetValue<int>());
        Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(owned.Path).Count());
    }

    private JsonNode Read(ProcessResult child)
    {
        Assert.AreEqual(0, child.ExitCode, child.StandardError); Assert.AreEqual(string.Empty, child.StandardError);
        TestContext.WriteLine(child.StandardOutput);
        JsonNode result = JsonNode.Parse(child.StandardOutput)!;
        JsonNode observed = result["observed"]!;
        Assert.AreEqual(".NET 10.0.12", observed["runtimeDescription"]!.GetValue<string>());
        Assert.AreEqual(Hash(Host()), observed["fixtureSha256"]!.GetValue<string>());
        Assert.AreEqual(Hash(Path.Combine(Path.GetDirectoryName(Host())!, "CIS.Core.dll")), observed["coreSha256"]!.GetValue<string>());
        return result;
    }
    private static string Host() => Path.Combine(AppContext.BaseDirectory, "discovery-fixture", "CIS.Core.DiscoveryFixture.dll");
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    private static Dictionary<string, string?> Variables()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string path = Path.Combine(directory.FullName, "contracts", "logging", "v1", "configuration.json");
            if (File.Exists(path)) { return JsonNode.Parse(File.ReadAllText(path))!["settings"]!.AsArray().SelectMany(row => new[] { row!["key"]!.GetValue<string>() }.Concat(row["aliases"]!.AsArray().Select(alias => alias!.GetValue<string>()))).ToDictionary(name => name, _ => (string?)null, StringComparer.Ordinal); }
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Cannot find startup catalog.");
    }
}
