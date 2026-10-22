using System.Text.Json.Nodes;
using CIS.TestSupport;
using CIS.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

// Synthetic protocol receiving controls only. These are not native evidence or
// manufactured replacements for the immutable full Pipes20 diagnostic files.
[TestClass]
public sealed class HealthcheckNativeDiscoveryOwnershipReceivingTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CompleteActualDiscoveryAssignsEachLinkedGuidToItsOwnDllBeforeSourceBasedExecution(bool concatenateControllers)
    {
        using var fixture = new Fixture(concatenateControllers);
        var audit = fixture.Audit();
        Assert.HasCount(0, audit.Issues);
        CollectionAssert.AreEqual(new[] { "1" }, audit.AssertionKilledIds);
        Assert.AreEqual(1, audit.NormalHostCount);
        Assert.AreEqual(2, (int?)audit.Observations["normalDiscoveryHostCount"]);
        Assert.AreEqual(2, (int?)audit.Observations["discoveredGuidCount"]);
    }

    [TestMethod]
    [DataRow("linked-guid-missing")]
    [DataRow("linked-guid-duplicate")]
    [DataRow("linked-guid-unknown")]
    [DataRow("linked-method-wrong")]
    [DataRow("linked-source-cross-project")]
    [DataRow("linked-source-unknown")]
    [DataRow("discovery-aborted")]
    [DataRow("discovery-partial")]
    [DataRow("discovery-warning")]
    [DataRow("controller-terminate-missing-first")]
    [DataRow("controller-exit-missing-first")]
    [DataRow("controller-exit-nonzero-first")]
    [DataRow("controller-terminate-missing-last")]
    [DataRow("controller-exit-missing-last")]
    [DataRow("controller-exit-before-terminate")]
    [DataRow("controller-pid-reused")]
    [DataRow("controller-pid-interleaved")]
    [DataRow("controller-new-pid-without-header")]
    [DataRow("controller-own-host-exit-nonzero")]
    [DataRow("controller-own-host-exit-missing")]
    [DataRow("controller-mixed-discovery-and-mutation")]
    [DataRow("controller-pid-duplicate-physical")]
    [DataRow("controller-classify-envelope-malformed")]
    [DataRow("execution-cross-project-result")]
    [DataRow("execution-abort")]
    [DataRow("execution-cleanup")]
    public void LinkedOwnershipAndSequentialLogsCannotHideMissingCasesConflictingOriginsOrActualFailures(string mode)
    {
        using var fixture = new Fixture(concatenateControllers: true);
        fixture.Corrupt(mode);
        var audit = fixture.Audit();
        Assert.IsGreaterThan(0, audit.Issues.Length);
        Assert.HasCount(0, audit.AssertionKilledIds);
    }

    private const string GuidA = "11111111-1111-4111-8111-111111111111", GuidB = "22222222-2222-4222-8222-222222222222";
    private const string Method = "CIS.Tests.TestAssemblySetup.SchedulingMatchesRequestedMode";
    private const string Code = "/synthetic/implementations/csharp/tests/TestAssembly.cs";
    private const string Unit = "/synthetic/implementations/csharp/tests/CIS.Core.UnitTests/bin/Release/net10.0/CIS.Core.UnitTests.dll";
    private const string Integration = "/synthetic/implementations/csharp/tests/CIS.Core.IntegrationTests/bin/Release/net10.0/CIS.Core.IntegrationTests.dll";
    private const string Filter = "FullyQualifiedName=CIS.Tests.TestAssemblySetup.SchedulingMatchesRequestedMode";
    private const string Receive = "TestRequestHandler.OnMessageReceived: received message: ", Send = "TestRequestHandler.SendData: sending data from testhost: ";
    private static JsonObject Case(string guid, string source) => new() { ["Id"] = guid, ["FullyQualifiedName"] = Method, ["Source"] = source, ["CodeFilePath"] = Code };
    private static string Settings(bool mutation) => "<RunSettings><RunConfiguration><TargetFrameworkVersion>net10.0</TargetFrameworkVersion><TestCaseFilter>" + Filter + "</TestCaseFilter></RunConfiguration>"
        + (mutation ? "<InProcDataCollectionRunSettings><InProcDataCollectors><InProcDataCollector><Configuration><Parameters><Mutant id=\"1\" tests=\"\"/><MutantControl name=\"StrykerSynthetic.MutantControl\"/></Parameters></Configuration></InProcDataCollector></InProcDataCollectors></InProcDataCollectionRunSettings>" : "") + "</RunSettings>";
    private static string Line(int pid, bool controller, string text) => "TpTrace Information: 0 : " + pid.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", 1, synthetic, " + (controller ? "vstest.console.dll, " : "testhost.dll, ") + text;
    private static string Message(string type, JsonNode? payload) => new JsonObject { ["MessageType"] = type, ["Payload"] = payload }.ToJsonString();
    private static string Request(int pid, string type, JsonNode? payload) => Line(pid, true, "DesignModeClient.ProcessRequests: Processing Message: (" + type + ") -> " + Message(type, payload));
    private static string Incoming(int pid, string type, JsonNode? payload) => Line(pid, false, Receive + Message(type, payload));
    private static string Outgoing(int pid, string type, JsonNode? payload) => Line(pid, false, Send + Message(type, payload));
    private static string Callback(int parent, int child) => Line(parent, true, "TestHostManagerCallbacks.ExitCallBack: Testhost processId: " + child + " exited with exitcode: 0 error: ''");

    private sealed class Fixture : IDisposable
    {
        private readonly TemporaryDirectory temporary = new();
        private readonly bool concatenate;
        private readonly List<string> controllerA, controllerB, discoveryA, discoveryB, runner, host;
        private readonly JsonObject report, config;
        private bool duplicatePhysicalController;

        internal Fixture(bool concatenateControllers)
        {
            concatenate = concatenateControllers;
            report = new JsonObject
            {
                ["testFiles"] = new JsonObject { [Code] = new JsonObject { ["tests"] = new JsonArray(new JsonObject { ["id"] = GuidA, ["name"] = Method }, new JsonObject { ["id"] = GuidB, ["name"] = Method }) } },
                ["files"] = new JsonObject { ["/synthetic/Core.cs"] = new JsonObject { ["mutants"] = new JsonArray(new JsonObject { ["id"] = "1", ["status"] = "Killed", ["killedBy"] = new JsonArray(GuidA) }) } }
            };
            config = new JsonObject { ["stryker-config"] = new JsonObject { ["target-framework"] = "net10.0", ["configuration"] = "Release", ["test-case-filter"] = Filter, ["test-projects"] = new JsonArray("../../tests/CIS.Core.UnitTests/CIS.Core.UnitTests.csproj", "../../tests/CIS.Core.IntegrationTests/CIS.Core.IntegrationTests.csproj") } };
            (controllerA, discoveryA) = Discovery(30, 40, Unit, GuidA);
            (controllerB, discoveryB) = Discovery(31, 41, Integration, GuidB);
            runner = [Line(10, true, "Version: synthetic"), Request(10, "TestExecution.GetTestRunnerProcessStartInfoForRunAll", new JsonObject { ["Sources"] = new JsonArray(Unit), ["RunSettings"] = Settings(true) }), Request(10, "TestExecution.CustomTestHostLaunchCallback", new JsonObject { ["HostProcessId"] = 20, ["ErrorMessage"] = null }), Callback(10, 20)];
            host =
            [
                Line(20, false, "Version: synthetic"), Line(20, false, "Runtime location: /synthetic/dotnet/shared/Microsoft.NETCore.App/10.0.12"), Line(20, false, "Testhost process started with args :[--parentprocessid, 10]"),
                Incoming(20, "TestExecution.StartWithSources", new JsonObject { ["AdapterSourceMap"] = new JsonObject { ["_none_"] = new JsonArray(Unit) }, ["RunSettings"] = Settings(true) }),
                Outgoing(20, "TestExecution.StatsChange", new JsonObject { ["NewTestResults"] = new JsonArray(new JsonObject { ["TestCase"] = Case(GuidA, Unit), ["Outcome"] = 2, ["ErrorMessage"] = "Assertion failed. Expected true, observed false.", ["ErrorStackTrace"] = "at " + Method + "() in " + Code + ":line 10" }) }),
                Outgoing(20, "TestExecution.Completed", new JsonObject { ["TestRunCompleteArgs"] = new JsonObject { ["IsAborted"] = false, ["IsCanceled"] = false, ["Error"] = null, ["TestRunStatistics"] = new JsonObject { ["ExecutedTests"] = 1, ["Stats"] = new JsonObject { ["Failed"] = 1 } } }, ["LastRunTests"] = new JsonObject { ["NewTestResults"] = new JsonArray(), ["ActiveTests"] = new JsonArray() } }),
                Incoming(20, "TestSession.Terminate", null), Line(20, false, "Testhost process exiting.")
            ];
        }

        private static (List<string> Controller, List<string> Host) Discovery(int parent, int child, string source, string guid)
        {
            List<string> controller = [Line(parent, true, "Version: synthetic"), Request(parent, "TestDiscovery.Start", new JsonObject { ["Sources"] = new JsonArray(source), ["RunSettings"] = Settings(false) }), Line(parent, true, "DotnetTestHostManager: Starting process '/synthetic/dotnet' with command line 'exec /synthetic/testhost.dll --parentprocessid " + parent + "'"), Callback(parent, child), Request(parent, "TestSession.Terminate", null), Line(parent, true, "Executor.Execute: Exiting with exit code of 0")];
            List<string> host = [Line(child, false, "Version: synthetic"), Line(child, false, "Runtime location: /synthetic/dotnet/shared/Microsoft.NETCore.App/10.0.12"), Line(child, false, "Testhost process started with args :[--parentprocessid, " + parent + "]"), Incoming(child, "TestDiscovery.Start", new JsonObject { ["AdapterSourceMap"] = new JsonObject { ["_none_"] = new JsonArray(source) }, ["RunSettings"] = Settings(false), ["TestCaseFilter"] = Filter }), Outgoing(child, "TestDiscovery.TestFound", new JsonArray(Case(guid, source))), Outgoing(child, "TestDiscovery.Completed", new JsonObject { ["TotalTests"] = 1, ["LastDiscoveredTests"] = new JsonArray(), ["IsAborted"] = false, ["FullyDiscoveredSources"] = new JsonArray(source), ["PartiallyDiscoveredSources"] = new JsonArray(), ["NotDiscoveredSources"] = new JsonArray(), ["SkippedDiscoverySources"] = new JsonArray() }), Incoming(child, "TestSession.Terminate", null), Line(child, false, "Testhost process exiting.")];
            return (controller, host);
        }

        internal void Corrupt(string mode)
        {
            switch (mode)
            {
                case "linked-guid-missing": Change(discoveryB, Send, "TestDiscovery.TestFound", message => message["Payload"] = new JsonArray()); break;
                case "linked-guid-duplicate": Change(discoveryB, Send, "TestDiscovery.TestFound", message => message["Payload"]!.AsArray().Add(message["Payload"]![0]!.DeepClone())); break;
                case "linked-guid-unknown": Change(discoveryB, Send, "TestDiscovery.TestFound", message => message["Payload"]![0]!["Id"] = "33333333-3333-4333-8333-333333333333"); break;
                case "linked-method-wrong": Change(discoveryB, Send, "TestDiscovery.TestFound", message => message["Payload"]![0]!["FullyQualifiedName"] = Method + "Other"); break;
                case "linked-source-cross-project": Change(discoveryB, Send, "TestDiscovery.TestFound", message => message["Payload"]![0]!["Source"] = Unit); break;
                case "linked-source-unknown": Change(discoveryB, Send, "TestDiscovery.TestFound", message => message["Payload"]![0]!["Source"] = "/other/Unknown.dll"); break;
                case "discovery-aborted": Change(discoveryB, Send, "TestDiscovery.Completed", message => message["Payload"]!["IsAborted"] = true); break;
                case "discovery-partial": Change(discoveryB, Send, "TestDiscovery.Completed", message => message["Payload"]!["PartiallyDiscoveredSources"]!.AsArray().Add(Integration)); break;
                case "discovery-warning": discoveryB.Insert(5, Outgoing(41, "TestSession.Message", new JsonObject { ["MessageLevel"] = 1, ["Message"] = "Synthetic warning" })); break;
                case "controller-terminate-missing-first": controllerA.RemoveAt(4); break;
                case "controller-exit-missing-first": controllerA.RemoveAt(5); break;
                case "controller-exit-nonzero-first": controllerA[5] = controllerA[5].Replace("code of 0", "code of 1", StringComparison.Ordinal); break;
                case "controller-terminate-missing-last": controllerB.RemoveAt(4); break;
                case "controller-exit-missing-last": controllerB.RemoveAt(5); break;
                case "controller-exit-before-terminate": (controllerA[4], controllerA[5]) = (controllerA[5], controllerA[4]); break;
                case "controller-pid-reused":
                    for (int i = 0; i < controllerB.Count; i++)
                    {
                        controllerB[i] = controllerB[i].Replace("0 : 31,", "0 : 30,", StringComparison.Ordinal);
                    }
                    break;
                case "controller-pid-interleaved": controllerA.Insert(2, controllerB[0]); break;
                case "controller-new-pid-without-header": controllerA.Insert(2, Line(31, true, "Synthetic conflicting process record")); break;
                case "controller-own-host-exit-nonzero": controllerA[3] = controllerA[3].Replace("exitcode: 0", "exitcode: 134", StringComparison.Ordinal); break;
                case "controller-own-host-exit-missing": controllerA.RemoveAt(3); break;
                case "controller-mixed-discovery-and-mutation": controllerB.AddRange(runner); break;
                case "controller-pid-duplicate-physical": duplicatePhysicalController = true; break;
                case "controller-classify-envelope-malformed": controllerA[1] = controllerA[1].Replace("\"MessageType\":\"TestDiscovery.Start\"", "\"MessageType\":\"TestDiscovery.Wrong\"", StringComparison.Ordinal); break;
                case "execution-cross-project-result": Change(host, Send, "TestExecution.StatsChange", message => message["Payload"]!["NewTestResults"]![0]!["TestCase"] = Case(GuidB, Integration)); break;
                case "execution-abort": host.Insert(5, Incoming(20, "TestExecution.Abort", null)); break;
                case "execution-cleanup": Change(host, Send, "TestExecution.StatsChange", message => message["Payload"]!["NewTestResults"]![0]!["ErrorStackTrace"] = "at " + Method + "() in " + Code + ":line 10\nat Fixture.DisposeAsync()"); break;
                default: throw new InvalidOperationException("Unknown synthetic ownership receiving control.");
            }
        }

        internal CIS.Verification.HealthcheckMutationUnionModel.NativeAuditResult Audit()
        {
            var paths = new List<string>();
            void Add(string name, IEnumerable<string> lines) { string path = System.IO.Path.Combine(temporary.Path, name); File.WriteAllLines(path, lines); paths.Add(path); }
            Add("Runner-log.txt", runner); Add("Runner-log.host.synthetic.txt", host);
            if (concatenate)
            {
                Add("TestDiscoverer-log.txt", controllerA.Concat(controllerB));
            }
            else { Add("Discovery-A.txt", controllerA); Add("Discovery-B.txt", controllerB); }
            if (duplicatePhysicalController)
            {
                Add("Duplicate-discovery-controller.txt", controllerA);
            }
            Add("Discovery-A-host.txt", discoveryA); Add("Discovery-B-host.txt", discoveryB);
            paths.Reverse();
            return HealthcheckMutationNativeAudit.Audit(report, config, paths, System.IO.Path.Combine(AppContext.BaseDirectory, "Microsoft.VisualStudio.TestPlatform.ObjectModel.dll"));
        }
        private static void Change(List<string> lines, string marker, string type, Action<JsonObject> change)
        {
            int index = lines.FindIndex(line => line.Contains(marker, StringComparison.Ordinal) && line.Contains("\"MessageType\":\"" + type + "\"", StringComparison.Ordinal));
            Assert.IsGreaterThanOrEqualTo(0, index);
            int start = lines[index].IndexOf(marker, StringComparison.Ordinal) + marker.Length; JsonObject message = JsonNode.Parse(lines[index][start..])!.AsObject(); change(message); lines[index] = lines[index][..start] + message.ToJsonString();
        }
        public void Dispose() => temporary.Dispose();
    }
}
