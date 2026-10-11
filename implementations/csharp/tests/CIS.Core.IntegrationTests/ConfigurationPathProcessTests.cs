using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class ConfigurationPathProcessTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow("CFG-LOG-FILE-PATH-RELATIVE", "logs/app.jsonl")]
    [DataRow("CFG-LOG-FILE-PATH-BLANK-CORE-USES-SETUP", "logs/app.jsonl")]
    [DataRow("CFG-LOG-FILE-PATH-ABSENT", null)]
    public async Task ChildCwdChangeCannotRedirectValidatedPathOrCreateOutputs(string id, string? relative)
    {
        string fixture = Path.Combine(AppContext.BaseDirectory, "discovery-fixture", "CIS.Core.DiscoveryFixture.dll");
        string inputs = FindInputs();
        using var owned = new TemporaryDirectory();
        string beforeDirectory = Path.Combine(owned.Path, "before");
        string afterDirectory = Path.Combine(owned.Path, "after");
        Directory.CreateDirectory(beforeDirectory);
        Directory.CreateDirectory(afterDirectory);
        string parentBefore = Directory.GetCurrentDirectory();
        ProcessResult process = await ProcessRunner.RunAsync("dotnet", ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", fixture, "configuration-path", inputs, id, afterDirectory], beforeDirectory, TimeSpan.FromSeconds(15));
        Assert.AreEqual(0, process.ExitCode, process.StandardOutput + process.StandardError);
        Assert.AreEqual(string.Empty, process.StandardError);
        TestContext.WriteLine(process.StandardOutput);
        JsonNode result = JsonNode.Parse(process.StandardOutput)!;
        Assert.AreEqual(beforeDirectory, result["initialActualDirectory"]!.GetValue<string>());
        Assert.AreEqual(afterDirectory, result["afterActualDirectory"]!.GetValue<string>());
        Assert.AreEqual("actual-child-cwd", result["directorySource"]!.GetValue<string>());
        Assert.AreEqual(beforeDirectory, result["capturedDirectory"]!.GetValue<string>());
        Assert.AreEqual(beforeDirectory, result["validatedWorkingDirectory"]!.GetValue<string>());
        Assert.AreEqual(1, result["directoryReads"]!.GetValue<int>());
        string? expected = relative is null ? null : Path.Combine(beforeDirectory, "logs", "app.jsonl");
        Assert.AreEqual(expected, result["absolutePathBefore"]?.GetValue<string>());
        Assert.AreEqual(expected, result["absolutePathAfter"]?.GetValue<string>());
        Assert.AreEqual(0, result["issues"]!.AsArray().Count);
        Assert.AreEqual(21, result["observed"]!["recognizedReads"]!.AsArray().Count);
        Assert.AreEqual(Hash(fixture), result["observed"]!["fixtureSha256"]!.GetValue<string>());
        Assert.AreEqual(Hash(Path.Combine(Path.GetDirectoryName(fixture)!, "CIS.Core.dll")), result["observed"]!["coreSha256"]!.GetValue<string>());
        Assert.AreEqual(Hash(inputs), result["observed"]!["inputsSha256"]!.GetValue<string>());
        Assert.AreEqual(".NET 10.0.12", result["observed"]!["runtimeDescription"]!.GetValue<string>());
        Assert.AreEqual(0, Directory.GetFileSystemEntries(beforeDirectory).Length);
        Assert.AreEqual(0, Directory.GetFileSystemEntries(afterDirectory).Length);
        Assert.AreEqual(2, Directory.GetFileSystemEntries(owned.Path).Length);
        Assert.AreEqual(parentBefore, Directory.GetCurrentDirectory());
    }

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    private static string FindInputs()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string path = Path.Combine(directory.FullName, "conformance", "logging", "configuration-cases.json");
            if (File.Exists(path))
            {
                return path;
            }
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Cannot find shared path inputs.");
    }
}
