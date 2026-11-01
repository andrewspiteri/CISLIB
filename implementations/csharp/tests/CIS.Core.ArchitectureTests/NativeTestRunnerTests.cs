using System.Text.Json;
using System.Xml.Linq;
using CIS.TestSupport;
using CIS.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class NativeTestRunnerTests
{
    [TestMethod]
    public void NativeArgumentsKeepOneAssemblyAndWholeSelectorTokens()
    {
        string assembly = Path.Combine(Path.GetTempPath(), "space & semicolon;", "tests.dll");
        string settings = Path.Combine(Path.GetTempPath(), "settings space", "profile.runsettings");
        string[] arguments = NativeTestRunner.Arguments("vstest.console.dll", assembly, settings, "results space", RegressionTests.Filter);
        CollectionAssert.AreEqual(new[] { "exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", "vstest.console.dll" }, arguments.Take(6).ToArray());
        Assert.AreEqual(assembly, arguments[6]);
        Assert.AreEqual(1, arguments.Count(item => item == assembly));
        Assert.IsTrue(arguments.Contains("/Settings:" + settings));
        Assert.IsTrue(arguments.Contains("/TestCaseFilter:" + RegressionTests.Filter));
        Assert.IsFalse(NativeTestRunner.Arguments("vstest.console.dll", assembly, settings, "results").Any(item => item.StartsWith("/TestCaseFilter:", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void NativeRuntimeRequiresAllCompanionsAndExactFrameworkWithoutRollForward()
    {
        using var root = new TemporaryDirectory();
        string assembly = Path.Combine(root.Path, "tests.dll");
        File.WriteAllText(assembly, "fixture");
        Assert.ThrowsExactly<InvalidDataException>(() => NativeTestRunner.ValidateAssembly(assembly));
        File.WriteAllText(Path.ChangeExtension(assembly, ".deps.json"), "{}");
        string runtime = Path.ChangeExtension(assembly, ".runtimeconfig.json");
        File.WriteAllText(runtime, """{"runtimeOptions":{"rollForward":"Disable","framework":{"name":"Microsoft.NETCore.App","version":"10.0.12"}}}""");
        NativeTestRunner.ValidateAssembly(assembly);
        File.WriteAllText(runtime, File.ReadAllText(runtime).Replace("10.0.12", "10.0.999", StringComparison.Ordinal));
        Assert.ThrowsExactly<InvalidDataException>(() => NativeTestRunner.ValidateAssembly(assembly));
        File.WriteAllText(runtime, """{"runtimeOptions":{"rollForward":"LatestPatch","framework":{"name":"Microsoft.NETCore.App","version":"10.0.12"}}}""");
        Assert.ThrowsExactly<InvalidDataException>(() => NativeTestRunner.ValidateAssembly(assembly));
        File.WriteAllText(runtime, """{"runtimeOptions":{"rollForward":"Disable","frameworks":[{"name":"Microsoft.NETCore.App","version":"10.0.12"},{"name":"Microsoft.AspNetCore.App","version":"10.0.12"}]}}""");
        NativeTestRunner.ValidateAssembly(assembly);
        File.WriteAllText(runtime, """{"runtimeOptions":{"rollForward":"Disable"}}""");
        Assert.ThrowsExactly<InvalidDataException>(() => NativeTestRunner.ValidateAssembly(assembly));
    }

    [TestMethod]
    public void HostIdentityRejectsMissingDuplicateOrDifferentAssemblyEvidence()
    {
        using var root = new TemporaryDirectory();
        string assembly = Path.Combine(root.Path, "tests.dll"), report = Path.Combine(root.Path, "report.trx");
        string marker = NativeTestRunner.HostMarker + JsonSerializer.Serialize(new { processId = Environment.ProcessId, startedUtc = DateTimeOffset.UtcNow.ToString("O"), assembly });
        WriteReport(marker);
        Assert.AreEqual(Environment.ProcessId, NativeTestRunner.ReadHostIdentity(report, assembly).ProcessId);
        Assert.ThrowsExactly<InvalidDataException>(() => NativeTestRunner.ReadHostIdentity(report, assembly + ".other"));
        WriteReport("");
        Assert.ThrowsExactly<InvalidDataException>(() => NativeTestRunner.ReadHostIdentity(report, assembly));
        WriteReport(marker + "\n" + marker);
        Assert.ThrowsExactly<InvalidDataException>(() => NativeTestRunner.ReadHostIdentity(report, assembly));

        void WriteReport(string output)
        {
            XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
            new XDocument(new XElement(ns + "TestRun",
                new XElement(ns + "TestDefinitions", new XElement(ns + "UnitTest", new XAttribute("id", "fixture"),
                    new XElement(ns + "TestMethod", new XAttribute("name", "SchedulingMatchesRequestedMode"), new XAttribute("className", "CIS.Tests.TestAssemblySetup")))),
                new XElement(ns + "Results", new XElement(ns + "UnitTestResult", new XAttribute("testId", "fixture"),
                    new XElement(ns + "Output", new XElement(ns + "StdOut", output)))))).Save(report);
        }
    }

    [TestMethod]
    public void FastSolutionCannotSilentlyDropOrAddAnOwner()
    {
        using var root = new TemporaryDirectory();
        string[] owners = ["CIS.Core.FastTests", "CIS.Core.FastLoggingTests", "CIS.Core.FastIsolationTests", "CIS.Core.FastArchitectureTests"];
        foreach (string owner in owners.Append("Unexpected.Tests"))
        {
            string directory = Path.Combine(root.Path, "tests", owner);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, owner + ".csproj"), "<Project><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup></Project>");
        }
        WriteSolution(owners);
        FastTests.ValidateProjects(root.Path);
        WriteSolution(owners.Skip(1));
        Assert.ThrowsExactly<InvalidDataException>(() => FastTests.ValidateProjects(root.Path));
        WriteSolution(owners.Append("Unexpected.Tests"));
        Assert.ThrowsExactly<InvalidDataException>(() => FastTests.ValidateProjects(root.Path));

        void WriteSolution(IEnumerable<string> projects) => new XDocument(new XElement("Solution", projects.Select(project =>
            new XElement("Project", new XAttribute("Path", "tests/" + project + "/" + project + ".csproj"))))).Save(Path.Combine(root.Path, "CisLib.slnx"));
    }
}
