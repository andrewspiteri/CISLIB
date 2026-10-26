using System.Text.Json.Nodes;
using System.Xml.Linq;
using CIS.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Verification.HealthcheckMutationUnionModel;

namespace CIS.Tests.ArchitectureTests;

public sealed partial class HealthcheckMutationNativeAuditTests
{
    [TestMethod]
    [DataRow("System.InvalidOperationException")]
    [DataRow("System.NullReferenceException")]
    [DataRow("System.AggregateException")]
    [DataRow("System.TimeoutException")]
    [DataRow("System.OperationCanceledException")]
    public void ProspectiveNativePolicyCreditsCompletedTestBodyExceptionsWithoutChangingTheLegacyDefault(string exception)
    {
        using var fixture = new TraceFixture();
        fixture.ChangeResult(result => result["ErrorMessage"] = "Test method " + Method + " threw exception:\r\n" + exception + ": application failure");
        string original = fixture.Report.ToJsonString();
        AssertRejected(fixture.Audit());

        NativeAuditResult audit = fixture.AuditWithPolicy(NativeAuditPolicy.CisNativeCompletedV1);
        CollectionAssert.AreEqual(new[] { "1" }, audit.NativeFailedKilledIds);
        CollectionAssert.AreEqual(new[] { "1" }, audit.QualifiedKilledIds);
        Assert.HasCount(0, audit.AssertionKilledIds);
        Assert.HasCount(0, audit.ReviewedSemanticKilledIds);
        Assert.HasCount(0, audit.Issues);
        Assert.AreEqual(1, audit.NormalHostCount);
        Assert.AreEqual("CisNativeCompletedV1", (string?)audit.Observations["outcomePolicy"]);
        Assert.AreEqual(original, fixture.Report.ToJsonString());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ProspectiveNativeFailedOutcomeDoesNotRequireAnAssertionMessageOrStackCatalog(bool absentDiagnostics)
    {
        using var fixture = new TraceFixture();
        fixture.ChangeResult(result =>
        {
            result["ErrorMessage"] = absentDiagnostics ? null : "Initialization method returned an unexpected application value.";
            result["ErrorStackTrace"] = absentDiagnostics ? null : "at Application.Worker.DisposeAsync()";
        });
        NativeAuditResult audit = fixture.AuditWithPolicy(NativeAuditPolicy.CisNativeCompletedV1);
        CollectionAssert.AreEqual(new[] { "1" }, audit.NativeFailedKilledIds);
        Assert.HasCount(0, audit.Issues);
    }

    [TestMethod]
    [DataRow("Initialization method Fixture.Initialize threw exception. System.Exception: setup.")]
    [DataRow("Class Initialization method Fixture.Initialize threw exception. System.Exception: setup.")]
    [DataRow("Assembly Initialization method Fixture.Initialize threw exception. System.Exception: setup. Aborting test execution.")]
    [DataRow("TestCleanup method Fixture.Cleanup threw exception. System.Exception: cleanup.")]
    [DataRow("Class Cleanup method Fixture.Cleanup failed. Error Message: cleanup. Stack Trace: frame")]
    [DataRow("Assembly Cleanup method Fixture.Cleanup failed. Error Message: cleanup. StackTrace: frame")]
    [DataRow("Error calling Test Cleanup method for test class Fixture: cleanup")]
    [DataRow("Test 'Direct' timed out after 15000ms")]
    [DataRow("Test 'Direct(\"can't\")' timed out after 15000ms")]
    [DataRow("Test 'Direct' was canceled")]
    [DataRow("Test cleanup method 'Fixture.Cleanup' timed out after 15000ms")]
    [DataRow("Global test initialize method 'Fixture.Initialize' was canceled")]
    [DataRow("Test method Fixture.Direct threw exception:\r\nSystem.Exception: body\r\nTestCleanup method Fixture.Cleanup threw exception. cleanup.")]
    public void ProspectiveNativePolicyStillHoldsActualFrameworkLifecycleDeadlineAndCancellationFailures(string message)
    {
        using var fixture = new TraceFixture();
        fixture.ChangeResult(result => result["ErrorMessage"] = message);
        NativeAuditResult audit = fixture.AuditWithPolicy(NativeAuditPolicy.CisNativeCompletedV1);
        AssertRejected(audit);
        Assert.IsTrue(audit.Issues.Any(issue => issue.Code == "NativeFrameworkLifecycleFailure"));
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public void ProspectiveNativePolicyHoldsKnownProcessGuardsWithTheirActualOrigin(bool capture, bool wrapped)
    {
        using var fixture = new TraceFixture();
        fixture.ChangeResult(result =>
        {
            result["ErrorMessage"] = "Test method " + Method + " threw exception:" + (wrapped ? " \r\n" : "\r\n")
                + (wrapped ? "System.AggregateException: One or more errors occurred. ---> " : "") + ProcessGuardMessage(capture);
            result["ErrorStackTrace"] = ProcessGuardFrame;
        });
        NativeAuditResult audit = fixture.AuditWithPolicy(NativeAuditPolicy.CisNativeCompletedV1);

        AssertRejected(audit);
        Assert.HasCount(0, audit.NativeFailedKilledIds);
        Assert.IsTrue(audit.Issues.Any(issue => issue.Code == "NativeFrameworkLifecycleFailure"));
    }

    [TestMethod]
    [DataRow(false, "origin-missing")]
    [DataRow(true, "origin-missing")]
    [DataRow(false, "message-different")]
    [DataRow(true, "message-different")]
    [DataRow(false, "product-type")]
    [DataRow(true, "product-type")]
    [DataRow(false, "wrapper-missing")]
    [DataRow(true, "wrapper-missing")]
    public void ProcessGuardWordsAndTypesCannotReplaceTheNativeGuardFailureOrigin(bool capture, string difference)
    {
        using var fixture = new TraceFixture();
        fixture.ChangeResult(result =>
        {
            string message = ProcessGuardMessage(capture);
            if (difference == "message-different") { message = message[..message.IndexOf(':')] + ": application failure"; }
            if (difference == "product-type") { message = "System.TimeoutException" + message[message.IndexOf(':')..]; }
            result["ErrorMessage"] = (difference == "wrapper-missing" ? "" : "Test method " + Method + " threw exception:\r\n") + message;
            result["ErrorStackTrace"] = difference == "origin-missing" ? "at Application.Worker.RunAsync()" : ProcessGuardFrame;
        });
        NativeAuditResult audit = fixture.AuditWithPolicy(NativeAuditPolicy.CisNativeCompletedV1);

        CollectionAssert.AreEqual(new[] { "1" }, audit.NativeFailedKilledIds);
        Assert.HasCount(0, audit.Issues);
    }

    private static string ProcessGuardMessage(bool capture) => capture
        ? "CIS.TestSupport.ProcessOutputLimitException: Child process output exceeded its capture limit; bounded head/tail diagnostics are available in Result."
        : "CIS.TestSupport.ProcessTimeoutException: Child process exceeded its 00:00:15 deadline: dotnet";

    private const string ProcessGuardFrame = "at CIS.TestSupport.ProcessRunner.RunAsync(String executable, IEnumerable`1 arguments, String workingDirectory, TimeSpan timeout, IReadOnlyDictionary`2 environment, Encoding outputEncoding, Int32 maximumCapturedBytesPerStream, Boolean allowTruncatedOutput, Nullable`1 maximumCapturedStandardOutputBytes) in /synthetic/ProcessRunner.cs:line 115";

    [TestMethod]
    [DataRow("raw-survived")]
    [DataRow("wrong-killedby")]
    [DataRow("passed-result")]
    [DataRow("stats-disagree")]
    [DataRow("wrong-result-origin")]
    [DataRow("canceled")]
    [DataRow("aborted")]
    [DataRow("actual-exit-failed")]
    [DataRow("active-test")]
    [DataRow("incomplete-result-set")]
    public void ProspectiveNativePolicyPreservesNativeOwnershipCompletedExecutionAndCleanupHolds(string mode)
    {
        using var fixture = new TraceFixture(secondCase: mode == "incomplete-result-set");
        fixture.ChangeResult(result => result["ErrorMessage"] = "Test method " + Method + " threw exception:\r\nSystem.InvalidOperationException: body");
        switch (mode)
        {
            case "raw-survived": fixture.Definition["status"] = "Survived"; break;
            case "wrong-killedby": fixture.Definition["killedBy"] = new JsonArray(SecondGuid); break;
            case "passed-result": fixture.ChangeResult(result => result["Outcome"] = 1); fixture.ChangeCompletion(args => args["TestRunStatistics"]!["Stats"] = new JsonObject { ["Passed"] = 1 }); break;
            case "stats-disagree": fixture.ChangeCompletion(args => args["TestRunStatistics"]!["Stats"]!["Failed"] = 0); break;
            case "wrong-result-origin": fixture.ChangeResult(result => result["TestCase"]!["CodeFilePath"] = "/wrong/Fixture.cs"); break;
            case "canceled": fixture.ChangeCompletion(args => args["IsCanceled"] = true); break;
            case "aborted": fixture.ChangeCompletion(args => args["IsAborted"] = true); break;
            case "actual-exit-failed": fixture.Controller[3] = fixture.Controller[3].Replace("exitcode: 0", "exitcode: 1", StringComparison.Ordinal); break;
            case "active-test": fixture.ChangeHostMessage("TestExecution.Completed", message => message["Payload"]!["LastRunTests"]!["ActiveTests"]!.AsArray().Add(TestCase(FirstGuid))); break;
            case "incomplete-result-set":
                fixture.ChangeHostMessage("TestExecution.StatsChange", message => message["Payload"]!["NewTestResults"]!.AsArray().RemoveAt(1));
                fixture.ChangeCompletion(args => args["TestRunStatistics"] = new JsonObject { ["ExecutedTests"] = 1, ["Stats"] = new JsonObject { ["Failed"] = 1 } });
                break;
            default: throw new InvalidOperationException("Unknown prospective native receiving control.");
        }
        AssertRejected(fixture.AuditWithPolicy(NativeAuditPolicy.CisNativeCompletedV1));
    }

    [TestMethod]
    public void MultipleCompletedNativeFailedGuidsCountTheirExactMutantOnce()
    {
        using var fixture = new TraceFixture(secondCase: true, secondFails: true);
        fixture.Definition["killedBy"] = new JsonArray(FirstGuid, SecondGuid);
        fixture.Report["files"]!["/synthetic/Core.cs"]!["mutants"]!.AsArray().RemoveAt(1);
        fixture.ChangeSettings(settings =>
        {
            XDocument document = XDocument.Parse(settings);
            XElement[] selectors = document.Descendants("Mutant").ToArray();
            selectors[0].SetAttributeValue("tests", FirstGuid + "," + SecondGuid);
            selectors[1].Remove();
            return document.ToString(SaveOptions.DisableFormatting);
        });
        NativeAuditResult audit = fixture.AuditWithPolicy(NativeAuditPolicy.CisNativeCompletedV1);
        CollectionAssert.AreEqual(new[] { "1" }, audit.QualifiedKilledIds);
        Assert.HasCount(0, audit.Issues);
        Assert.AreEqual(2, (int?)audit.Observations["nativeFailedWitnessCount"]);
        Assert.AreEqual(1, (int?)audit.Observations["nativeFailedKilledDefinitionCount"]);
    }

    private sealed partial class TraceFixture
    {
        internal NativeAuditResult AuditWithPolicy(NativeAuditPolicy policy)
        {
            string controller = System.IO.Path.Combine(temporary.Path, "Runner 0-log.txt");
            string host = System.IO.Path.Combine(temporary.Path, "Runner 0-log.host.synthetic.txt");
            File.WriteAllLines(controller, Controller); File.WriteAllLines(host, Host);
            return HealthcheckMutationNativeAudit.Audit(Report, Configuration, [controller, host],
                System.IO.Path.Combine(AppContext.BaseDirectory, "Microsoft.VisualStudio.TestPlatform.ObjectModel.dll"), policy: policy);
        }
    }
}
