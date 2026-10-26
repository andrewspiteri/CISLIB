using System.Globalization;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using CIS.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Verification.HealthcheckMutationUnionModel;

namespace CIS.Tests.ArchitectureTests;

public sealed partial class HealthcheckMutationNativeAuditTests
{
    [TestMethod]
    [DataRow(false, false, false)]
    [DataRow(true, false, false)]
    [DataRow(false, true, false)]
    [DataRow(false, false, true)]
    public void ProspectiveNativeEarlyBailRequiresOriginalEngineIntentAndCompletedFailureBeforeCancellation(bool reverse, bool recycled, bool windowsController)
    {
        using var fixture = new TraceFixture(secondCase: true);
        fixture.PrepareNativeBail();
        if (windowsController)
        {
            for (int index = 0; index < fixture.Controller.Count; index++)
            {
                fixture.Controller[index] = fixture.Controller[index].Replace(", vstest.console.dll,", ", vstest.console.exe,", StringComparison.Ordinal);
            }
            // These independent native clocks were observed in the actual
            // Windows control: engine cue can timestamp just after controller
            // receipt, still before this subject's uniquely joined cancel.
            fixture.Engine[1] = fixture.Engine[1].Replace("10:00:02.0010000", "10:00:02.0025000", StringComparison.Ordinal);
        }
        string original = fixture.Report.ToJsonString();
        AssertRejected(fixture.AuditWithEngine(NativeAuditPolicy.LegacySourceProofs));

        NativeAuditResult audit = fixture.AuditWithEngine(NativeAuditPolicy.CisNativeCompletedV1, reverse, recycled);
        Assert.HasCount(0, audit.Issues);
        CollectionAssert.AreEqual(new[] { "1" }, audit.NativeFailedKilledIds);
        Assert.AreEqual(1, audit.NormalHostCount);
        Assert.AreEqual(1, (int?)audit.Observations["nativeEarlyBailWitnessCount"]);
        JsonNode witness = audit.Observations["nativeEarlyBailWitnesses"]![0]!;
        Assert.AreEqual(2, (int?)witness["selectedTestCount"]);
        Assert.AreEqual(1, (int?)witness["completedResultCount"]);
        Assert.AreEqual(1, (int?)witness["unexecutedSelectedTestCount"]);
        Assert.AreEqual(original, fixture.Report.ToJsonString());
    }

