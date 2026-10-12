using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class ConfigurationValidationProcessTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow("CFG-VALIDATE-ALL-DISABLED-COMPLETE-ERRORS")]
    [DataRow("CFG-VALIDATE-MINIMUM-BOUNDS-UNICODE")]
    [DataRow("CFG-VALIDATE-CORE-INVALID-SHADOWS-DD-SETUP")]
    [DataRow("CFG-VALIDATE-RELATIVE-UNICODE-TILDE-PATH-STORED")]
    public async Task CompleteValidationRemainsSafeAndStableAcrossActualChildDirectoryChanges(string id)
    {
        string host = Path.Combine(AppContext.BaseDirectory, "discovery-fixture", "CIS.Core.DiscoveryFixture.dll");
        string inputs = FindInputs();
        JsonNode row = JsonNode.Parse(File.ReadAllText(inputs))!["cases"]!.AsArray().Single(row => row!["id"]!.GetValue<string>() == id)!;
        using var owned = new TemporaryDirectory();
        string before = Path.Combine(owned.Path, "before");
        string after = Path.Combine(owned.Path, "after");
        Directory.CreateDirectory(before);
        Directory.CreateDirectory(after);
        string parentDirectory = Directory.GetCurrentDirectory();
        ProcessResult process = await ProcessRunner.RunAsync("dotnet", ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", host, "configuration-validation", inputs, id, after], before, TimeSpan.FromSeconds(15));
        Assert.AreEqual(0, process.ExitCode, process.StandardError);
        Assert.AreEqual(string.Empty, process.StandardError);
        TestContext.WriteLine(process.StandardOutput);
        JsonNode result = JsonNode.Parse(process.StandardOutput)!;
        JsonNode expected = row["expected"]!;
        Assert.AreEqual(expected["status"]!.GetValue<string>(), result["status"]!.GetValue<string>());
        string[] Pairs(JsonNode issues) => issues.AsArray().Select(issue => issue!["code"]!.GetValue<string>() + ":" + issue["key"]!.GetValue<string>()).ToArray();
        CollectionAssert.AreEquivalent(Pairs(expected["issues"]!), Pairs(result["issues"]!));
        JsonNode observed = result["observed"]!;
        Assert.AreEqual(before, observed["initialActualDirectory"]!.GetValue<string>());
        Assert.AreEqual(after, observed["afterActualDirectory"]!.GetValue<string>());
        Assert.AreEqual(1, observed["directoryReads"]!.GetValue<int>());
        Assert.IsTrue(observed["storedProjectionStable"]!.GetValue<bool>());
        CollectionAssert.AreEqual(ConfigurationReadExpectations.Expected(row["inputs"]!.AsObject()), observed["recognizedReads"]!.AsArray().Select(name => name!.GetValue<string>()).ToArray());
        Assert.AreEqual(Hash(host), observed["fixtureSha256"]!.GetValue<string>());
        Assert.AreEqual(Hash(Path.Combine(Path.GetDirectoryName(host)!, "CIS.Core.dll")), observed["coreSha256"]!.GetValue<string>());
        Assert.AreEqual(Hash(inputs), observed["inputsSha256"]!.GetValue<string>());
        Assert.AreEqual(".NET 10.0.12", observed["runtimeDescription"]!.GetValue<string>());
        if (expected["status"]!.GetValue<string>() == "Valid")
        {
            string? relative = expected["filePath"]?["input"]?.GetValue<string>();
            Assert.AreEqual(relative is null ? null : Path.Combine(before, relative.Replace('/', Path.DirectorySeparatorChar)), result["filePath"]?.GetValue<string>());
            Assert.AreEqual(before, result["workingDirectory"]!.GetValue<string>());
        }
        foreach (JsonNode? forbidden in row["diagnosticsMustExclude"]!.AsArray())
        {
            Assert.IsFalse(process.StandardOutput.Contains(forbidden!.GetValue<string>(), StringComparison.Ordinal));
        }
        Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(before).Count());
        Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(after).Count());
        Assert.AreEqual(2, Directory.EnumerateFileSystemEntries(owned.Path).Count());
        Assert.AreEqual(parentDirectory, Directory.GetCurrentDirectory());
    }

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    private static string FindInputs()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string path = Path.Combine(directory.FullName, "conformance", "logging", "passive-configuration-validation-cases.json");
            if (File.Exists(path)) { return path; }
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Cannot find full-validation fixtures.");
    }
}
