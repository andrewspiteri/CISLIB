using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.TestSupport;
using CIS.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class HealthcheckMutationNativeKernelEvidenceTests
{
    [TestMethod]
    public void CompletePinnedProcessAndThreadAncestryProvesOnlyTheDiscoveryExit()
    {
        using var fixture = new KernelFixture();
        Assert.IsTrue(fixture.Confirm(out JsonObject? proof));
        Assert.IsNotNull(proof);
        Assert.AreEqual("NativeKernelDiscoveryExit", (string?)proof["kind"]);
        Assert.AreEqual(0, (int?)proof["executionOutcomeCredit"]);
        Assert.IsFalse((bool)proof["callbackFabricated"]!);
        Assert.IsFalse((bool)proof["loadedImageClaim"]!);
        Assert.HasCount(3, proof["tracePins"]!.AsArray());
    }

    [TestMethod]
    [DataRow("unfinished-host")]
    [DataRow("unfinished-controller")]
    public void NonreturningExitGroupSupportsOnlyTheSameImmediateNaturalTerminal(string change)
    {
        using var fixture = new KernelFixture();
        fixture.Change(change);
        Assert.IsTrue(fixture.Confirm(out JsonObject? proof));
        Assert.IsNotNull(proof);
        var audit = fixture.Audit();
        Assert.HasCount(0, audit.Issues);
        Assert.AreEqual(1, (int)audit.Observations["kernelDiscoveryExitProofCount"]!);
    }

    [TestMethod]
    [DataRow("missing-host")]
    [DataRow("missing-thread")]
    [DataRow("wrong-parent")]
    [DataRow("wrong-diag")]
    [DataRow("wrong-deps")]
    [DataRow("wrong-executable")]
    [DataRow("wrong-controller-diag")]
    [DataRow("nonthread-creator")]
    [DataRow("duplicate-creation")]
    [DataRow("host-nonzero")]
    [DataRow("thread-nonzero")]
    [DataRow("controller-nonzero")]
    [DataRow("missing-exit-group")]
    [DataRow("contradictory-exit-group")]
    [DataRow("duplicate-exit-group")]
    [DataRow("reused-exec")]
    [DataRow("controller-exits-first")]
    [DataRow("wrong-runtime")]
    [DataRow("startup-parent-mismatch")]
    [DataRow("changed-input-after-read")]
    [DataRow("unfinished-nonzero")]
    [DataRow("unfinished-killed")]
    public void UnsupportedOrContradictoryKernelEvidenceCannotSupplyExitProof(string change)
    {
        using var fixture = new KernelFixture();
        fixture.Change(change);
        Assert.IsFalse(fixture.Confirm(out JsonObject? proof));
        Assert.IsNull(proof);
    }

    [TestMethod]
    [DataRow("bad-pin")]
    [DataRow("duplicate-pid")]
    [DataRow("unterminated-creation")]
    [DataRow("out-of-order")]
    [DataRow("after-terminal")]
    [DataRow("malformed-exit-group")]
    [DataRow("unfinished-exit-eof")]
    [DataRow("unfinished-exit-resumed")]
    [DataRow("unfinished-exit-activity")]
    [DataRow("unfinished-exit-second-call")]
    public void MalformedPhysicalKernelInputIsRejectedBeforeHostAssociation(string change)
    {
        using var fixture = new KernelFixture();
        fixture.Change(change);
        Assert.ThrowsExactly<InvalidDataException>(() => fixture.Read());
    }

    [TestMethod]
    public void MissingCallbackUsesKernelOnlyAfterTheSameDiscoveryProtocolCompletes()
    {
        using var fixture = new KernelFixture();
        var audit = fixture.Audit();
        Assert.HasCount(0, audit.Issues);
        CollectionAssert.AreEqual(new[] { "1" }, audit.AssertionKilledIds);
        Assert.AreEqual(1, (int)audit.Observations["normalDiscoveryHostCount"]!);
        Assert.AreEqual(1, (int)audit.Observations["kernelDiscoveryExitProofCount"]!);
        Assert.AreEqual(1, (int)audit.Observations["normalHostCount"]!);
    }

    [TestMethod]
    [DataRow("missing-kernel")]
    [DataRow("nonzero-callback")]
    [DataRow("duplicate-callback")]
    [DataRow("discovery-aborted")]
    [DataRow("discovery-incomplete")]
    [DataRow("discovery-missing-terminate")]
    [DataRow("discovery-duplicate-launch")]
    public void KernelCannotOverrideBadCallbacksOrIncompleteDiscovery(string change)
    {
        using var fixture = new KernelFixture();
        fixture.Change(change);
        var audit = fixture.Audit(change != "missing-kernel");
        Assert.IsNotEmpty(audit.Issues);
        Assert.AreEqual(0, (int)audit.Observations["normalDiscoveryHostCount"]!);
        Assert.AreEqual(0, (int)audit.Observations["kernelDiscoveryExitProofCount"]!);
    }

    [TestMethod]
    public void ExistingActualCallbackPathRemainsIndependentOfOptionalKernel()
    {
        using var fixture = new KernelFixture();
        fixture.Change("valid-callback");
        var audit = fixture.Audit(false);
        Assert.HasCount(0, audit.Issues);
        Assert.AreEqual(1, (int)audit.Observations["normalDiscoveryHostCount"]!);
        Assert.AreEqual(0, (int)audit.Observations["kernelDiscoveryExitProofCount"]!);
    }

    [TestMethod]
    public void DiscoveryExitProofDoesNotReplaceAnExecutionHostCallback()
    {
        using var fixture = new KernelFixture();
        fixture.Change("missing-execution-callback");
        var audit = fixture.Audit();
        Assert.IsNotEmpty(audit.Issues);
        Assert.HasCount(0, audit.AssertionKilledIds);
        Assert.AreEqual(1, (int)audit.Observations["normalDiscoveryHostCount"]!);
        Assert.AreEqual(1, (int)audit.Observations["kernelDiscoveryExitProofCount"]!);
        Assert.AreEqual(0, (int)audit.Observations["normalHostCount"]!);
    }

    [TestMethod]
    [DataRow("unit-runtime")]
    [DataRow("pipes")]
    [DataRow("aspnet")]
    public void StandardSingleFamilyDiscoveryKeepsItsActualSourceOwnershipAndProtocolForEveryGroup(string group)
    {
        using var fixture = new KernelFixture(group);
        Assert.IsTrue(fixture.Confirm(out JsonObject? proof));
        Assert.IsNotNull(proof);
        Assert.AreEqual(0, (int)proof["executionOutcomeCredit"]!);
        var audit = fixture.Audit();
        Assert.HasCount(0, audit.Issues);
        CollectionAssert.AreEqual(new[] { "1" }, audit.AssertionKilledIds);
        Assert.AreEqual(1, (int)audit.Observations["normalDiscoveryHostCount"]!);
        Assert.AreEqual(1, (int)audit.Observations["kernelDiscoveryExitProofCount"]!);
        Assert.AreEqual(1, (int)audit.Observations["normalHostCount"]!);
    }

    [TestMethod]
    [DataRow("pipes")]
    [DataRow("aspnet")]
    public void IntegrationDiscoveryProofCannotReplaceItsExecutionHostCallback(string group)
    {
        using var fixture = new KernelFixture(group);
        fixture.Change("missing-execution-callback");
        var audit = fixture.Audit();
        Assert.IsNotEmpty(audit.Issues);
        Assert.HasCount(0, audit.AssertionKilledIds);
        Assert.AreEqual(1, (int)audit.Observations["normalDiscoveryHostCount"]!);
        Assert.AreEqual(1, (int)audit.Observations["kernelDiscoveryExitProofCount"]!);
        Assert.AreEqual(0, (int)audit.Observations["normalHostCount"]!);
    }

    // Literal parser controls, never native process evidence. Only ObjectModel enum
    // decoding uses an actual installed test-platform assembly in these controls.
    private sealed class KernelFixture : IDisposable
    {
        private const string GuidText = "11111111-1111-4111-8111-111111111111";
        private readonly string Method, CodeFile, Dll, Filter;
        private const string Dotnet = "/synthetic/dotnet/dotnet";
        private const string Runtime = "/synthetic/dotnet/shared/Microsoft.NETCore.App/10.0.12";
        private const string NativeLogs = "/synthetic/native/logs";
        private const string DiscoveryName = "SpecificTestDiscoverer-log.txt";
        private const string DiscoveryHostName = "SpecificTestDiscoverer-log.host.synthetic.txt";
        private readonly TemporaryDirectory temporary = new();
        private readonly Dictionary<int, List<string>> traces = [];
        private readonly List<string> controller = [], host = [], discoveryController = [], discoveryHost = [];
        private readonly JsonObject report, configuration;
        private string? change;

        internal KernelFixture(string group = "unit-runtime")
        {
            string family = group == "unit-runtime" ? "UnitTests" : "IntegrationTests";
            string fixture = group switch
            {
                "unit-runtime" => "NativeKernelFixture",
                "pipes" => "HealthcheckProbeProcessTests",
                "aspnet" => "HealthcheckHttpTransportTests",
                _ => throw new InvalidDataException("Unknown synthetic kernel fixture group.")
            };
            Method = "CIS.Tests." + family + ".Healthcheck." + (group == "unit-runtime" ? "" : "Reporting.") + fixture + ".Direct";
            CodeFile = "/synthetic/implementations/csharp/tests/CIS.Core." + family + "/Healthcheck/" + (group == "unit-runtime" ? "Fixture.cs" : "Reporting/" + fixture + ".cs");
            Dll = "/synthetic/implementations/csharp/tests/CIS.Core." + family + "/bin/Release/net10.0/CIS.Core." + family + ".dll";
            Filter = "FullyQualifiedName~CIS.Tests." + family + ".Healthcheck.";
            string stem = Dll[..^4];
            traces[30] = [Exec("1.0", [Dotnet, "/synthetic/tools/vstest.console.dll", "/diag:" + NativeLogs + "/" + DiscoveryName + ";tracelevel=Verbose"]),
                "1.1 clone3({flags=CLONE_VM|CLONE_THREAD, exit_signal=0}, 88) = 31", "4.0 exit_group(0) = ?", "4.2 +++ exited with 0 +++"];
            traces[31] = ["2.0 vfork() = 40", "4.1 +++ exited with 0 +++"];
            traces[40] = [Exec("2.1", [Dotnet, "exec", "--runtimeconfig", stem + ".runtimeconfig.json", "--depsfile", stem + ".deps.json", Dll[..Dll.LastIndexOf('/')] + "/testhost.dll", "--parentprocessid", "30", "--diag", NativeLogs + "/" + DiscoveryHostName, "--role", "client"]),
                "3.0 exit_group(0)         = ?", "3.2 +++ exited with 0 +++"];
            report = new JsonObject
            {
                ["testFiles"] = new JsonObject { [CodeFile] = new JsonObject { ["tests"] = new JsonArray(new JsonObject { ["id"] = GuidText, ["name"] = Method }) } },
                ["files"] = new JsonObject { ["/synthetic/Core.cs"] = new JsonObject { ["mutants"] = new JsonArray(new JsonObject { ["id"] = "1", ["status"] = "Killed", ["killedBy"] = new JsonArray(GuidText) }) } }
            };
            configuration = new JsonObject { ["stryker-config"] = new JsonObject { ["target-framework"] = "net10.0", ["configuration"] = "Release", ["test-case-filter"] = Filter, ["test-projects"] = new JsonArray("../../tests/CIS.Core." + family + "/CIS.Core." + family + ".csproj") } };
            string settings = "<RunSettings><RunConfiguration><TargetFrameworkVersion>net10.0</TargetFrameworkVersion><TestCaseFilter>" + Filter + "</TestCaseFilter></RunConfiguration></RunSettings>";
            string executionSettings = settings.Replace("</RunSettings>", "<InProcDataCollectionRunSettings><InProcDataCollectors><InProcDataCollector><Configuration><Parameters><Mutant id=\"1\" tests=\"" + GuidText + "\"/><MutantControl name=\"StrykerSynthetic.MutantControl\"/></Parameters></Configuration></InProcDataCollector></InProcDataCollectors></InProcDataCollectionRunSettings></RunSettings>", StringComparison.Ordinal);
            controller.AddRange([Line(10, true, "Version: synthetic"), ControllerMessage(10, "TestExecution.GetTestRunnerProcessStartInfoForRunSelected", new JsonObject { ["RunSettings"] = executionSettings, ["TestCases"] = new JsonArray(TestCase()) }), ControllerMessage(10, "TestExecution.CustomTestHostLaunchCallback", new JsonObject { ["HostProcessId"] = 20, ["ErrorMessage"] = null }), Callback(10, 20, 0)]);
            host.AddRange([Line(20, false, "Version: synthetic"), Line(20, false, "Runtime location: " + Runtime), Line(20, false, "Testhost process started with args :[--parentprocessid, 10]"), Receive(20, "TestExecution.StartWithTests", new JsonObject { ["RunSettings"] = executionSettings, ["Tests"] = new JsonArray(TestCase()) }), Send(20, "TestExecution.StatsChange", new JsonObject { ["NewTestResults"] = new JsonArray(new JsonObject { ["TestCase"] = TestCase(), ["Outcome"] = 2, ["ErrorMessage"] = "Assertion failed. Expected true, observed false.", ["ErrorStackTrace"] = "at " + Method + "() in " + CodeFile + ":line 10" }) }), Send(20, "TestExecution.Completed", new JsonObject { ["TestRunCompleteArgs"] = new JsonObject { ["IsAborted"] = false, ["IsCanceled"] = false, ["Error"] = null, ["TestRunStatistics"] = new JsonObject { ["ExecutedTests"] = 1, ["Stats"] = new JsonObject { ["Failed"] = 1 } } }, ["LastRunTests"] = new JsonObject { ["NewTestResults"] = new JsonArray(), ["ActiveTests"] = new JsonArray() } }), Receive(20, "TestSession.Terminate", null), Line(20, false, "Testhost process exiting.")]);
            discoveryController.AddRange([Line(30, true, "Version: synthetic"), ControllerMessage(30, "TestDiscovery.Start", new JsonObject { ["Sources"] = new JsonArray(Dll), ["RunSettings"] = settings }), Line(30, true, "DotnetTestHostManager: Starting process '/synthetic/dotnet' with command line 'exec testhost.dll'")]);
            discoveryHost.AddRange([Line(40, false, "Version: synthetic"), Line(40, false, "Runtime location: " + Runtime), Line(40, false, "Testhost process started with args :[--parentprocessid, 30],[--diag, " + NativeLogs + "/" + DiscoveryHostName + "]"), Receive(40, "TestDiscovery.Start", new JsonObject { ["AdapterSourceMap"] = new JsonObject { ["_none_"] = new JsonArray(Dll) }, ["RunSettings"] = settings, ["TestCaseFilter"] = Filter }), Send(40, "TestDiscovery.TestFound", new JsonArray(TestCase())), Send(40, "TestDiscovery.Completed", new JsonObject { ["TotalTests"] = 1, ["LastDiscoveredTests"] = new JsonArray(), ["IsAborted"] = false, ["FullyDiscoveredSources"] = new JsonArray(Dll), ["PartiallyDiscoveredSources"] = new JsonArray(), ["NotDiscoveredSources"] = new JsonArray(), ["SkippedDiscoverySources"] = new JsonArray() }), Receive(40, "TestSession.Terminate", null), Line(40, false, "Testhost process exiting.")]);
        }

        internal void Change(string value)
        {
            change = value;
            switch (value)
            {
                case "missing-host": traces.Remove(40); break;
                case "missing-thread": traces.Remove(31); break;
                case "wrong-parent": traces[40][0] = traces[40][0].Replace("\"30\"", "\"99\"", StringComparison.Ordinal); break;
                case "wrong-diag": traces[40][0] = traces[40][0].Replace(DiscoveryHostName, "other.host.txt", StringComparison.Ordinal); break;
                case "wrong-deps": traces[40][0] = traces[40][0].Replace("CIS.Core.UnitTests.deps.json", "Other.deps.json", StringComparison.Ordinal); break;
                case "wrong-executable": traces[40][0] = traces[40][0].Replace(Dotnet, "/other/dotnet", StringComparison.Ordinal); break;
                case "wrong-controller-diag": traces[30][0] = traces[30][0].Replace(DiscoveryName, "other.txt", StringComparison.Ordinal); break;
                case "nonthread-creator": traces[30][1] = traces[30][1].Replace("CLONE_THREAD", "CLONE_VFORK", StringComparison.Ordinal); break;
                case "duplicate-creation": traces[31].Insert(1, "2.05 vfork() = 40"); break;
                case "host-nonzero": traces[40][^1] = "3.2 +++ exited with 1 +++"; break;
                case "thread-nonzero": traces[31][^1] = "4.1 +++ exited with 1 +++"; break;
                case "controller-nonzero": traces[30][^1] = "4.2 +++ exited with 1 +++"; break;
                case "missing-exit-group": traces[40].RemoveAt(1); break;
                case "unfinished-host": traces[40][1] = "3.0 exit_group(0 <unfinished ...>"; break;
                case "unfinished-controller": traces[30][2] = "4.0 exit_group(0 <unfinished ...>"; break;
                case "unfinished-nonzero": traces[40][1] = "3.0 exit_group(1 <unfinished ...>"; break;
                case "unfinished-killed": traces[40][1] = "3.0 exit_group(0 <unfinished ...>"; traces[40][2] = "3.2 +++ killed by SIGKILL +++"; break;
                case "unfinished-exit-eof": traces[40][1] = "3.0 exit_group(0 <unfinished ...>"; traces[40].RemoveAt(2); break;
                case "unfinished-exit-resumed": traces[40][1] = "3.0 exit_group(0 <unfinished ...>"; traces[40].Insert(2, "3.1 <... exit_group resumed>) = 0"); break;
                case "unfinished-exit-activity": traces[40][1] = "3.0 exit_group(0 <unfinished ...>"; traces[40].Insert(2, "3.1 getpid() = 40"); break;
                case "unfinished-exit-second-call": traces[40][1] = "3.0 exit_group(0 <unfinished ...>"; traces[40].Insert(2, "3.1 exit_group(0) = ?"); break;
                case "contradictory-exit-group": traces[40].Insert(1, "2.9 exit_group(1) = ?"); break;
                case "duplicate-exit-group": traces[40].Insert(1, "2.9 exit_group(0) = ?"); break;
                case "reused-exec": traces[40].Insert(1, traces[40][0].Replace("2.1 ", "2.2 ", StringComparison.Ordinal)); break;
                case "controller-exits-first": traces[40][^1] = "4.3 +++ exited with 0 +++"; break;
                case "startup-parent-mismatch": discoveryHost[2] = discoveryHost[2].Replace("parentprocessid, 30", "parentprocessid, 99", StringComparison.Ordinal); break;
                case "unterminated-creation": traces[31].Insert(0, "1.9 vfork( <unfinished ...>"); break;
                case "out-of-order": traces[40][1] = "2.0 exit_group(0) = ?"; break;
                case "after-terminal": traces[40].Add("3.3 getpid() = 40"); break;
                case "malformed-exit-group": traces[40][1] = "3.0 exit_group(0oops) = ?"; break;
                case "valid-callback": discoveryController.Add(Callback(30, 40, 0)); break;
                case "nonzero-callback": discoveryController.Add(Callback(30, 40, 1)); break;
                case "duplicate-callback": discoveryController.AddRange([Callback(30, 40, 0), Callback(30, 40, 0)]); break;
                case "discovery-aborted": discoveryHost[5] = discoveryHost[5].Replace("\"IsAborted\":false", "\"IsAborted\":true", StringComparison.Ordinal); break;
                case "discovery-incomplete": discoveryHost[5] = discoveryHost[5].Replace("\"TotalTests\":1", "\"TotalTests\":2", StringComparison.Ordinal); break;
                case "discovery-missing-terminate": discoveryHost.RemoveAt(6); break;
                case "discovery-duplicate-launch": discoveryController.Add(discoveryController[2]); break;
                case "missing-execution-callback": controller.RemoveAt(3); break;
            }
        }

        internal HealthcheckMutationNativeKernelEvidence Read()
        {
            WriteLogs();
            string executable = System.IO.Path.Combine(temporary.Path, "dotnet"); File.WriteAllText(executable, "synthetic executable identity only");
            var pins = new List<HealthcheckMutationNativeKernelEvidence.Pin>();
            foreach ((int pid, List<string> lines) in traces)
            {
                string path = System.IO.Path.Combine(temporary.Path, "actual-process." + pid); File.WriteAllLines(path, lines); pins.Add(Pin(path));
            }
            if (change == "bad-pin") { pins[0] = pins[0] with { Sha256 = new string('0', 64) }; }
            if (change == "duplicate-pid") { pins.Add(pins[0]); }
            var result = HealthcheckMutationNativeKernelEvidence.Read(pins, new(Pin(executable), Dotnet), temporary.Path, NativeLogs);
            if (change == "changed-input-after-read") { File.AppendAllText(System.IO.Path.Combine(temporary.Path, "actual-process.40"), "4.5 getpid() = 40\n"); }
            return result;
        }

        internal bool Confirm(out JsonObject? proof) => Read().TryConfirmDiscoveryHost(40, 30, Path(DiscoveryHostName), Path(DiscoveryName), [Dll], change == "wrong-runtime" ? "/other/shared/Microsoft.NETCore.App/10.0.12" : Runtime, out proof);
        internal HealthcheckMutationUnionModel.NativeAuditResult Audit(bool kernel = true)
        {
            HealthcheckMutationNativeKernelEvidence? evidence = kernel ? Read() : null; WriteLogs();
            return HealthcheckMutationNativeAudit.Audit(report, configuration, [Path("Runner 0-log.txt"), Path("Runner 0-log.host.synthetic.txt"), Path(DiscoveryName), Path(DiscoveryHostName)], System.IO.Path.Combine(AppContext.BaseDirectory, "Microsoft.VisualStudio.TestPlatform.ObjectModel.dll"), kernelEvidence: evidence);
        }
        private void WriteLogs() { File.WriteAllLines(Path("Runner 0-log.txt"), controller); File.WriteAllLines(Path("Runner 0-log.host.synthetic.txt"), host); File.WriteAllLines(Path(DiscoveryName), discoveryController); File.WriteAllLines(Path(DiscoveryHostName), discoveryHost); }
        private string Path(string name) => System.IO.Path.Combine(temporary.Path, name);
        private static HealthcheckMutationNativeKernelEvidence.Pin Pin(string path) { using FileStream file = File.OpenRead(path); return new(path, Convert.ToHexStringLower(SHA256.HashData(file)), file.Length); }
        private static string Exec(string time, string[] arguments) => time + " execve(" + JsonSerializer.Serialize(Dotnet) + ", [" + string.Join(", ", arguments.Select(argument => JsonSerializer.Serialize(argument))) + "], 0x1 /* environment */) = 0";
        private JsonObject TestCase() => new() { ["Id"] = GuidText, ["FullyQualifiedName"] = Method, ["Source"] = Dll, ["CodeFilePath"] = CodeFile };
        private static string Line(int pid, bool isController, string text) => "TpTrace Information: 0 : " + pid + ", 1, synthetic, " + (isController ? "vstest.console.dll, " : "testhost.dll, ") + text;
        private static string Message(string type, JsonNode? payload) => new JsonObject { ["MessageType"] = type, ["Payload"] = payload }.ToJsonString();
        private static string ControllerMessage(int pid, string type, JsonNode? payload) => Line(pid, true, "DesignModeClient.ProcessRequests: Processing Message: (" + type + ") -> " + Message(type, payload));
        private static string Receive(int pid, string type, JsonNode? payload) => Line(pid, false, "TestRequestHandler.OnMessageReceived: received message: " + Message(type, payload));
        private static string Send(int pid, string type, JsonNode? payload) => Line(pid, false, "TestRequestHandler.SendData: sending data from testhost: " + Message(type, payload));
        private static string Callback(int controllerPid, int hostPid, int exit) => Line(controllerPid, true, "TestHostManagerCallbacks.ExitCallBack: Testhost processId: " + hostPid + " exited with exitcode: " + exit + " error: ''");
        public void Dispose() => temporary.Dispose();
    }
}
