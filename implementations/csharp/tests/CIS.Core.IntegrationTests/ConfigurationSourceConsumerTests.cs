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
    [DataRow("Console")]
    [DataRow("Web")]
    public async Task ActualBuiltSourceHostLoadsDiscoveredConfigurationBeforeConstructionAndRefreshesTheSameRoots(string host)
    {
        Assert.IsTrue(OperatingSystem.IsWindows() || OperatingSystem.IsLinux());
        Assert.AreEqual(Architecture.X64, RuntimeInformation.ProcessArchitecture);
        string language = Path.Combine(CandidatePackageFixture.Repository(), "implementations/csharp");
        string configuration = typeof(TypeManager).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        string rid = OperatingSystem.IsWindows() ? "win-x64" : "linux-x64";
        string output = Path.Combine(language, "examples/type-manager", host, "bin", configuration, "net10.0", rid);
        string assembly = "CIS.TypeManager.Examples." + host;
        string application = Path.Combine(output, assembly + ".dll");
        string late = Path.Combine(language, "tests/fixtures/TypeManager/ExampleLate/bin", configuration,
            "net10.0/CIS.TypeManager.Examples.Late.dll");
        foreach (string prerequisite in new[] { application, Path.Combine(output, assembly + ".types.json"),
            Path.Combine(output, "CIS.Core.dll"), late })
        {
            Assert.IsTrue(File.Exists(prerequisite),
                "Build the actual source Console/Web and ExampleLate prerequisites before running this case; no build runs inside the testhost: " + prerequisite);
        }
        string currentCore = CandidatePackageFixture.Hash(typeof(TypeManager).Assembly.Location);
        Assert.AreEqual(currentCore, CandidatePackageFixture.Hash(Path.Combine(output, "CIS.Core.dll")),
            "The source host must execute the current test candidate's exact Core bytes.");
        ProcessResult result = await ProcessRunner.RunAsync("dotnet",
            ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", application, "--verify", late],
            output, TimeSpan.FromSeconds(45));
        Assert.IsTrue(result.OutputDrained);
        Assert.IsFalse(result.CaptureTruncated);
        Assert.AreEqual(0, result.ExitCode, result.StandardOutput + result.StandardError);
        Assert.AreEqual("", result.StandardError);
        string json = result.StandardOutput.Split('\n').Last(line => line.TrimStart().StartsWith('{'));
        JsonNode report = JsonNode.Parse(json)!;
        Assert.IsFalse(report["unusedInitiallyLoaded"]!.GetValue<bool>());
        Assert.IsTrue(report["fixedMembershipChecked"]!.GetValue<bool>());
        CollectionAssert.AreEquivalent(new[] { "CIS.TypeManagement.Examples.Unused.DirectExample", "CIS.TypeManagement.Examples.Unused.InheritedExample" },
            report["candidates"]!.AsArray().Select(item => item!["type"]!.GetValue<string>()).ToArray());
        if (host == "Web")
        {
            Assert.IsTrue(report["rootAndScopesShareManager"]!.GetValue<bool>());
            Assert.IsTrue(report["queryIdentityStable"]!.GetValue<bool>());
        }
        JsonNode values = report["configuration"]!;
        Assert.AreEqual("configured-before-construction", values["constructorGreeting"]!.GetValue<string>());
        Assert.AreEqual(42, values["constructorBatchSize"]!.GetValue<int>());
        Assert.IsTrue(values["rootAndScopesShareConfiguration"]!.GetValue<bool>());
        Assert.IsTrue(values["startupReaderObservedConfiguration"]!.GetValue<bool>());
        Assert.IsTrue(values["startupReaderObservedWatch"]!.GetValue<bool>());
        Assert.IsFalse(values["watchActiveInConstructor"]!.GetValue<bool>());
        Assert.IsTrue(values["sameRootsAfterRefresh"]!.GetValue<bool>());
        Assert.AreEqual("refreshed-after-startup", values["currentGreeting"]!.GetValue<string>());
        Assert.AreEqual(84, values["currentBatchSize"]!.GetValue<int>());
        Assert.AreEqual(2, values["changedEvents"]!.GetValue<int>());
        Assert.IsTrue(values["watchActivated"]!.GetValue<bool>());
        CollectionAssert.AreEquivalent(new[] { "CIS.TypeManagement.Examples.Unused.DirectConfiguration", "CIS.TypeManagement.Examples.Unused.InheritedConfiguration" },
            values["discovered"]!.AsArray().Select(item => item!["type"]!.GetValue<string>()).ToArray());
        Assert.IsTrue(report["configurationWatchDisposed"]!.GetValue<bool>());
        Assert.IsFalse(report["configurationStoreDisposedByRuntime"]!.GetValue<bool>());
        TestContext.WriteLine("CIS-CONFIGURATION-SOURCE " + host + " coreSha256=" + currentCore + " " + json);
    }
}
