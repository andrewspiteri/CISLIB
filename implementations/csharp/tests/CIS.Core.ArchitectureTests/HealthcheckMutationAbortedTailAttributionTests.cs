using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using CIS.TestSupport;
using CIS.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Verification.HealthcheckMutationNativeAudit;
using static CIS.Verification.HealthcheckMutationUnionModel;

namespace CIS.Tests.ArchitectureTests;

// Synthetic structured receiving records are
// never represented as native subject executions or actual gate evidence.
[TestClass]
public sealed class HealthcheckMutationAbortedTailAttributionTests
{
    [TestMethod]
    public void ExactReviewedAbortedTailHasOneOwnedIssueAndNeverAddsANormalHostOrKill()
    {
        using var fixture = new Trace();
        NativeAuditResult result = fixture.Audit();
        Assert.HasCount(0, result.AssertionKilledIds);
        Assert.HasCount(0, result.ReviewedSemanticKilledIds);
        Assert.HasCount(0, result.QualifiedKilledIds);
        Assert.AreEqual(1, result.HostCount);
        Assert.AreEqual(0, result.NormalHostCount);
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == "NativeAbortedResultTail" && issue.MutantIds.SequenceEqual(["1904"])));
        Assert.IsFalse(result.Issues.Any(issue => issue.MutantIds.Length == 0));
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == "NativeAbortOrCancel"));
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == "NativeCompletionFailure"));
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == "NativeResultSetIncomplete"));
    }

    [TestMethod]
    public void EveryBeforeFailureAndLateExtraResultIsRetainedWithoutRewritingTheRawReport()
    {
        using var fixture = new Trace();
        string original = fixture.Report.ToJsonString();
        NativeAuditResult result = fixture.Audit();
        JsonArray tails = result.Observations["reviewedUnqualifiedAbortedTails"]!.AsArray();
        Assert.HasCount(1, tails);
        JsonObject tail = tails[0]!.AsObject();
        Assert.HasCount(3, tail["preCompletedResults"]!.AsArray());
        Assert.HasCount(1, tail["lateResults"]!.AsArray());
        CollectionAssert.AreEquivalent(new[] { First, Second }, tail["rawKilledByGuids"]!.AsArray().Select(value => value!.GetValue<string>()).ToArray());
        Assert.AreEqual(Late, (string?)tail["lateResults"]![0]!["guid"]);
        Assert.AreEqual("Actual retained late TimeoutException", (string?)tail["lateResults"]![0]!["message"]);
        Assert.AreEqual(Active, (string?)tail["lateActiveGuids"]![0]);
        Assert.AreEqual(0, (int?)tail["normalHostIncrement"]);
        Assert.AreEqual(0, (int?)tail["killCredit"]);
        Assert.AreEqual(original, fixture.Report.ToJsonString());
    }

    [TestMethod]
    public void TheOrdinaryEntryPointDoesNotAcceptAReceivingFixtureAsTheHistoricalReviewedCase()
    {
        using var fixture = new Trace();
        NativeAuditResult result = fixture.Audit(useFixedCatalog: true);
        Assert.HasCount(0, result.QualifiedKilledIds);
        Assert.AreEqual(0, result.NormalHostCount);
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == "NativeHostMalformed" && issue.MutantIds.Length == 0));
        Assert.HasCount(0, result.Observations["reviewedUnqualifiedAbortedTails"]!.AsArray());
    }

    [TestMethod]
    [DataRow("wrong-owner")]
    [DataRow("multiple-owners")]
    [DataRow("foreign-late-guid")]
    [DataRow("duplicate-late-guid")]
    [DataRow("wrong-late-method")]
    [DataRow("wrong-late-source")]
    [DataRow("nonaborted-completion")]
    [DataRow("canceled-completion")]
    [DataRow("error-completion")]
    [DataRow("wrong-completed-count")]
    [DataRow("wrong-late-count")]
    [DataRow("terminate-before-late")]
    [DataRow("host-abort-missing")]
    [DataRow("parent-timeout-missing")]
    [DataRow("parent-budget-mismatch")]
    [DataRow("actual-os-exit-missing")]
    [DataRow("actual-os-exit-nonzero")]
    [DataRow("unknown-tail-message")]
    [DataRow("second-late-stats")]
    [DataRow("second-completed")]
    [DataRow("late-active-already-completed")]
    [DataRow("wrong-source-identity")]
    [DataRow("wrong-original-report")]
    [DataRow("unbound-original-host-hash")]
    [DataRow("duplicate-protocol")]
    [DataRow("duplicate-initialization")]
    [DataRow("unknown-received-message")]
    [DataRow("wrong-parent")]
    [DataRow("wrong-runtime")]
    [DataRow("unknown-controller-message")]
    [DataRow("parent-abort-send-missing")]
    public void DerivativeHashRebindingCannotApproveWrongOwnershipMalformedResultsOrMissingTerminalEvidence(string variant)
    {
        using var fixture = new Trace();
        fixture.Change(variant);
        NativeAuditResult result = fixture.Audit(variant);
        Assert.HasCount(0, result.QualifiedKilledIds);
        Assert.AreEqual(0, result.NormalHostCount);
        Assert.IsTrue(result.Issues.Any(issue => issue.MutantIds.Length == 0), "Unproved attribution must remain a global integrity failure.");
        Assert.HasCount(0, result.Observations["reviewedUnqualifiedAbortedTails"]!.AsArray());
    }

    private const string First = "11111111-1111-4111-8111-111111111111";
    private const string Second = "22222222-2222-4222-8222-222222222222";
    private const string Passed = "33333333-3333-4333-8333-333333333333";
    private const string Late = "44444444-4444-4444-8444-444444444444";
    private const string Active = "55555555-5555-4555-8555-555555555555";
    private const string Foreign = "66666666-6666-4666-8666-666666666666";
    private const string Method = "CIS.Tests.UnitTests.Healthcheck.AbortedTailFixture.Receiving";
    private const string Code = "/synthetic/implementations/csharp/tests/CIS.Core.UnitTests/Healthcheck/Fixture.cs";
    private const string Dll = "/synthetic/implementations/csharp/tests/CIS.Core.UnitTests/bin/Release/net10.0/CIS.Core.UnitTests.dll";
    private const string Filter = "FullyQualifiedName~CIS.Tests.UnitTests.Healthcheck.";
    private const string Receive = "TestRequestHandler.OnMessageReceived: received message: ";
    private const string Send = "TestRequestHandler.SendData: sending data from testhost: ";
    private const string Request = "TestExecution.GetTestRunnerProcessStartInfoForRunSelected";
    private static readonly string[] Guids = [First, Second, Passed, Late, Active];

    private static JsonObject Test(string guid) => new() { ["Id"] = guid, ["FullyQualifiedName"] = Method, ["Source"] = Dll, ["CodeFilePath"] = Code };
    private static JsonObject Result(string guid, bool passed = false) => new()
    {
        ["TestCase"] = Test(guid),
        ["Outcome"] = passed ? 1 : 2,
        ["ErrorMessage"] = passed ? null : guid == Late ? "Actual retained late TimeoutException" : "Assertion failed. Expected Reserved; observed Retired.",
        ["ErrorStackTrace"] = passed ? null : "at " + Method + "() in " + Code + ":line 34"
    };
    private static JsonObject Stats(int failed, int passed) => new()
    { ["ExecutedTests"] = failed + passed, ["Stats"] = new JsonObject { ["Failed"] = failed, ["Passed"] = passed } };
    private static string Message(string type, JsonNode? payload) => new JsonObject { ["MessageType"] = type, ["Payload"] = payload }.ToJsonString();
    private static string Line(bool parent, int clock, string text) => "TpTrace Information: 0 : " + (parent ? "10" : "20")
        + ", 1, synthetic, " + clock.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", " + (parent ? "vstest.console.dll, " : "testhost.dll, ") + text;
    private static string Host(int clock, string marker, string type, JsonNode? payload) => Line(false, clock, marker + Message(type, payload));
    private static string Controller(int clock, string type, JsonNode? payload) => Line(true, clock,
        "DesignModeClient.ProcessRequests: Processing Message: (" + type + ") -> " + Message(type, payload));
    private static string Settings() => new XElement("RunSettings",
        new XElement("RunConfiguration", new XElement("TargetFrameworkVersion", "net10.0"), new XElement("TestSessionTimeout", 24152), new XElement("TestCaseFilter", Filter)),
        new XElement("InProcDataCollectionRunSettings", new XElement("InProcDataCollectors", new XElement("InProcDataCollector", new XElement("Configuration", new XElement("Parameters",
            new XElement("Mutant", new XAttribute("id", "1904"), new XAttribute("tests", string.Join(',', Guids))),
            new XElement("MutantControl", new XAttribute("name", "Synthetic.MutantControl")))))))).ToString(SaveOptions.DisableFormatting);

    private sealed class Trace : IDisposable
    {
        private readonly TemporaryDirectory temporary = new();
        internal readonly List<string> ControllerLines;
        internal readonly List<string> HostLines;
        internal readonly JsonObject Report;
        private readonly JsonObject config;
        private readonly Definition definition;
        private readonly string reportHash = new('a', 64);
        private readonly string initialHostHash;

        internal Trace()
        {
            const string source = "false";
            string sourceHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
            definition = new Definition("1904", "Healthcheck/Fixture.cs", sourceHash, 0, 5, "Boolean mutation", "false", "true", "Killed", [First, Second]);
            Report = new JsonObject
            {
                ["testFiles"] = new JsonObject { [Code] = new JsonObject { ["tests"] = new JsonArray(Guids.Select(guid => (JsonNode)new JsonObject { ["id"] = guid, ["name"] = Method }).ToArray()) } },
                ["files"] = new JsonObject
                {
                    ["/synthetic/Core.cs"] = new JsonObject
                    {
                        ["source"] = source,
                        ["mutants"] = new JsonArray(
                    new JsonObject { ["id"] = "1904", ["status"] = "Killed", ["killedBy"] = new JsonArray(First, Second), ["coveredBy"] = new JsonArray(Guids.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()) },
                    new JsonObject { ["id"] = "1905", ["status"] = "Survived", ["killedBy"] = new JsonArray() })
                    }
                }
            };
            config = new JsonObject
            {
                ["stryker-config"] = new JsonObject
                {
                    ["target-framework"] = "net10.0",
                    ["configuration"] = "Release",
                    ["test-case-filter"] = Filter,
                    ["test-projects"] = new JsonArray("../../tests/CIS.Core.UnitTests/CIS.Core.UnitTests.csproj")
                }
            };
            JsonArray tests = new(Guids.Select(guid => (JsonNode)Test(guid)).ToArray());
            string settings = Settings();
            ControllerLines =
            [
                Line(true, 1, "Synthetic controller"), Controller(10, Request, new JsonObject { ["TestCases"] = tests.DeepClone(), ["RunSettings"] = settings }),
                Controller(20, "TestExecution.CustomTestHostLaunchCallback", new JsonObject { ["HostProcessId"] = 20, ["ErrorMessage"] = null }),
                Line(true, 100, "TestRunRequest.OnTestSessionTimeout: calling cancellation as test run exceeded testSessionTimeout 24152 milliseconds"),
                Line(true, 110, "TestRequestSender.SendTestRunAbort: Sending test run abort."),
                Line(true, 200, "TestHostManagerCallbacks.ExitCallBack: Testhost processId: 20 exited with exitcode: 0 error: ''")
            ];
            HostLines =
            [
                Line(false, 30, "Synthetic host"), Line(false, 31, "Runtime location: /synthetic/shared/Microsoft.NETCore.App/10.0.12"),
                Line(false, 32, "Testhost process started with args :[--parentprocessid, 10]"), Host(33, Receive, "ProtocolVersion", JsonValue.Create(7)),
                Host(34, Receive, "TestExecution.Initialize", new JsonArray(Dll)),
                Host(40, Receive, "TestExecution.StartWithTests", new JsonObject { ["Tests"] = tests.DeepClone(), ["RunSettings"] = settings }),
                Host(50, Send, "TestExecution.StatsChange", new JsonObject { ["NewTestResults"] = new JsonArray(Result(First), Result(Second), Result(Passed, true)), ["TestRunStatistics"] = Stats(2, 1), ["ActiveTests"] = new JsonArray(Test(Late)) }),
                Host(120, Receive, "TestExecution.Abort", null),
                Host(130, Send, "TestExecution.Completed", new JsonObject { ["TestRunCompleteArgs"] = new JsonObject
                    { ["IsAborted"] = true, ["IsCanceled"] = false, ["Error"] = null, ["TestRunStatistics"] = Stats(2, 1) },
                    ["LastRunTests"] = new JsonObject { ["NewTestResults"] = new JsonArray(), ["ActiveTests"] = new JsonArray(), ["TestRunStatistics"] = Stats(2, 1) } }),
                Host(140, Send, "TestExecution.StatsChange", new JsonObject { ["NewTestResults"] = new JsonArray(Result(Late)), ["TestRunStatistics"] = Stats(3, 1), ["ActiveTests"] = new JsonArray(Test(Active)) }),
                Host(150, Receive, "TestSession.Terminate", null), Line(false, 160, "Testhost process exiting.")
            ];
            Write(); initialHostHash = Hash(Path.Combine(temporary.Path, "Runner 0-log.host.synthetic.txt"));
        }

        internal NativeAuditResult Audit(string? variant = null, bool useFixedCatalog = false)
        {
            Write(); string parent = Path.Combine(temporary.Path, "Runner 0-log.txt"), host = Path.Combine(temporary.Path, "Runner 0-log.host.synthetic.txt");
            string model = Path.Combine(AppContext.BaseDirectory, "Microsoft.VisualStudio.TestPlatform.ObjectModel.dll");
            if (useFixedCatalog) { return HealthcheckMutationNativeAudit.Audit(Report, config, [parent, host], model, reportHash, [definition]); }
            var review = new AbortedTailCase(variant == "wrong-original-report" ? new string('b', 64) : reportHash,
                variant == "wrong-source-identity" ? new string('b', 64) : definition.Identity, "1904",
                variant == "unbound-original-host-hash" ? initialHostHash : Hash(host), Hash(parent), 5, 3, 2);
            return AuditAbortedTailReceivingCase(Report, config, [parent, host], model, reportHash, [definition], review);
        }

        internal void Change(string variant)
        {
            switch (variant)
            {
                case "wrong-owner": ChangeSettings(value => value.Replace("id=\"1904\"", "id=\"1905\"", StringComparison.Ordinal)); break;
                case "multiple-owners": ChangeSettings(value => value.Replace("</Parameters>", "<Mutant id=\"1905\" tests=\"" + Active + "\"/></Parameters>", StringComparison.Ordinal).Replace(string.Join(',', Guids), string.Join(',', Guids.Take(4)), StringComparison.Ordinal)); break;
                case "foreign-late-guid": LateResult(value => value["TestCase"]!["Id"] = Foreign); break;
                case "duplicate-late-guid": LateResult(value => value["TestCase"]!["Id"] = First); break;
                case "wrong-late-method": LateResult(value => value["TestCase"]!["FullyQualifiedName"] = Method + "Other"); break;
                case "wrong-late-source": LateResult(value => value["TestCase"]!["Source"] = "/foreign/CIS.Core.UnitTests.dll"); break;
                case "nonaborted-completion": Complete(value => value["IsAborted"] = false); break;
                case "canceled-completion": Complete(value => value["IsCanceled"] = true); break;
                case "error-completion": Complete(value => value["Error"] = "Actual infrastructure error"); break;
                case "wrong-completed-count": Complete(value => value["TestRunStatistics"]!["ExecutedTests"] = 4); break;
                case "wrong-late-count": ChangeLastStats(value => value["Payload"]!["TestRunStatistics"]!["ExecutedTests"] = 5); break;
                case "terminate-before-late": string terminal = HostLines[10]; HostLines.RemoveAt(10); HostLines.Insert(9, terminal); break;
                case "host-abort-missing": HostLines.RemoveAt(7); break;
                case "parent-timeout-missing": ControllerLines.RemoveAt(3); break;
                case "parent-budget-mismatch": ControllerLines[3] = ControllerLines[3].Replace("24152", "24153", StringComparison.Ordinal); break;
                case "actual-os-exit-missing": ControllerLines.RemoveAt(5); break;
                case "actual-os-exit-nonzero": ControllerLines[5] = ControllerLines[5].Replace("exitcode: 0", "exitcode: 134", StringComparison.Ordinal); break;
                case "unknown-tail-message": HostLines.Insert(10, Host(145, Send, "Unexpected.Execution.Message", new JsonObject())); break;
                case "second-late-stats": HostLines.Insert(10, HostLines[9]); break;
                case "second-completed": HostLines.Insert(10, HostLines[8]); break;
                case "late-active-already-completed": ChangeLastStats(value => value["Payload"]!["ActiveTests"]![0] = Test(First)); break;
                case "wrong-source-identity": case "wrong-original-report": break;
                case "unbound-original-host-hash": HostLines.Insert(10, Line(false, 145, "Different retained body")); break;
                case "duplicate-protocol": HostLines.Insert(4, HostLines[3]); break;
                case "duplicate-initialization": HostLines.Insert(5, HostLines[4]); break;
                case "unknown-received-message": HostLines.Insert(10, Host(145, Receive, "Unexpected.Execution.Message", null)); break;
                case "wrong-parent": HostLines[2] = HostLines[2].Replace("[--parentprocessid, 10]", "[--parentprocessid, 99]", StringComparison.Ordinal); break;
                case "wrong-runtime": HostLines[1] = HostLines[1].Replace("10.0.12", "10.0.13", StringComparison.Ordinal); break;
                case "unknown-controller-message": ControllerLines.Insert(5, Controller(115, "Unexpected.Execution.Message", new JsonObject())); break;
                case "parent-abort-send-missing": ControllerLines.RemoveAt(4); break;
                default: throw new InvalidOperationException("Unknown receiving control.");
            }
        }

        private void LateResult(Action<JsonObject> change) => ChangeLastStats(value => change(value["Payload"]!["NewTestResults"]![0]!.AsObject()));
        private void Complete(Action<JsonObject> change) => ChangeMessage(HostLines, "TestExecution.Completed", value => change(value["Payload"]!["TestRunCompleteArgs"]!.AsObject()));
        private void ChangeLastStats(Action<JsonObject> change) => ChangeMessage(HostLines, "TestExecution.StatsChange", change, last: true);
        private void ChangeSettings(Func<string, string> change)
        {
            ChangeMessage(ControllerLines, Request, value => value["Payload"]!["RunSettings"] = change((string)value["Payload"]!["RunSettings"]!));
            ChangeMessage(HostLines, "TestExecution.StartWithTests", value => value["Payload"]!["RunSettings"] = change((string)value["Payload"]!["RunSettings"]!));
        }
        private static void ChangeMessage(List<string> lines, string type, Action<JsonObject> change, bool last = false)
        {
            string marker = "\"MessageType\":\"" + type + "\"";
            int index = last ? lines.FindLastIndex(value => value.Contains(marker, StringComparison.Ordinal)) : lines.FindIndex(value => value.Contains(marker, StringComparison.Ordinal));
            int json = lines[index].IndexOf('{'); JsonObject value = JsonNode.Parse(lines[index][json..])!.AsObject(); change(value);
            lines[index] = lines[index][..json] + value.ToJsonString();
        }
        private void Write()
        {
            File.WriteAllLines(Path.Combine(temporary.Path, "Runner 0-log.txt"), ControllerLines);
            File.WriteAllLines(Path.Combine(temporary.Path, "Runner 0-log.host.synthetic.txt"), HostLines);
        }
        private static string Hash(string path)
        { using FileStream input = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(input)); }
        public void Dispose() => temporary.Dispose();
    }
}