    [TestMethod]
    [DataRow("no-engine")]
    [DataRow("no-cue")]
    [DataRow("wrong-runner")]
    [DataRow("wrong-engine")]
    [DataRow("duplicate-start")]
    [DataRow("duplicate-cue")]
    [DataRow("cue-before-failure")]
    [DataRow("cue-after-cancel")]
    [DataRow("cue-malformed-clock")]
    [DataRow("controller-clock-missing")]
    [DataRow("host-clock-missing")]
    [DataRow("bail-disabled")]
    [DataRow("bail-unspecified")]
    [DataRow("bail-string")]
    [DataRow("cancel-warning")]
    [DataRow("retry")]
    [DataRow("power-saving")]
    [DataRow("vstest-failed")]
    [DataRow("controller-cancel-missing")]
    [DataRow("controller-cancel-duplicate")]
    [DataRow("host-cancel-missing")]
    [DataRow("host-cancel-duplicate")]
    [DataRow("failure-after-cancel")]
    [DataRow("unfinished-active-test")]
    [DataRow("unrecognized-active-test")]
    [DataRow("framework-cleanup-failure")]
    [DataRow("second-selector-unresolved")]
    [DataRow("aborted")]
    [DataRow("completion-error")]
    [DataRow("active-at-completion")]
    [DataRow("stats-disagree")]
    [DataRow("no-terminate")]
    [DataRow("bad-exit")]
    public void IntentionalBailNeverConvertsMissingAmbiguousRetriedOrUnfinishedExecutionIntoKillCredit(string mode)
    {
        using var fixture = new TraceFixture(secondCase: true);
        fixture.PrepareNativeBail();
        switch (mode)
        {
            case "no-engine": fixture.Engine.Clear(); break;
            case "no-cue": fixture.Engine.RemoveAt(1); break;
            case "wrong-runner": fixture.Engine[1] = fixture.Engine[1].Replace("Runner 0", "Runner 1", StringComparison.Ordinal); break;
            case "wrong-engine": fixture.Engine[0] = fixture.Engine[0].Replace("4.16.0", "4.17.0", StringComparison.Ordinal); break;
            case "duplicate-start": fixture.Engine.Add(fixture.Engine[0]); break;
            case "duplicate-cue": fixture.Engine.Add(fixture.Engine[1]); break;
            case "cue-before-failure": fixture.Engine[1] = fixture.Engine[1].Replace("10:00:02.0010000", "10:00:01.9990000", StringComparison.Ordinal); break;
            case "cue-after-cancel": fixture.Engine[1] = fixture.Engine[1].Replace("10:00:02.0010000", "10:00:02.0040000", StringComparison.Ordinal); break;
            case "cue-malformed-clock": fixture.Engine[1] = fixture.Engine[1].Replace("+02:00", "", StringComparison.Ordinal); break;
            case "controller-clock-missing": fixture.Controller[1] = fixture.Controller[1].Replace("2026/10/26", "synthetic", StringComparison.Ordinal); break;
            case "host-clock-missing": fixture.Host[4] = fixture.Host[4].Replace("2026/10/26", "synthetic", StringComparison.Ordinal); break;
            case "bail-disabled": fixture.Configuration["stryker-config"]!["disable-bail"] = true; break;
            case "bail-unspecified": fixture.Configuration["stryker-config"]!.AsObject().Remove("disable-bail"); break;
            case "bail-string": fixture.Configuration["stryker-config"]!["disable-bail"] = "false"; break;
            case "cancel-warning": fixture.Engine.Add(EngineLine("10:00:02.0100000", "Error while cancelling VsTest session.", "WRN")); break;
            case "retry": fixture.Engine.Add(EngineLine("10:00:02.0100000", "\"Runner 0\": Retrying the test session.", "INF")); break;
            case "power-saving": fixture.Engine.Add(EngineLine("10:00:02.0100000", "\"Runner 0\": Rerun of the test session because computer entered power saving mode.", "WRN")); break;
            case "vstest-failed": fixture.Engine.Add(EngineLine("10:00:02.0100000", "\"Runner 0\": VsTest failed 3 times, settings: x", "WRN")); break;
            case "controller-cancel-missing": fixture.Controller.RemoveAt(3); break;
            case "controller-cancel-duplicate": fixture.Controller.Insert(3, fixture.Controller[3]); break;
            case "host-cancel-missing": fixture.RemoveHostMessage("TestExecution.Cancel"); break;
            case "host-cancel-duplicate": fixture.Host.Insert(5, fixture.Host[5]); break;
            case "failure-after-cancel": (fixture.Host[4], fixture.Host[5]) = (fixture.Host[5], fixture.Host[4]); break;
            case "unfinished-active-test": fixture.ChangeHostMessage("TestExecution.StatsChange", message => message["Payload"]!["ActiveTests"]!.AsArray().Add(TestCase(SecondGuid))); break;
            case "unrecognized-active-test": fixture.ChangeHostMessage("TestExecution.StatsChange", message => message["Payload"]!["ActiveTests"]!.AsArray().Add(TestCase("33333333-3333-3333-3333-333333333333"))); break;
            case "framework-cleanup-failure": fixture.ChangeResult(result => result["ErrorMessage"] = "TestCleanup method Fixture.Cleanup threw exception. System.Exception: cleanup."); break;
            case "second-selector-unresolved":
                fixture.Report["files"]!["/synthetic/Core.cs"]!["mutants"]!.AsArray().Add(new JsonObject { ["id"] = "2", ["status"] = "Killed", ["killedBy"] = new JsonArray(SecondGuid) });
                fixture.ChangeSettings(_ => Settings([(FirstGuid, "1"), (SecondGuid, "2")]));
                break;
            case "aborted": fixture.Host.Insert(6, HostReceive("TestExecution.Abort", null)); fixture.ChangeCompletion(args => args["IsAborted"] = true); break;
            case "completion-error": fixture.ChangeCompletion(args => args["Error"] = "native error"); break;
            case "active-at-completion": fixture.ChangeHostMessage("TestExecution.Completed", message => message["Payload"]!["LastRunTests"]!["ActiveTests"]!.AsArray().Add(TestCase(SecondGuid))); break;
            case "stats-disagree": fixture.ChangeCompletion(args => args["TestRunStatistics"]!["Stats"]!["Failed"] = 0); break;
            case "no-terminate": fixture.RemoveHostMessage("TestSession.Terminate"); break;
            case "bad-exit": fixture.Controller[^1] = fixture.Controller[^1].Replace("exitcode: 0", "exitcode: 134", StringComparison.Ordinal); break;
            default: throw new InvalidOperationException("Unknown native early-bail receiving control.");
        }
        NativeAuditResult audit = fixture.AuditWithEngine(NativeAuditPolicy.CisNativeCompletedV1);
        AssertRejected(audit);
        Assert.AreEqual(0, (int?)audit.Observations["nativeEarlyBailWitnessCount"]);
    }

