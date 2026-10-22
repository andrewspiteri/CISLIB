using System.Text.Json.Nodes;
using System.Xml.Linq;
using CIS.TestSupport;
using CIS.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Verification.HealthcheckMutationUnionModel;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class HealthcheckMutationNativeAuditTests
{
    [TestMethod]
    [DataRow("AreEqual", false)]
    [DataRow("AreEqual", true)]
    [DataRow("AreEquivalent", false)]
    [DataRow("AreEquivalent", true)]
    public void SyntheticLegacyCollectionAssertionsRequireTheActualReceivingSourceAndAdapterBoundary(string operation, bool helper)
    {
        using var fixture = new TraceFixture();
        fixture.ChangeResult(result =>
        {
            result["ErrorMessage"] = "CollectionAssert." + operation + " failed. Different number of elements.";
            result["ErrorStackTrace"] = CollectionStack(helper);
        });
        NativeAuditResult audit = fixture.Audit();
        CollectionAssert.AreEqual(new[] { "1" }, audit.AssertionKilledIds);
        Assert.HasCount(0, audit.Issues);
    }

    [TestMethod]
    [DataRow("wrong-source")]
    [DataRow("wrong-class")]
    [DataRow("wrong-receiver")]
    [DataRow("adapter-missing")]
    [DataRow("adapter-wrong")]
    [DataRow("zero-line")]
    [DataRow("line-missing")]
    [DataRow("unsupported-operation")]
    [DataRow("embedded-prefix")]
    [DataRow("exception-wrapper")]
    [DataRow("timeout")]
    [DataRow("cancellation")]
    [DataRow("cleanup")]
    [DataRow("initialization")]
    public void CollectionAssertionTextCannotReplaceItsOriginOrQualifyCleanupAndDeadlineFailures(string mode)
    {
        using var fixture = new TraceFixture();
        fixture.ChangeResult(result =>
        {
            string message = "CollectionAssert.AreEqual failed. Different number of elements.", stack = CollectionStack(false);
            switch (mode)
            {
                case "wrong-source": stack = stack.Replace(CodeFile, "/other/Fixture.cs", StringComparison.Ordinal); break;
                case "wrong-class": stack = "at Other.Helper() in " + CodeFile + ":line 10\n" + stack; break;
                case "wrong-receiver": stack = stack.Replace(Method + "(", "Other.Receiving(", StringComparison.Ordinal); break;
                case "adapter-missing": stack = stack[..stack.LastIndexOf('\n')]; break;
                case "adapter-wrong": stack = stack.Replace("TestMethodInfo.ExecuteInternalAsync(", "Other.ExecuteInternalAsync(", StringComparison.Ordinal); break;
                case "zero-line": stack = stack.Replace(":line 10", ":line 0", StringComparison.Ordinal); break;
                case "line-missing": stack = stack.Replace(":line 10", "", StringComparison.Ordinal); break;
                case "unsupported-operation": message = message.Replace("AreEqual", "AreNotEqual", StringComparison.Ordinal); break;
                case "embedded-prefix": message = "Unreviewed text: " + message; break;
                case "exception-wrapper": message = "Test method " + Method + " threw exception:\r\nSystem.Exception: " + message; break;
                case "timeout": message += " System.TimeoutException"; break;
                case "cancellation": message += " System.OperationCanceledException"; break;
                case "cleanup": stack = "at " + Method[..(Method.LastIndexOf('.') + 1)] + "TestCleanup() in " + CodeFile + ":line 9\n" + stack; break;
                case "initialization": stack = "at " + Method[..(Method.LastIndexOf('.') + 1)] + "TestInitialize() in " + CodeFile + ":line 9\n" + stack; break;
                default: throw new InvalidOperationException("Unknown collection assertion receiving control.");
            }
            result["ErrorMessage"] = message; result["ErrorStackTrace"] = stack;
        });
        AssertRejected(fixture.Audit());
    }

    private static string CollectionStack(bool helper) =>
        (helper ? "at " + Method[..(Method.LastIndexOf('.') + 1)] + "Headers() in " + CodeFile + ":line 9\n" : "")
        + "at " + Method + "() in " + CodeFile + ":line 10\n"
        + "at Microsoft.VisualStudio.TestPlatform.MSTest.TestAdapter.Execution.TestMethodInfo.ExecuteInternalAsync(Object[] arguments, CancellationTokenSource timeoutTokenSource, TestInvocationState invocationState)";

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ExplicitSyntheticControllerAndHostRecordsJoinByActualPidInEitherFileOrder(bool reverseFiles)
    {
        using var fixture = new TraceFixture();
        string original = fixture.Report.ToJsonString();
        NativeAuditResult result = fixture.Audit(reverseFiles);
        CollectionAssert.AreEqual(new[] { "1" }, result.AssertionKilledIds);
        Assert.HasCount(0, result.Issues);
        Assert.AreEqual(1, result.HostCount);
        Assert.AreEqual(1, result.NormalHostCount);
        Assert.AreEqual(1, (int?)result.Observations["outcomeMetadata"]!["passed"]);
        Assert.AreEqual(2, (int?)result.Observations["outcomeMetadata"]!["failed"]);
        Assert.AreEqual(false, (bool?)result.Observations["scoreComputed"]);
        Assert.AreEqual(original, fixture.Report.ToJsonString());
    }

    [TestMethod]
    [DataRow("request-missing")]
    [DataRow("callback-missing")]
    [DataRow("callback-duplicate")]
    [DataRow("pending-request-overlap")]
    [DataRow("late-request-overlap")]
    [DataRow("mixed-controller-pid")]
    [DataRow("parent-mismatch")]
    [DataRow("parent-conflict")]
    [DataRow("complete-missing")]
    [DataRow("complete-duplicate")]
    [DataRow("abort-marker")]
    [DataRow("cancel-marker")]
    [DataRow("aborted-completion")]
    [DataRow("canceled-completion")]
    [DataRow("error-completion")]
    [DataRow("active-tests-remain")]
    [DataRow("terminate-missing")]
    [DataRow("exiting-before-terminate")]
    [DataRow("exiting-missing")]
    [DataRow("actual-os-exit-missing")]
    [DataRow("actual-os-exit-nonzero")]
    [DataRow("actual-os-exit-error")]
    [DataRow("mixed-host-pid")]
    [DataRow("runtime-missing")]
    [DataRow("runtime-unsupported")]
    [DataRow("malformed-structured-json")]
    public void ADirectAssertionCannotQualifyAnIncompleteAmbiguousOrAbortedNativeHost(string mode)
    {
        using var fixture = new TraceFixture();
        switch (mode)
        {
            case "request-missing": fixture.Controller.RemoveAt(1); break;
            case "callback-missing": fixture.Controller.RemoveAt(2); break;
            case "callback-duplicate": fixture.Controller.Add(fixture.Controller[2]); break;
            case "pending-request-overlap": fixture.Controller.Insert(2, fixture.Controller[1]); break;
            case "late-request-overlap": fixture.Controller.Add(fixture.Controller[1]); fixture.Controller.Add(fixture.Controller[1]); break;
            case "mixed-controller-pid": fixture.Controller.Add(ControllerLine("Synthetic conflicting process header").Replace("0 : 10,", "0 : 99,", StringComparison.Ordinal)); break;
            case "parent-mismatch": fixture.Host[2] = fixture.Host[2].Replace("[--parentprocessid, 10]", "[--parentprocessid, 99]", StringComparison.Ordinal); break;
            case "parent-conflict": fixture.Host.Insert(3, HostLine("Monitoring parent process with id: '99'")); break;
            case "complete-missing": fixture.RemoveHostMessage("TestExecution.Completed"); break;
            case "complete-duplicate": fixture.Host.Insert(6, fixture.Host[5]); break;
            case "abort-marker": fixture.Host.Insert(5, HostReceive("TestExecution.Abort", null)); break;
            case "cancel-marker": fixture.Host.Insert(5, HostReceive("TestExecution.Cancel", null)); break;
            case "aborted-completion": fixture.ChangeCompletion(args => args["IsAborted"] = true); break;
            case "canceled-completion": fixture.ChangeCompletion(args => args["IsCanceled"] = true); break;
            case "error-completion": fixture.ChangeCompletion(args => args["Error"] = "Synthetic infrastructure failure"); break;
            case "active-tests-remain": fixture.ChangeHostMessage("TestExecution.Completed", message => message["Payload"]!["LastRunTests"]!["ActiveTests"]!.AsArray().Add(TestCase(FirstGuid))); break;
            case "terminate-missing": fixture.RemoveHostMessage("TestSession.Terminate"); break;
            case "exiting-before-terminate": (fixture.Host[6], fixture.Host[7]) = (fixture.Host[7], fixture.Host[6]); break;
            case "exiting-missing": fixture.Host.RemoveAt(7); break;
            case "actual-os-exit-missing": fixture.Controller.RemoveAt(3); break;
            case "actual-os-exit-nonzero": fixture.Controller[3] = fixture.Controller[3].Replace("exitcode: 0", "exitcode: 134", StringComparison.Ordinal); break;
            case "actual-os-exit-error": fixture.Controller[3] = fixture.Controller[3].Replace("error: ''", "error: 'crash'", StringComparison.Ordinal); break;
            case "mixed-host-pid": fixture.Host[4] = fixture.Host[4].Replace("0 : 20,", "0 : 21,", StringComparison.Ordinal); break;
            case "runtime-missing": fixture.Host.RemoveAt(1); break;
            case "runtime-unsupported": fixture.Host[1] = fixture.Host[1].Replace("10.0.12", "10.0.13", StringComparison.Ordinal); break;
            case "malformed-structured-json": fixture.Host[4] = HostLine(SendMarker + "{not-json"); break;
            default: throw new InvalidOperationException("Unknown native receiving control.");
        }
        AssertRejected(fixture.Audit());
    }

    [TestMethod]
    [DataRow("unknown-reported-killedby")]
    [DataRow("duplicate-reported-guid")]
    [DataRow("contradictory-request-fqn")]
    [DataRow("unknown-result-guid")]
    [DataRow("duplicate-result-guid")]
    [DataRow("wrong-request-dll")]
    [DataRow("wrong-result-codefile")]
    [DataRow("unsupported-outcome-number")]
    [DataRow("unsupported-outcome-string")]
    [DataRow("statistics-disagree")]
    [DataRow("structured-results-missing")]
    [DataRow("wrong-host-filter")]
    [DataRow("unknown-selector")]
    [DataRow("selector-does-not-own-guid")]
    [DataRow("empty-native-inputs")]
    [DataRow("duplicate-log-path")]
    [DataRow("unrecognized-runner-header")]
    public void ExactGuidSourceSelectorAndNumericOutcomeBindingsCannotBeReplacedByACohortFailure(string mode)
    {
        using var fixture = new TraceFixture();
        bool noLogs = false, duplicatePath = false;
        switch (mode)
        {
            case "unknown-reported-killedby": fixture.Definition["killedBy"] = new JsonArray(SecondGuid); break;
            case "duplicate-reported-guid": fixture.Report["testFiles"]![CodeFile]!["tests"]!.AsArray().Add(fixture.Report["testFiles"]![CodeFile]!["tests"]![0]!.DeepClone()); break;
            case "contradictory-request-fqn": fixture.ChangeControllerMessage(RequestType, message => message["Payload"]!["TestCases"]![0]!["FullyQualifiedName"] = Method + "Other"); break;
            case "unknown-result-guid": fixture.ChangeResult(result => result["TestCase"]!["Id"] = SecondGuid); break;
            case "duplicate-result-guid": fixture.ChangeHostMessage("TestExecution.StatsChange", message => message["Payload"]!["NewTestResults"]!.AsArray().Add(message["Payload"]!["NewTestResults"]![0]!.DeepClone())); break;
            case "wrong-request-dll": fixture.ChangeControllerMessage(RequestType, message => message["Payload"]!["TestCases"]![0]!["Source"] = DllFile.Replace("Release", "Debug", StringComparison.Ordinal)); break;
            case "wrong-result-codefile": fixture.ChangeResult(result => result["TestCase"]!["CodeFilePath"] = "/other/Fixture.cs"); break;
            case "unsupported-outcome-number": fixture.ChangeResult(result => result["Outcome"] = 99); break;
            case "unsupported-outcome-string": fixture.ChangeResult(result => result["Outcome"] = "Failed"); break;
            case "statistics-disagree": fixture.ChangeCompletion(args => args["TestRunStatistics"]!["Stats"]!["Failed"] = 0); break;
            case "structured-results-missing": fixture.ChangeHostMessage("TestExecution.StatsChange", message => message["Payload"]!["NewTestResults"] = new JsonArray()); break;
            case "wrong-host-filter": fixture.ChangeHostMessage("TestExecution.StartWithTests", message => message["Payload"]!["RunSettings"] = Settings([(FirstGuid, "1")]).Replace(Filter, "FullyQualifiedName~Other", StringComparison.Ordinal)); break;
            case "unknown-selector": fixture.ChangeSettings(settings => settings.Replace("id=\"1\"", "id=\"999\"", StringComparison.Ordinal)); break;
            case "selector-does-not-own-guid": fixture.ChangeSettings(settings => settings.Replace(FirstGuid, SecondGuid, StringComparison.Ordinal)); break;
            case "empty-native-inputs": noLogs = true; break;
            case "duplicate-log-path": duplicatePath = true; break;
            case "unrecognized-runner-header": fixture.Controller[0] = "Synthetic unsupported diagnostic header"; break;
            default: throw new InvalidOperationException("Unknown native receiving control.");
        }
        AssertRejected(fixture.Audit(noLogs: noLogs, duplicatePath: duplicatePath));
    }

    [TestMethod]
    [DataRow("timeout")]
    [DataRow("timeout-wrapped-as-assertion")]
    [DataRow("cancellation")]
    [DataRow("aggregate")]
    [DataRow("cleanup-stack")]
    [DataRow("disposal-stack")]
    [DataRow("missing-stack")]
    [DataRow("wrong-receiving-method")]
    [DataRow("raw-status-not-killed")]
    [DataRow("reported-kill-actually-passed")]
    public void TimeoutsExceptionsCleanupAndUnrelatedStacksNeverSupplyDirectAssertionCredit(string mode)
    {
        using var fixture = new TraceFixture();
        switch (mode)
        {
            case "timeout": fixture.ChangeResult(result => result["ErrorMessage"] = "System.TimeoutException: synthetic bounded wait expired"); break;
            case "timeout-wrapped-as-assertion": fixture.ChangeResult(result => result["ErrorMessage"] = "Assertion failed. System.TimeoutException"); break;
            case "cancellation": fixture.ChangeResult(result => result["ErrorMessage"] = "Assertion failed. System.OperationCanceledException"); break;
            case "aggregate": fixture.ChangeResult(result => result["ErrorMessage"] = "Assertion failed. AggregateException: cleanup also failed"); break;
            case "cleanup-stack": fixture.ChangeResult(result => result["ErrorStackTrace"] = "at " + Method + "()\nat Fixture.TestCleanup()"); break;
            case "disposal-stack": fixture.ChangeResult(result => result["ErrorStackTrace"] = "at " + Method + "()\nat Fixture.DisposeAsync()"); break;
            case "missing-stack": fixture.ChangeResult(result => result["ErrorStackTrace"] = null); break;
            case "wrong-receiving-method": fixture.ChangeResult(result => result["ErrorStackTrace"] = "at Other.UnrelatedAssertion()"); break;
            case "raw-status-not-killed": fixture.Definition["status"] = "Survived"; break;
            case "reported-kill-actually-passed":
                fixture.ChangeResult(result => result["Outcome"] = 1);
                fixture.ChangeCompletion(args => args["TestRunStatistics"]!["Stats"] = new JsonObject { ["Passed"] = 1 });
                break;
            default: throw new InvalidOperationException("Unknown native receiving control.");
        }
        AssertRejected(fixture.Audit());
    }

    [TestMethod]
    public void AFailedGuidInTheSameHostDoesNotKillItsPassedSiblingSelector()
    {
        using var fixture = new TraceFixture(secondCase: true, secondFails: false);
        fixture.SecondDefinition["killedBy"] = new JsonArray(FirstGuid);
        NativeAuditResult result = fixture.Audit();
        CollectionAssert.AreEqual(new[] { "1" }, result.AssertionKilledIds);
        Assert.AreEqual(1, result.NormalHostCount);
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == "NativeAssertionWitnessMissing" && issue.MutantIds.SequenceEqual(["2"])));
    }

    [TestMethod]
    public void DistinctDataRowGuidsCanShareOneReceivingMethodWithoutLosingExactSelectorOwnership()
    {
        using var fixture = new TraceFixture(secondCase: true, secondFails: true);
        NativeAuditResult result = fixture.Audit();
        CollectionAssert.AreEqual(new[] { "1", "2" }, result.AssertionKilledIds);
        Assert.HasCount(0, result.Issues);
        Assert.AreEqual(1, result.NormalHostCount);
    }

    [TestMethod]
    public void OverlappingPerMutantGuidDeclarationsAreAmbiguousEvenWhenTheReportedKillLooksPlausible()
    {
        using var fixture = new TraceFixture(secondCase: true, secondFails: true);
        fixture.ChangeSettings(settings => settings.Replace("id=\"2\" tests=\"" + SecondGuid, "id=\"2\" tests=\"" + FirstGuid, StringComparison.Ordinal));
        AssertRejected(fixture.Audit());
    }

    [TestMethod]
    public void ASharedFailureCannotCreditAReportedKilledByGuidAssignedToAnotherSelector()
    {
        using var fixture = new TraceFixture(secondCase: true, secondFails: false);
        fixture.Definition["killedBy"] = new JsonArray(SecondGuid);
        AssertRejected(fixture.Audit());
    }

    [TestMethod]
    public void AnUnrecognizedObjectModelAssemblyCannotSupplyInventedOutcomeConstants()
    {
        using var fixture = new TraceFixture();
        NativeAuditResult result = fixture.Audit(objectModel: typeof(HealthcheckMutationNativeAuditTests).Assembly.Location);
        AssertRejected(result);
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == "NativeAuditUnsupported" && issue.MutantIds.Length == 0));
    }

    [TestMethod]
    [DataRow("indentation")]
    [DataRow("framework-alias")]
    [DataRow("logger-only")]
    [DataRow("xml-space-default")]
    public void NonSemanticSettingsPresentationDoesNotBreakActualParentHostAssociation(string mode)
    {
        using var fixture = new TraceFixture();
        if (mode == "xml-space-default")
        {
            fixture.ChangeSettings(settings => AppendConfiguration(settings, "<Synthetic xml:space=\"preserve\"><Child xml:space=\"default\"><A /></Child></Synthetic>"));
        }
        fixture.ChangeHostMessage("TestExecution.StartWithTests", message =>
        {
            string settings = (string)message["Payload"]!["RunSettings"]!;
            if (mode == "indentation") { settings = XDocument.Parse(settings).ToString(); }
            if (mode == "framework-alias") { settings = settings.Replace("net10.0", ".NETCoreApp,Version=v10.0", StringComparison.Ordinal); }
            if (mode == "logger-only") { settings = settings.Replace("</RunSettings>", "<LoggerRunSettings><Loggers><Logger friendlyName=\"synthetic\" /></Loggers></LoggerRunSettings></RunSettings>", StringComparison.Ordinal); }
            if (mode == "xml-space-default") { settings = settings.Replace("<A />", "\n  <A />\n", StringComparison.Ordinal); }
            message["Payload"]!["RunSettings"] = settings;
        });
        NativeAuditResult result = fixture.Audit();
        CollectionAssert.AreEqual(new[] { "1" }, result.AssertionKilledIds);
        Assert.HasCount(0, result.Issues);
    }

    [TestMethod]
    [DataRow("leaf-space")]
    [DataRow("leaf-cdata")]
    [DataRow("xml-space-preserve")]
    public void ScalarWhitespaceAndXmlSpacePreserveRemainPartOfTheActualSettingsIdentity(string mode)
    {
        using var fixture = new TraceFixture();
        string original = mode == "xml-space-preserve" ? "<Synthetic xml:space=\"preserve\"><A /></Synthetic>" : "<SyntheticScalar></SyntheticScalar>";
        fixture.ChangeSettings(settings => AppendConfiguration(settings, original));
        fixture.ChangeHostMessage("TestExecution.StartWithTests", message =>
        {
            string changed = mode switch
            {
                "leaf-space" => "<SyntheticScalar> </SyntheticScalar>",
                "leaf-cdata" => "<SyntheticScalar><![CDATA[ ]]></SyntheticScalar>",
                _ => "<Synthetic xml:space=\"preserve\"> <A /></Synthetic>"
            };
            message["Payload"]!["RunSettings"] = ((string)message["Payload"]!["RunSettings"]!).Replace(original, changed, StringComparison.Ordinal);
        });
        AssertRejected(fixture.Audit());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CompleteSourceCheckedDiscoveryIsObservedSeparatelyAndNeverCreatesMutationKills(bool dataRows)
    {
        using var fixture = new TraceFixture(secondCase: dataRows, secondFails: dataRows);
        fixture.AddDiscovery();
        NativeAuditResult result = fixture.Audit(reverseFiles: true);
        Assert.HasCount(dataRows ? 2 : 1, result.AssertionKilledIds);
        Assert.HasCount(0, result.Issues);
        Assert.AreEqual(1, result.HostCount);
        Assert.AreEqual(1, result.NormalHostCount);
        Assert.AreEqual(1, (int?)result.Observations["discoveryHostCount"]);
        Assert.AreEqual(1, (int?)result.Observations["normalDiscoveryHostCount"]);
    }

    [TestMethod]
    [DataRow("request-missing")]
    [DataRow("request-duplicate")]
    [DataRow("wrong-parent")]
    [DataRow("wrong-filter")]
    [DataRow("wrong-source")]
    [DataRow("selectors-present")]
    [DataRow("guid-missing")]
    [DataRow("guid-unknown")]
    [DataRow("guid-duplicate")]
    [DataRow("guid-method-mismatch")]
    [DataRow("guid-source-mismatch")]
    [DataRow("completion-missing")]
    [DataRow("completion-aborted")]
    [DataRow("total-mismatch")]
    [DataRow("partial-source")]
    [DataRow("skipped-source")]
    [DataRow("os-exit-missing")]
    [DataRow("os-exit-nonzero")]
    [DataRow("terminate-missing")]
    [DataRow("execution-message")]
    public void ADiscoveryFilenameCannotHideIncompleteConflictingOrWrongSourceDiscovery(string mode)
    {
        using var fixture = new TraceFixture(); fixture.AddDiscovery();
        switch (mode)
        {
            case "request-missing": fixture.DiscoveryController.RemoveAt(1); break;
            case "request-duplicate": fixture.DiscoveryController.Insert(2, fixture.DiscoveryController[1]); break;
            case "wrong-parent": fixture.DiscoveryHost[2] = fixture.DiscoveryHost[2].Replace("[--parentprocessid, 30]", "[--parentprocessid, 99]", StringComparison.Ordinal); break;
            case "wrong-filter": fixture.ChangeDiscoveryMessage("TestDiscovery.Start", message => message["Payload"]!["TestCaseFilter"] = "FullyQualifiedName~Other"); break;
            case "wrong-source": fixture.ChangeDiscoveryMessage("TestDiscovery.Start", message => message["Payload"]!["AdapterSourceMap"]!["_none_"]![0] = "/other/Unknown.dll"); break;
            case "selectors-present":
                fixture.ChangeDiscoveryMessage("TestDiscovery.Start", message => message["Payload"]!["RunSettings"] = Settings([(FirstGuid, "1")]));
                fixture.ChangeDiscoveryControllerMessage("TestDiscovery.Start", message => message["Payload"]!["RunSettings"] = Settings([(FirstGuid, "1")]));
                break;
            case "guid-missing": fixture.ChangeDiscoveryMessage("TestDiscovery.TestFound", message => message["Payload"] = new JsonArray()); break;
            case "guid-unknown": fixture.ChangeDiscoveryMessage("TestDiscovery.TestFound", message => message["Payload"]![0]!["Id"] = SecondGuid); break;
            case "guid-duplicate": fixture.ChangeDiscoveryMessage("TestDiscovery.TestFound", message => message["Payload"]!.AsArray().Add(message["Payload"]![0]!.DeepClone())); break;
            case "guid-method-mismatch": fixture.ChangeDiscoveryMessage("TestDiscovery.TestFound", message => message["Payload"]![0]!["FullyQualifiedName"] = Method + "Other"); break;
            case "guid-source-mismatch": fixture.ChangeDiscoveryMessage("TestDiscovery.TestFound", message => message["Payload"]![0]!["CodeFilePath"] = "/other/Fixture.cs"); break;
            case "completion-missing": fixture.DiscoveryHost.RemoveAll(line => line.Contains("\"MessageType\":\"TestDiscovery.Completed\"", StringComparison.Ordinal)); break;
            case "completion-aborted": fixture.ChangeDiscoveryMessage("TestDiscovery.Completed", message => message["Payload"]!["IsAborted"] = true); break;
            case "total-mismatch": fixture.ChangeDiscoveryMessage("TestDiscovery.Completed", message => message["Payload"]!["TotalTests"] = 2); break;
            case "partial-source": fixture.ChangeDiscoveryMessage("TestDiscovery.Completed", message => message["Payload"]!["PartiallyDiscoveredSources"]!.AsArray().Add(DllFile)); break;
            case "skipped-source": fixture.ChangeDiscoveryMessage("TestDiscovery.Completed", message => message["Payload"]!["SkippedDiscoverySources"]!.AsArray().Add(DllFile)); break;
            case "os-exit-missing": fixture.DiscoveryController.RemoveAt(3); break;
            case "os-exit-nonzero": fixture.DiscoveryController[3] = fixture.DiscoveryController[3].Replace("exitcode: 0", "exitcode: 1", StringComparison.Ordinal); break;
            case "terminate-missing": fixture.DiscoveryHost.RemoveAt(6); break;
            case "execution-message": fixture.DiscoveryHost.Insert(5, DiscoveryLine(false, SendMarker + Message("TestExecution.StatsChange", new JsonObject { ["NewTestResults"] = new JsonArray() }))); break;
            default: throw new InvalidOperationException("Unknown discovery receiving control.");
        }
        NativeAuditResult result = fixture.Audit();
        Assert.IsTrue(result.AssertionKilledIds.All(id => id == "1"), "Discovery cannot add an unrelated execution witness.");
        Assert.IsTrue(result.Issues.Any(issue => issue.MutantIds.Length == 0), "Incomplete discovery must remain a global integrity issue.");
    }

    [TestMethod]
    [DataRow("discovery", 0, true)]
    [DataRow("discovery", 1, false)]
    [DataRow("discovery", 2, false)]
    [DataRow("discovery", 99, false)]
    [DataRow("execution", 0, true)]
    [DataRow("execution", 1, false)]
    [DataRow("execution", 2, false)]
    [DataRow("execution", 99, false)]
    public void ActualMetadataInformationalDiagnosticsAreAllowedWhileWarningsErrorsAndUnknownLevelsBlockEvidence(string lane, int level, bool informational)
    {
        using var fixture = new TraceFixture();
        var payload = new JsonObject { ["MessageLevel"] = level, ["Message"] = "Logging TestHost Diagnostics in file: /synthetic/diagnostic.txt" };
        if (lane == "discovery")
        {
            fixture.AddDiscovery();
            fixture.DiscoveryHost.Insert(3, DiscoveryLine(false, SendMarker + Message("TestSession.Message", payload)));
        }
        else { fixture.Host.Insert(3, HostSend("TestSession.Message", payload)); }
        NativeAuditResult result = fixture.Audit();
        if (informational)
        {
            CollectionAssert.AreEqual(new[] { "1" }, result.AssertionKilledIds);
            Assert.HasCount(0, result.Issues);
            Assert.AreEqual(1, result.NormalHostCount);
            if (lane == "discovery") { Assert.AreEqual(1, (int?)result.Observations["normalDiscoveryHostCount"]); }
        }
        else
        {
            Assert.IsGreaterThan(0, result.Issues.Length);
            if (lane == "execution") { Assert.HasCount(0, result.AssertionKilledIds); }
            else { Assert.IsTrue(result.Issues.Any(issue => issue.MutantIds.Length == 0)); }
        }
    }

    private static void AssertRejected(NativeAuditResult result)
    {
        Assert.HasCount(0, result.AssertionKilledIds);
        Assert.IsGreaterThan(0, result.Issues.Length);
    }

    private const string FirstGuid = "11111111-1111-4111-8111-111111111111";
    private const string SecondGuid = "22222222-2222-4222-8222-222222222222";
    private const string Method = "CIS.Tests.UnitTests.Healthcheck.NativeParserFixture.Direct";
    private const string CodeFile = "/synthetic/implementations/csharp/tests/CIS.Core.UnitTests/Healthcheck/Fixture.cs";
    private const string DllFile = "/synthetic/implementations/csharp/tests/CIS.Core.UnitTests/bin/Release/net10.0/CIS.Core.UnitTests.dll";
    private const string Filter = "FullyQualifiedName~CIS.Tests.UnitTests.Healthcheck.";
    private const string RequestType = "TestExecution.GetTestRunnerProcessStartInfoForRunSelected";
    private const string ReceiveMarker = "TestRequestHandler.OnMessageReceived: received message: ";
    private const string SendMarker = "TestRequestHandler.SendData: sending data from testhost: ";

    private static JsonObject TestCase(string guid) => new()
    {
        ["Id"] = guid,
        ["FullyQualifiedName"] = Method,
        ["Source"] = DllFile,
        ["CodeFilePath"] = CodeFile
    };

    private static string Settings((string Guid, string Mutant)[] assignments)
    {
        string selectors = string.Concat(assignments.Select(pair => "<Mutant id=\"" + pair.Mutant + "\" tests=\"" + pair.Guid + "\"/>"));
        return "<RunSettings><RunConfiguration><TargetFrameworkVersion>net10.0</TargetFrameworkVersion><TestCaseFilter>" + Filter
            + "</TestCaseFilter></RunConfiguration><InProcDataCollectionRunSettings><InProcDataCollectors><InProcDataCollector><Configuration><Parameters>"
            + selectors + "<MutantControl name=\"StrykerSynthetic.MutantControl\"/></Parameters></Configuration></InProcDataCollector></InProcDataCollectors></InProcDataCollectionRunSettings></RunSettings>";
    }

    private static string ControllerLine(string text) => "TpTrace Information: 0 : 10, 1, synthetic, vstest.console.dll, " + text;
    private static string HostLine(string text) => "TpTrace Information: 0 : 20, 1, synthetic, testhost.dll, " + text;
    private static string ControllerMessage(string type, JsonNode? payload) => ControllerLine("DesignModeClient.ProcessRequests: Processing Message: (" + type + ") -> " + Message(type, payload));
    private static string HostReceive(string type, JsonNode? payload) => HostLine(ReceiveMarker + Message(type, payload));
    private static string HostSend(string type, JsonNode? payload) => HostLine(SendMarker + Message(type, payload));
    private static string Message(string type, JsonNode? payload) => new JsonObject { ["MessageType"] = type, ["Payload"] = payload }.ToJsonString();
    private static string AppendConfiguration(string settings, string value) => settings.Replace("</RunConfiguration>", value + "</RunConfiguration>", StringComparison.Ordinal);
    private static string DiscoveryLine(bool controller, string text) => "TpTrace Information: 0 : " + (controller ? "30, 1, synthetic, vstest.console.dll, " : "40, 1, synthetic, testhost.dll, ") + text;

    // These literals are synthetic protocol inputs, not fabricated native evidence.
    // The reader still inspects the actual installed ObjectModel metadata.
    private sealed class TraceFixture : IDisposable
    {
        private readonly TemporaryDirectory temporary = new();
        internal JsonObject Report { get; }
        internal JsonObject Configuration { get; }
        internal List<string> Controller { get; }
        internal List<string> Host { get; }
        internal List<string> DiscoveryController { get; } = [];
        internal List<string> DiscoveryHost { get; } = [];
        internal JsonObject Definition => Report["files"]!["/synthetic/Core.cs"]!["mutants"]![0]!.AsObject();
        internal JsonObject SecondDefinition => Report["files"]!["/synthetic/Core.cs"]!["mutants"]![1]!.AsObject();

        internal TraceFixture(bool secondCase = false, bool secondFails = false)
        {
            string[] guids = secondCase ? [FirstGuid, SecondGuid] : [FirstGuid];
            var tests = new JsonArray(); var definitions = new JsonArray(); var selected = new JsonArray(); var results = new JsonArray();
            for (int index = 0; index < guids.Length; index++)
            {
                string guid = guids[index];
                tests.Add(new JsonObject { ["id"] = guid, ["name"] = Method }); selected.Add(TestCase(guid));
                definitions.Add(new JsonObject { ["id"] = (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), ["status"] = "Killed", ["killedBy"] = new JsonArray(guid) });
                results.Add(new JsonObject
                {
                    ["TestCase"] = TestCase(guid),
                    ["Outcome"] = index == 0 || secondFails ? 2 : 1,
                    ["ErrorMessage"] = "Assertion failed. Expected true, observed false.",
                    ["ErrorStackTrace"] = "at " + Method + "() in " + CodeFile + ":line 10"
                });
            }
            Report = new JsonObject
            {
                ["testFiles"] = new JsonObject { [CodeFile] = new JsonObject { ["tests"] = tests } },
                ["files"] = new JsonObject { ["/synthetic/Core.cs"] = new JsonObject { ["mutants"] = definitions } }
            };
            Configuration = new JsonObject
            {
                ["stryker-config"] = new JsonObject
                {
                    ["target-framework"] = "net10.0",
                    ["configuration"] = "Release",
                    ["test-case-filter"] = Filter,
                    ["test-projects"] = new JsonArray("../../tests/CIS.Core.UnitTests/CIS.Core.UnitTests.csproj")
                }
            };
            string settings = Settings(guids.Select((guid, index) => (guid, (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture))).ToArray());
            var request = new JsonObject { ["RunSettings"] = settings, ["TestCases"] = selected };
            Controller =
            [
                ControllerLine("Version: synthetic"), ControllerMessage(RequestType, request),
                ControllerMessage("TestExecution.CustomTestHostLaunchCallback", new JsonObject { ["HostProcessId"] = 20, ["ErrorMessage"] = null }),
                ControllerLine("TestHostManagerCallbacks.ExitCallBack: Testhost processId: 20 exited with exitcode: 0 error: ''")
            ];
            int failed = secondCase && secondFails ? 2 : 1;
            var counts = new JsonObject { ["Failed"] = failed };
            if (guids.Length > failed) { counts["Passed"] = guids.Length - failed; }
            Host =
            [
                HostLine("Version: synthetic"), HostLine("Runtime location: /synthetic/dotnet/shared/Microsoft.NETCore.App/10.0.12"),
                HostLine("Testhost process started with args :[--parentprocessid, 10]"),
                HostReceive("TestExecution.StartWithTests", new JsonObject { ["RunSettings"] = settings, ["Tests"] = selected.DeepClone() }),
                HostSend("TestExecution.StatsChange", new JsonObject { ["NewTestResults"] = results }),
                HostSend("TestExecution.Completed", new JsonObject
                {
                    ["TestRunCompleteArgs"] = new JsonObject
                    {
                        ["IsAborted"] = false, ["IsCanceled"] = false, ["Error"] = null,
                        ["TestRunStatistics"] = new JsonObject { ["ExecutedTests"] = guids.Length, ["Stats"] = counts }
                    },
                    ["LastRunTests"] = new JsonObject { ["NewTestResults"] = new JsonArray(), ["ActiveTests"] = new JsonArray() }
                }),
                HostReceive("TestSession.Terminate", null), HostLine("Testhost process exiting.")
            ];
        }

        internal NativeAuditResult Audit(bool reverseFiles = false, bool noLogs = false, bool duplicatePath = false, string? objectModel = null)
        {
            string controller = System.IO.Path.Combine(temporary.Path, "Runner 0-log.txt"), host = System.IO.Path.Combine(temporary.Path, "Runner 0-log.host.synthetic.txt");
            File.WriteAllLines(controller, Controller); File.WriteAllLines(host, Host);
            var all = new List<string> { controller, host };
            if (DiscoveryController.Count > 0)
            {
                string discovery = System.IO.Path.Combine(temporary.Path, "SpecificTestDiscoverer-log.txt");
                File.WriteAllLines(discovery, DiscoveryController); all.Add(discovery);
            }
            if (DiscoveryHost.Count > 0)
            {
                string discovery = System.IO.Path.Combine(temporary.Path, "SpecificTestDiscoverer-log.host.synthetic.txt");
                File.WriteAllLines(discovery, DiscoveryHost); all.Add(discovery);
            }
            if (duplicatePath) { all.Add(controller); }
            if (reverseFiles) { all.Reverse(); }
            string[] paths = noLogs ? [] : all.ToArray();
            return HealthcheckMutationNativeAudit.Audit(Report, Configuration, paths,
                objectModel ?? System.IO.Path.Combine(AppContext.BaseDirectory, "Microsoft.VisualStudio.TestPlatform.ObjectModel.dll"));
        }

        internal void ChangeResult(Action<JsonObject> change) => ChangeHostMessage("TestExecution.StatsChange", message => change(message["Payload"]!["NewTestResults"]![0]!.AsObject()));
        internal void ChangeCompletion(Action<JsonObject> change) => ChangeHostMessage("TestExecution.Completed", message => change(message["Payload"]!["TestRunCompleteArgs"]!.AsObject()));
        internal void ChangeHostMessage(string type, Action<JsonObject> change) => ChangeMessage(Host, type, change);
        internal void ChangeControllerMessage(string type, Action<JsonObject> change) => ChangeMessage(Controller, type, change);
        internal void ChangeDiscoveryMessage(string type, Action<JsonObject> change) => ChangeMessage(DiscoveryHost, type, change);
        internal void ChangeDiscoveryControllerMessage(string type, Action<JsonObject> change) => ChangeMessage(DiscoveryController, type, change);
        internal void RemoveHostMessage(string type) => Host.RemoveAll(line => line.Contains("\"MessageType\":\"" + type + "\"", StringComparison.Ordinal));

        internal void ChangeSettings(Func<string, string> change)
        {
            ChangeControllerMessage(RequestType, message => message["Payload"]!["RunSettings"] = change((string)message["Payload"]!["RunSettings"]!));
            ChangeHostMessage("TestExecution.StartWithTests", message => message["Payload"]!["RunSettings"] = change((string)message["Payload"]!["RunSettings"]!));
        }

        internal void AddDiscovery()
        {
            string settings = "<RunSettings><RunConfiguration><TargetFrameworkVersion>net10.0</TargetFrameworkVersion><TestCaseFilter>" + Filter + "</TestCaseFilter></RunConfiguration></RunSettings>";
            JsonArray tests = new(Report["testFiles"]![CodeFile]!["tests"]!.AsArray().Select(test => (JsonNode)TestCase((string)test!["id"]!)).ToArray());
            DiscoveryController.AddRange([
                DiscoveryLine(true, "Version: synthetic"),
                DiscoveryLine(true, "DesignModeClient.ProcessRequests: Processing Message: (TestDiscovery.Start) -> " + Message("TestDiscovery.Start", new JsonObject { ["Sources"] = new JsonArray(DllFile), ["RunSettings"] = settings })),
                DiscoveryLine(true, "DotnetTestHostManager: Starting process '/synthetic/dotnet' with command line 'exec /synthetic/testhost.dll --parentprocessid 30'"),
                DiscoveryLine(true, "TestHostManagerCallbacks.ExitCallBack: Testhost processId: 40 exited with exitcode: 0 error: ''")
            ]);
            DiscoveryHost.AddRange([
                DiscoveryLine(false, "Version: synthetic"),
                DiscoveryLine(false, "Runtime location: /synthetic/dotnet/shared/Microsoft.NETCore.App/10.0.12"),
                DiscoveryLine(false, "Testhost process started with args :[--parentprocessid, 30]"),
                DiscoveryLine(false, ReceiveMarker + Message("TestDiscovery.Start", new JsonObject { ["AdapterSourceMap"] = new JsonObject { ["_none_"] = new JsonArray(DllFile) }, ["RunSettings"] = settings, ["TestCaseFilter"] = Filter })),
                DiscoveryLine(false, SendMarker + Message("TestDiscovery.TestFound", tests)),
                DiscoveryLine(false, SendMarker + Message("TestDiscovery.Completed", new JsonObject
                {
                    ["TotalTests"] = tests.Count, ["LastDiscoveredTests"] = new JsonArray(), ["IsAborted"] = false,
                    ["FullyDiscoveredSources"] = new JsonArray(DllFile), ["PartiallyDiscoveredSources"] = new JsonArray(),
                    ["NotDiscoveredSources"] = new JsonArray(), ["SkippedDiscoverySources"] = new JsonArray()
                })),
                DiscoveryLine(false, ReceiveMarker + Message("TestSession.Terminate", null)),
                DiscoveryLine(false, "Testhost process exiting.")
            ]);
        }

        private static void ChangeMessage(List<string> lines, string type, Action<JsonObject> change)
        {
            int index = lines.FindIndex(line => line.Contains("\"MessageType\":\"" + type + "\"", StringComparison.Ordinal));
            Assert.IsGreaterThanOrEqualTo(0, index, "Synthetic fixture message is missing.");
            int jsonStart = lines[index].IndexOf('{');
            JsonObject message = JsonNode.Parse(lines[index][jsonStart..])!.AsObject(); change(message);
            lines[index] = lines[index][..jsonStart] + message.ToJsonString();
        }

        public void Dispose() => temporary.Dispose();
    }
}
