using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using CIS.TestSupport;
using CIS.TypeManagement;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class ConfigurationSourceConsumerTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task ActualBuiltSourceFixtureDiscoversPassiveConfigurationAndStartsWithTheSameDefaultRoots()
    {
        Assert.IsTrue(OperatingSystem.IsWindows() || OperatingSystem.IsLinux());
        Assert.AreEqual(Architecture.X64, RuntimeInformation.ProcessArchitecture);
        string language = Path.Combine(CandidatePackageFixture.Repository(), "implementations/csharp");
        string configuration = typeof(TypeManager).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        string rid = OperatingSystem.IsWindows() ? "win-x64" : "linux-x64";
        string output = Path.Combine(language, "tests/fixtures/TypeManager/Host/bin", configuration, "net10.0", rid);
        const string assembly = "CIS.TypeManager.Fixture.Host";
        string application = Path.Combine(output, assembly + ".dll");
        foreach (string prerequisite in new[] { application, Path.Combine(output, assembly + ".types.json"),
            Path.Combine(output, "CIS.Core.dll"), Path.Combine(output, "CIS.TypeManager.Fixture.Unused.dll") })
        {
            Assert.IsTrue(File.Exists(prerequisite),
                "Build the existing TypeManager Host fixture before this case; no build runs inside the testhost: " + prerequisite);
        }
        string currentCore = CandidatePackageFixture.Hash(typeof(TypeManager).Assembly.Location);
        Assert.AreEqual(currentCore, CandidatePackageFixture.Hash(Path.Combine(output, "CIS.Core.dll")),
            "The source fixture must execute the current test candidate's exact Core bytes.");
        ProcessResult result = await ProcessRunner.RunAsync("dotnet",
            ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", application],
            output, TimeSpan.FromSeconds(45));
        Assert.IsTrue(result.OutputDrained);
        Assert.IsFalse(result.CaptureTruncated);
        Assert.AreEqual(0, result.ExitCode, result.StandardOutput + result.StandardError);
        Assert.AreEqual("", result.StandardError);
        string json = result.StandardOutput.Split('\n').Last(line => line.TrimStart().StartsWith('{'));
        JsonNode report = JsonNode.Parse(json)!;
        Assert.IsFalse(report["unusedInitiallyLoaded"]!.GetValue<bool>());
        Assert.IsTrue(report["containerConstructed"]!.GetValue<bool>());
        Assert.IsTrue(report["configurationDefaults"]!.GetValue<bool>());
        Assert.IsTrue(report["sameConfigurationAcrossScopes"]!.GetValue<bool>());
        Assert.AreEqual(1, report["startupCalls"]!.GetValue<int>());
        Assert.IsTrue(report["stopped"]!.GetValue<bool>());
        TestContext.WriteLine("CIS-CONFIGURATION-SOURCE fixture coreSha256=" + currentCore + " " + json);
    }
}