    private static string EngineLine(string time, string message, string level = "DBG") =>
        "2026-10-26T" + time + "+02:00  [" + level + "] " + message + " (0123abcd)";

    private sealed partial class TraceFixture
    {
        internal List<string> Engine { get; } = [];

        internal void PrepareNativeBail()
        {
            Configuration["stryker-config"]!["disable-bail"] = false;
            Report["files"]!["/synthetic/Core.cs"]!["mutants"]!.AsArray().RemoveAt(1);
            ChangeSettings(settings =>
            {
                XDocument document = XDocument.Parse(settings);
                XElement[] selectors = document.Descendants("Mutant").ToArray();
                selectors[0].SetAttributeValue("tests", FirstGuid + "," + SecondGuid);
                selectors[1].Remove();
                return document.ToString(SaveOptions.DisableFormatting);
            });
            ChangeHostMessage("TestExecution.StatsChange", message =>
            {
                message["Payload"]!["NewTestResults"]!.AsArray().RemoveAt(1);
                message["Payload"]!["ActiveTests"] = new JsonArray();
            });
            ChangeResult(result => result["ErrorMessage"] = "Test method " + Method + " threw exception:\r\nSystem.ArgumentOutOfRangeException: application guard");
            ChangeCompletion(args =>
            {
                args["IsCanceled"] = true;
                args["TestRunStatistics"] = new JsonObject { ["ExecutedTests"] = 1, ["Stats"] = new JsonObject { ["Failed"] = 1 } };
            });
            Controller.Insert(3, ControllerMessage("TestExecution.Cancel", null));
            Host.Insert(5, HostReceive("TestExecution.Cancel", null));
            string[] controllerTimes = ["00.000", "00.100", "00.200", "02.002", "02.500"];
            string[] hostTimes = ["00.250", "00.251", "00.252", "00.300", "02.000", "02.003", "02.100", "02.200", "02.300"];
            Stamp(Controller, controllerTimes); Stamp(Host, hostTimes);
            Engine.Add(EngineLine("09:59:59.0000000", "Stryker starting, version: \"4.16.0\""));
            Engine.Add(EngineLine("10:00:02.0010000", "\"Runner 0\": Each mutant's fate has been established, we can stop."));
        }

        private static void Stamp(List<string> lines, string[] times)
        {
            for (int index = 0; index < lines.Count; index++)
            {
                lines[index] = lines[index].Replace(", synthetic,", ", 2026/10/26, 10:00:" + times[index] + ", " + (1000 + index).ToString(CultureInfo.InvariantCulture) + ",", StringComparison.Ordinal);
            }
        }

        internal NativeAuditResult AuditWithEngine(NativeAuditPolicy policy, bool reverse = false, bool recycled = false)
        {
            string controller = System.IO.Path.Combine(temporary.Path, recycled ? "Runner 0-2-log.txt" : "Runner 0-log.txt");
            string host = System.IO.Path.Combine(temporary.Path, "Runner 0-log.host.synthetic.txt");
            string engine = System.IO.Path.Combine(temporary.Path, "log-20261026.txt");
            File.WriteAllLines(controller, Controller); File.WriteAllLines(host, Host); File.WriteAllLines(engine, Engine);
            string[] paths = reverse ? [engine, host, controller] : [controller, host, engine];
            return HealthcheckMutationNativeAudit.Audit(Report, Configuration, paths,
                System.IO.Path.Combine(AppContext.BaseDirectory, "Microsoft.VisualStudio.TestPlatform.ObjectModel.dll"), policy: policy);
        }
    }
}
