using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using static CIS.Verification.HealthcheckMutationUnionModel;

namespace CIS.Verification;

// Reads original VSTest diagnostics, not an approval receipt. Physical input pins,
// child-origin proofs and source-bound adjustments belong to the union caller.
internal static partial class HealthcheckMutationNativeAudit
{
    private const string RequestMarker = "DesignModeClient.ProcessRequests: Processing Message: (";
    private const string ReceiveMarker = "TestRequestHandler.OnMessageReceived: received message: ";
    private const string SendMarker = "TestRequestHandler.SendData: sending data from testhost: ";
    private static readonly Regex ProcessPrefix = new(@"^TpTrace \w+: 0 : (?<pid>\d+),", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex ExitCallback = new(@"TestHostManagerCallbacks\.ExitCallBack: Testhost processId: (?<pid>\d+) exited with exitcode: (?<exit>-?\d+) error: '(?<error>.*)'", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex ParentArgument = new(@"\[--parentprocessid, (?<pid>\d+)\]", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex ParentMonitor = new(@"Monitoring parent process with id: '(?<pid>\d+)'", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    internal static NativeAuditResult Audit(JsonObject report, JsonObject config, IReadOnlyList<string> logPaths, string objectModelPath,
        string? originalReportSha256 = null, Definition[]? sourceCheckedDefinitions = null, Source[]? reviewedSources = null,
        HealthcheckMutationNativeKernelEvidence? kernelEvidence = null)
        => AuditCore(report, config, logPaths, objectModelPath, originalReportSha256, sourceCheckedDefinitions, AbortedTailCase.Unit18Selector1904, reviewedSources, kernelEvidence);

    // Receiving seam: explicit synthetic case data cannot enter
    // the maintained Audit entry point, which uses only the fixed reviewed case.
    internal static NativeAuditResult AuditAbortedTailReceivingCase(JsonObject report, JsonObject config, IReadOnlyList<string> logPaths,
        string objectModelPath, string reportSha256, Definition[] definitions, AbortedTailCase receivingCase)
        => AuditCore(report, config, logPaths, objectModelPath, reportSha256, definitions, receivingCase);

    private static NativeAuditResult AuditCore(JsonObject report, JsonObject config, IReadOnlyList<string> logPaths, string objectModelPath,
        string? originalReportSha256, Definition[]? sourceCheckedDefinitions, AbortedTailCase abortedTailCase, Source[]? reviewedSources = null,
        HealthcheckMutationNativeKernelEvidence? kernelEvidence = null)
    {
        var state = new AuditState();
        try
        {
            JsonObject options = config["stryker-config"] as JsonObject ?? config;
            OutcomeMetadata outcomes = ReadOutcomeMetadata(objectModelPath);
            state.OutcomeMetadata = new JsonObject
            {
                ["assembly"] = outcomes.Assembly,
                ["version"] = outcomes.Version,
                ["mvid"] = outcomes.Mvid,
                ["passed"] = outcomes.Passed,
                ["failed"] = outcomes.Failed,
                ["informational"] = outcomes.Informational,
                ["warning"] = outcomes.Warning,
                ["error"] = outcomes.Error
            };
            Dictionary<string, NativeDefinition> definitions = Definitions(report);
            Dictionary<string, TestIdentity> tests = Tests(report, options);
            HealthcheckMutationSemanticProofs.Context? semantic = HealthcheckMutationSemanticProofs.CreateContext(report, originalReportSha256, sourceCheckedDefinitions, reviewedSources);
            Require(definitions.Values.All(definition => definition.KilledBy.All(tests.ContainsKey)), "Reported killedBy contains a GUID outside the declared report tests.");
            string filter = Text(options, "test-case-filter");
            var controllers = new List<string>();
            var hosts = new List<string>();
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (string path in logPaths)
            {
                Require(paths.Add(Path.GetFullPath(path)), "Duplicate native log path.");
                using var reader = File.OpenText(path);
                string? first = reader.ReadLine();
                if (first?.Contains(", vstest.console.dll,", StringComparison.Ordinal) == true) { controllers.Add(path); }
                else if (first?.Contains(", testhost.dll,", StringComparison.Ordinal) == true) { hosts.Add(path); }
                else if (Path.GetFileName(path).StartsWith("Runner ", StringComparison.Ordinal))
                {
                    state.Issue("NativeLogHeader", $"Unrecognized Runner diagnostic header: {path}", []);
                }
            }

            var requests = new Dictionary<int, NativeRequest>();
            var exits = new Dictionary<int, NativeExit>();
            var discoveries = new Dictionary<int, DiscoveryRequest>();
            string[] discoveryControllers = controllers.Where(ContainsDiscoveryRequest).ToArray();
            foreach (string path in discoveryControllers)
            {
                ReadController(path, tests, definitions, filter, state, requests, exits, discoveries);
            }

            var observedHosts = new HashSet<int>();
            var discoveryHosts = new HashSet<int>();
            var ownership = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string[] discoveryHostPaths = hosts.Where(IsDiscoveryHost).ToArray();
            foreach (string path in discoveryHostPaths)
            {
                ReadDiscoveryHost(path, tests, filter, outcomes, discoveries, requests, exits, discoveryHosts, state, ownership, kernelEvidence);
            }
            ResolveLinkedDiscoveryOwnership(tests, discoveries, ownership, state);
            foreach (string path in controllers.Except(discoveryControllers, StringComparer.Ordinal))
            {
                ReadController(path, tests, definitions, filter, state, requests, exits, discoveries);
            }
            if (controllers.Count == 0 || hosts.Count == 0 || requests.Count == 0)
            {
                state.Issue("NativeDiagnosticsMissing", "Original controller requests and subject host diagnostics are required; a native score or exit code cannot replace them.", []);
            }
            foreach (string path in hosts.Except(discoveryHostPaths, StringComparer.Ordinal))
            {
                ReadHost(path, tests, definitions, filter, outcomes, requests, exits, observedHosts, state, semantic, abortedTailCase);
            }

            foreach (DiscoveryRequest discovery in discoveries.Values)
            {
                if (!discovery.Trusted || discovery.LaunchLines.Count == 0 || discovery.LaunchLines.Count != discovery.HostCount
                    || !discovery.ObservedSources.SetEquals(discovery.Spec.Sources))
                { state.Issue("NativeDiscoveryIncomplete", $"Controller {discovery.ControllerPid}: standard launches, discovery hosts and source origins do not form a complete unique discovery request.", []); }
            }
            if (observedHosts.Overlaps(discoveryHosts)) { state.Issue("NativeProcessRoleAmbiguous", "One actual PID occurs in discovery and mutation execution.", []); }

            foreach ((int pid, NativeRequest request) in requests)
            {
                if (!observedHosts.Contains(pid)) { state.Issue("NativeHostMissing", $"Actual launch PID {pid} has no retained host diagnostic.", request.Ids); }
                if (!exits.ContainsKey(pid)) { state.Issue("NativeExitMissing", $"Actual launch PID {pid} has no actual parent OS-exit callback.", request.Ids); }
            }
            foreach (int pid in exits.Keys)
            {
                if (!requests.ContainsKey(pid) && !discoveryHosts.Contains(pid)) { state.Issue("NativeExitUnassigned", $"OS-exit callback PID {pid} has no unique native request/launch.", []); }
            }

            foreach (NativeDefinition definition in definitions.Values.Where(value => value.Status == "Killed"))
            {
                // The fixed reviewed completed-before-abort category is separate from normal
                // hosts. Unreviewed missing witnesses retain their existing normal-host rule.
                if (!state.AssertionKilled.Contains(definition.Id) && !state.ReviewedSemanticKilled.Contains(definition.Id)
                    && !state.CompletedBeforeAbortKilled.Contains(definition.Id))
                {
                    state.Issue("NativeAssertionWitnessMissing", "Raw Killed has no exact reported-and-declared GUID direct assertion or source-bound reviewed semantic failure in a complete normal host.", [definition.Id]);
                }
            }
        }
        catch (Exception error) when (EvidenceError(error))
        {
            state.Issue("NativeAuditUnsupported", $"{error.GetType().Name}: {Bound(error.Message)}", []);
        }

        return state.Result();
    }

    private static void ReadController(string path, Dictionary<string, TestIdentity> tests,
        Dictionary<string, NativeDefinition> definitions, string filter, AuditState state,
        Dictionary<int, NativeRequest> requests, Dictionary<int, NativeExit> exits, Dictionary<int, DiscoveryRequest> discoveries)
    {
        NativeRequest? pending = null;
        int controller = 0, lineNumber = 0;
        bool sequentialDiscoveryControllers = false;
        var segment = new DiscoveryControllerClosure();
        try
        {
            foreach (string line in File.ReadLines(path))
            {
                lineNumber++;
                CheckLine(line);
                int pid = LinePid(line);
                if (lineNumber == 1)
                {
                    controller = pid;
                    Require(controller > 0 && state.ControllerProcesses.Add(controller), "Controller PID header is missing, repeated or reused.");
                }
                else if (pid > 0 && pid != controller)
                {
                    Require(line.Contains(", vstest.console.dll, Version:", StringComparison.Ordinal), "A controller PID changes outside an actual new process header.");
                    CloseDiscoveryController(controller, path, pending, discoveries, exits, segment);
                    sequentialDiscoveryControllers = true;
                    Require(state.ControllerProcesses.Add(pid), "Sequential discovery controller PID is repeated or reused.");
                    controller = pid;
                    segment = new DiscoveryControllerClosure();
                }
                if (pid > 0) { Require(pid == controller, "Mixed controller process identities in one diagnostic."); }
                segment.Observe(line, lineNumber);
                if (line.Contains("DotnetTestHostManager: Starting process", StringComparison.Ordinal))
                {
                    Require(discoveries.TryGetValue(controller, out DiscoveryRequest? discovery), "Standard host launch has no unique typed discovery request.");
                    discovery.LaunchLines.Add(lineNumber);
                }
                Match exit = ExitCallback.Match(line);
                if (exit.Success)
                {
                    int hostPid = Number(exit.Groups["pid"].Value);
                    var value = new NativeExit(controller, Number(exit.Groups["exit"].Value), exit.Groups["error"].Value, lineNumber, path);
                    Require(exits.TryAdd(hostPid, value), "Duplicate or reused OS-exit PID is ambiguous.");
                }

                int marker = line.IndexOf(RequestMarker, StringComparison.Ordinal);
                if (marker < 0) { continue; }
                string suffix = line[(marker + RequestMarker.Length)..];
                int separator = suffix.IndexOf(") -> ", StringComparison.Ordinal);
                Require(separator >= 0, "Malformed native controller message envelope.");
                string envelopeType = suffix[..separator];
                JsonObject message = Parse(suffix[(separator + 5)..]);
                string type = Text(message, "MessageType");
                Require(type == envelopeType, "Controller message type disagrees with its envelope.");
                if (type == "TestSession.Terminate")
                {
                    Require(message.ContainsKey("Payload") && message["Payload"] is null, "Malformed controller termination payload.");
                    segment.Terminate(lineNumber);
                }
                if (type is "TestExecution.GetTestRunnerProcessStartInfoForRunAll" or "TestExecution.GetTestRunnerProcessStartInfoForRunSelected")
                {
                    Require(!discoveries.ContainsKey(controller), "A discovery controller cannot also launch mutation execution.");
                    JsonObject payload = Obj(message["Payload"]);
                    Require(pending is null, "Overlapping native launch requests have no unique callback association.");
                    bool selected = type.EndsWith("RunSelected", StringComparison.Ordinal);
                    RunSpec run = ReadRun(payload, selected ? "TestCases" : null, "Sources", tests, definitions, filter);
                    pending = new NativeRequest(controller, lineNumber, run, path);
                }
                else if (type == "TestExecution.CustomTestHostLaunchCallback")
                {
                    JsonObject payload = Obj(message["Payload"]);
                    Require(pending is not null, "Host launch callback has no unique pending native request.");
                    Require(Empty(payload["ErrorMessage"]), "Native launch callback reports an error.");
                    int hostPid = Integer(payload, "HostProcessId");
                    Require(hostPid > 0 && requests.TryAdd(hostPid, pending), "Duplicate, reused or invalid launch PID is ambiguous.");
                    pending.LaunchLine = lineNumber;
                    pending = null;
                }
                else if (type is "TestExecution.Abort" or "TestExecution.Cancel")
                {
                    NativeRequest[] active = requests.Where(pair => pair.Value.ControllerPid == controller && !exits.ContainsKey(pair.Key)).Select(pair => pair.Value).ToArray();
                    if (active.Length == 1) { active[0].ControllerStopped = true; }
                    state.Issue("NativeControllerAbortOrCancel", $"Controller {controller} received {type}.", active.Length == 1 ? active[0].Ids : []);
                }
                else if (type == "TestDiscovery.Start")
                {
                    Require(pending is null && !requests.Values.Any(request => request.ControllerPid == controller), "Mixed discovery/execution controller requests.");
                    DiscoverySpec spec = ReadDiscoverySpec(Obj(message["Payload"]), false, tests, filter);
                    Require(discoveries.TryAdd(controller, new DiscoveryRequest(controller, path, spec)), "Duplicate discovery controller request.");
                }
            }
            Require(pending is null, "Native request is missing its actual launch callback.");
            if (sequentialDiscoveryControllers) { CloseDiscoveryController(controller, path, pending, discoveries, exits, segment); }
        }
        catch (Exception error) when (EvidenceError(error))
        {
            foreach (NativeRequest request in requests.Values.Where(value => value.Path == path)) { request.ControllerTrusted = false; }
            foreach (DiscoveryRequest discovery in discoveries.Values.Where(value => value.Path == path)) { discovery.Trusted = false; }
            state.Issue("NativeControllerMalformed", $"{path}:{lineNumber}: {Bound(error.Message)}", []);
        }
    }

    private static void ReadHost(string path, Dictionary<string, TestIdentity> tests,
        Dictionary<string, NativeDefinition> definitions, string filter, OutcomeMetadata outcomes,
        Dictionary<int, NativeRequest> requests, Dictionary<int, NativeExit> exits,
        HashSet<int> observedHosts, AuditState state, HealthcheckMutationSemanticProofs.Context? semantic, AbortedTailCase abortedTailCase)
    {
        int pid = 0, lineNumber = 0, starts = 0, completeLine = 0, terminateLine = 0, exitingLine = 0;
        int completions = 0, terminates = 0, exiting = 0, aborts = 0, cancels = 0;
        NativeRequest? request = null;
        RunSpec? hostRun = null;
        JsonObject? completion = null;
        JsonArray? finalActive = null;
        var parents = new HashSet<int>();
        var results = new Dictionary<string, NativeResult>(StringComparer.OrdinalIgnoreCase);
        var completedBeforeAbort = new CompletedBeforeAbortProgress();
        bool malformed = false, diagnosticsNormal = true;
        string? runtime = null;
        try
        {
            foreach (string line in File.ReadLines(path))
            {
                lineNumber++;
                CheckLine(line);
                completedBeforeAbort.ObserveLifecycle(line, lineNumber, pid);
                int linePid = LinePid(line);
                if (linePid <= 0 && (line.Contains(ReceiveMarker, StringComparison.Ordinal) || line.Contains(SendMarker, StringComparison.Ordinal)))
                { completedBeforeAbort.UnknownTypedMessage = true; }
                if (lineNumber == 1)
                {
                    pid = linePid;
                    state.HostCount++;
                    Require(pid > 0 && observedHosts.Add(pid), "Host PID header is missing, duplicated or reused.");
                    Require(requests.TryGetValue(pid, out request), "Host has no unique actual controller launch callback.");
                }
                if (linePid > 0) { Require(linePid == pid, "Mixed host process identities in one diagnostic."); }
                const string runtimeMarker = "Runtime location: ";
                int runtimeOffset = line.IndexOf(runtimeMarker, StringComparison.Ordinal);
                if (runtimeOffset >= 0)
                {
                    Require(runtime is null, "Duplicate subject runtime observation.");
                    runtime = line[(runtimeOffset + runtimeMarker.Length)..];
                    state.RuntimeLocations.Add(runtime);
                }
                Match parent = ParentArgument.Match(line);
                if (!parent.Success) { parent = ParentMonitor.Match(line); }
                if (parent.Success) { parents.Add(Number(parent.Groups["pid"].Value)); }
                if (line.Contains("Testhost process exiting.", StringComparison.Ordinal)) { exiting++; exitingLine = lineNumber; }

                int receive = line.IndexOf(ReceiveMarker, StringComparison.Ordinal);
                if (receive >= 0)
                {
                    JsonObject message = Parse(line[(receive + ReceiveMarker.Length)..]);
                    string type = Text(message, "MessageType");
                    if (type is "TestExecution.StartWithTests" or "TestExecution.StartWithSources")
                    {
                        starts++;
                        Require(starts == 1 && completions == 0, "Host execution starts are ambiguous.");
                        JsonObject payload = Obj(message["Payload"]);
                        hostRun = ReadRun(payload, type == "TestExecution.StartWithTests" ? "Tests" : null,
                            type == "TestExecution.StartWithSources" ? "AdapterSourceMap" : "Sources", tests, definitions, filter);
                        Require(request is not null && request.Run.SameAs(hostRun), "Actual parent and host request/selector/test identities disagree.");
                    }
                    else if (type == "TestSession.Terminate") { terminates++; terminateLine = lineNumber; }
                    else if (type == "TestExecution.Abort") { aborts++; completedBeforeAbort.FirstAbortLine = completedBeforeAbort.FirstAbortLine == 0 ? lineNumber : completedBeforeAbort.FirstAbortLine; }
                    else if (type == "TestExecution.Cancel") { cancels++; }
                    else if (type == "TestDiscovery.Start") { throw Invalid("Mixed discovery and mutation execution in one host."); }
                    else if (type is not ("ProtocolVersion" or "TestExecution.Initialize")) { completedBeforeAbort.UnknownTypedMessage = true; }
                }

                int send = line.IndexOf(SendMarker, StringComparison.Ordinal);
                if (send < 0) { continue; }
                JsonObject sent = Parse(line[(send + SendMarker.Length)..]);
                string sentType = Text(sent, "MessageType");
                if (sentType == "TestSession.Message")
                {
                    if (InformationalMessage(sent, outcomes)) { state.InformationalMessageCount++; }
                    else { diagnosticsNormal = false; }
                    continue;
                }
                if (sentType is not ("TestExecution.StatsChange" or "TestExecution.Completed"))
                {
                    if (sentType != "ProtocolVersion") { completedBeforeAbort.UnknownTypedMessage = true; }
                    continue;
                }
                Require(starts == 1 && hostRun is not null && completions == 0, "Result is outside a unique active native execution.");
                JsonObject sentPayload = Obj(sent["Payload"]);
                JsonObject batch;
                if (sentType == "TestExecution.Completed")
                {
                    completions++;
                    completeLine = lineNumber;
                    completion = Obj(sentPayload["TestRunCompleteArgs"]);
                    batch = Obj(sentPayload["LastRunTests"]);
                    finalActive = Arr(batch["ActiveTests"]);
                }
                else { batch = sentPayload; }
                foreach (JsonNode? resultNode in Arr(batch["NewTestResults"]))
                {
                    JsonObject result = Obj(resultNode);
                    JsonObject testCase = Obj(result["TestCase"]);
                    string id = ValidateTest(testCase, tests);
                    Require(hostRun.Selected.Contains(id), "Result GUID was not requested for this native host.");
                    int outcome = Outcome(result["Outcome"], outcomes);
                    Require(results.TryAdd(id, new NativeResult(outcome, OptionalText(result, "ErrorMessage"), OptionalText(result, "ErrorStackTrace"))), "Duplicate native GUID result across StatsChange/Completed.");
                    completedBeforeAbort.ResultLines.Add(id, lineNumber);
                }
            }
        }
        catch (Exception error) when (EvidenceError(error))
        {
            malformed = true;
            // This does not make the session normal. Only the separately reviewed
            // original1904 tail may become an owned, still-unqualified issue.
            if (error.Message == "Result is outside a unique active native execution."
                && TryAttributeReviewedAbortedTail(abortedTailCase, semantic, path, request, hostRun, tests,
                    definitions, filter, outcomes, exits, lineNumber, results, out AbortedTailReceipt? tail))
            {
                state.Issue("NativeAbortedResultTail", $"{path}:{lineNumber}: {Bound(error.Message)} The complete reviewed tail is owned by selector{abortedTailCase.Id}; no host result receives kill credit.", [abortedTailCase.Id]);
                state.AbortedTailSamples.Add(tail!.Observation);
                // The first pass stopped at the late result. Independently read
                // actual terminal lines, rather than inventing a missing exit or
                // silently ignoring the rest of the raw diagnostic.
                terminates = 1; terminateLine = tail.TerminateLine;
                exiting = 1; exitingLine = tail.ExitingLine;
                request!.ControllerStopped = true;
            }
            else
            {
                // Unknown/ambiguous/malformed identities stay global.
                state.Issue("NativeHostMalformed", $"{path}:{lineNumber}: {Bound(error.Message)}", []);
            }
        }

        string[] ids = request?.Ids ?? [];
        bool normal = !malformed;
        void Check(bool condition, string code, string detail)
        {
            if (condition) { return; }
            normal = false;
            state.Issue(code, $"Host {pid}: {detail}", ids);
        }
        Check(diagnosticsNormal, "NativeDiagnosticFailure", "Actual warning/error diagnostics cannot support a normal host assertion witness.");
        Check(starts == 1 && hostRun is not null, "NativeStartMissing", "One actual host execution request is required.");
        Check(request?.ControllerTrusted == true, "NativeControllerUntrusted", "A malformed/ambiguous controller diagnostic cannot supply a unique launch/exit chain.");
        if (request is null || parents.Count != 1 || !parents.Contains(request.ControllerPid))
        {
            normal = false;
            state.Issue("NativeParentMismatch", $"Host {pid}: actual parent does not match a unique launching controller.", []);
        }
        if (runtime is null || !(runtime.EndsWith("/Microsoft.NETCore.App/10.0.12", StringComparison.Ordinal)
            || runtime.EndsWith("\\Microsoft.NETCore.App\\10.0.12", StringComparison.Ordinal)))
        {
            normal = false;
            state.Issue("NativeRuntimeUnsupported", $"Host {pid}: actual subject runtime location10.0.12 is required.", []);
        }
        Check(completions == 1 && completion is not null, "NativeCompletionMissing", "OS exit alone is not execution completion.");
        Check(aborts == 0 && cancels == 0, "NativeAbortOrCancel", $"Actual abort markers={aborts}, cancel markers={cancels}.");
        Check(request?.ControllerStopped == false, "NativeControllerStopped", "The actual parent must not request abort/cancel for this host.");
        Check(terminates == 1 && exiting == 1 && terminateLine > completeLine && exitingLine > terminateLine,
            "NativeTerminationMissing", "Complete, normal Terminate and exiting must occur once and in order.");
        bool hasExit = exits.TryGetValue(pid, out NativeExit? actualExit);
        if (hasExit && request is not null && (actualExit!.ControllerPid != request.ControllerPid || actualExit.Path != request.Path))
        {
            normal = false;
            state.Issue("NativeExitControllerMismatch", $"Host {pid}: OS-exit callback belongs to a different controller diagnostic.", []);
        }
        Check(hasExit && request is not null && actualExit!.ControllerPid == request.ControllerPid
            && actualExit.Path == request.Path && actualExit.Line > request.LaunchLine && actualExit.Code == 0 && actualExit.Error.Length == 0,
            "NativeExitNotNormal", "Actual launching-parent OS-exit callback0 without error is required.");
        if (completion is not null)
        {
            try
            {
                Check(!Boolean(completion, "IsAborted") && !Boolean(completion, "IsCanceled") && completion.ContainsKey("Error") && completion["Error"] is null,
                    "NativeCompletionFailure", "Completion is aborted/canceled or reports an error.");
                Check(finalActive is { Count: 0 }, "NativeActiveTestsRemain", "Completion must explicitly observe an empty active-test set after cleanup.");
                JsonObject statistics = Obj(completion["TestRunStatistics"]);
                JsonObject counts = Obj(statistics["Stats"]);
                int passed = results.Values.Count(value => value.Outcome == outcomes.Passed);
                int failed = results.Values.Count(value => value.Outcome == outcomes.Failed);
                Check(Integer(statistics, "ExecutedTests") == results.Count && Count(counts, "Passed") == passed && Count(counts, "Failed") == failed
                    && counts.All(pair => pair.Key is "Passed" or "Failed" || (pair.Value is JsonValue value && value.TryGetValue<int>(out int count) && count == 0)),
                    "NativeStatisticsMismatch", "Complete statistics must equal the unique actual GUID results; skipped/unknown outcomes do not qualify.");
            }
            catch (Exception error) when (EvidenceError(error))
            {
                normal = false;
                state.Issue("NativeCompletionMalformed", $"Host {pid}: {Bound(error.Message)}", []);
            }
        }
        Check(hostRun is not null && hostRun.Selected.SetEquals(results.Keys), "NativeResultSetIncomplete", "Actual unique results must equal the requested GUID set.");

        if (!normal || hostRun is null)
        {
            ObserveCompletedDirectAssertionBeforeLaterAbort(completedBeforeAbort, semantic, path, pid, request, hostRun, parents,
                runtime, malformed, diagnosticsNormal, starts, completions, aborts, cancels, completeLine, terminateLine, exitingLine,
                terminates, exiting, completion, finalActive, results, tests, definitions, outcomes, actualExit, state);
            return;
        }
        state.NormalHostCount++;
        if (ids.Length == 0)
        {
            if (results.Values.Any(result => result.Outcome != outcomes.Passed))
            {
                state.Issue("NativeBaselineFailure", $"Baseline/coverage host {pid} did not pass every selected case.", []);
            }
            return;
        }

        foreach ((string guid, NativeResult result) in results)
        {
            if (result.Outcome != outcomes.Failed) { continue; }
            string[] owners = hostRun.Selectors.Where(pair => pair.Value.Contains(guid)).Select(pair => pair.Key).ToArray();
            if (owners.Length != 1)
            {
                state.Issue("NativeFailureSelectorAmbiguous", $"Host {pid}, GUID {guid} has no unique exact per-mutant declaration.", []);
                continue;
            }
            string owner = owners[0];
            NativeDefinition definition = definitions[owner];
            if (definition.Status != "Killed" || !definition.KilledBy.Contains(guid))
            {
                state.Issue("NativeReportFailureMismatch", $"Host {pid}, GUID {guid} failed for its declared selector but is not reported in that raw Killed definition's killedBy.", [owner]);
                continue;
            }
            bool assertion = DirectAssertion(result, tests[guid]);
            if (!assertion)
            {
                if (HealthcheckMutationSemanticProofs.TryMatch(semantic, owner, guid, tests[guid].Method, tests[guid].SourceFile,
                    result.Message, result.Stack, out JsonObject? semanticWitness))
                {
                    state.ReviewedSemanticKilled.Add(owner);
                    state.SemanticWitnessCount++;
                    if (state.SemanticWitnessSamples.Count < 32)
                    {
                        semanticWitness!["hostPid"] = pid;
                        semanticWitness["controllerPid"] = request!.ControllerPid;
                        semanticWitness["hostPath"] = path;
                        semanticWitness["actualOsExitCode"] = actualExit!.Code;
                        semanticWitness["observedFailure"] = Bound(result.Message!);
                        state.SemanticWitnessSamples.Add(semanticWitness);
                    }
                    continue;
                }
                state.Issue("NativeFailureNotDirectAssertion", $"Host {pid}, GUID {guid} has neither a direct assertion nor an exact reviewed source-bound semantic witness; unreviewed exceptions, cleanup failures and deadlines supply no kill credit.", [owner]);
                continue;
            }
            state.AssertionKilled.Add(owner);
            state.WitnessCount++;
            if (state.WitnessSamples.Count < 32)
            {
                state.WitnessSamples.Add(new JsonObject
                {
                    ["mutantId"] = owner,
                    ["guid"] = guid,
                    ["method"] = tests[guid].Method,
                    ["hostPid"] = pid,
                    ["controllerPid"] = request!.ControllerPid,
                    ["hostPath"] = path,
                    ["directAssertion"] = Bound(result.Message!),
                    ["actualOsExitCode"] = actualExit!.Code
                });
            }
        }
    }

    private static bool DirectAssertion(NativeResult result, TestIdentity test)
    {
        if (string.IsNullOrWhiteSpace(result.Message) || string.IsNullOrWhiteSpace(result.Stack)
            || !result.Stack.Contains(test.Method + "(", StringComparison.Ordinal)
            || UnqualifiedFailure(result.Message, result.Stack)) { return false; }
        if (result.Message.StartsWith("Assertion failed.", StringComparison.Ordinal)) { return true; }

        // MSTest's collection assertions retain their older message format and
        // trim the framework assertion frames. Require the actual receiving
        // class/source origin and adapter boundary, not an exception wrapper or
        // an arbitrary message containing the words "CollectionAssert".
        if (!result.Message.StartsWith("CollectionAssert.AreEqual failed.", StringComparison.Ordinal)
            && !result.Message.StartsWith("CollectionAssert.AreEquivalent failed.", StringComparison.Ordinal)) { return false; }
        string[] frames = result.Stack.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(value => value.Trim()).ToArray();
        if (frames.Length < 2 || !frames[^1].StartsWith("at Microsoft.VisualStudio.TestPlatform.MSTest.TestAdapter.Execution.TestMethodInfo.ExecuteInternalAsync(", StringComparison.Ordinal)) { return false; }
        string first = frames[0], type = test.Method[..(test.Method.LastIndexOf('.') + 1)];
        int origin = first.LastIndexOf(" in ", StringComparison.Ordinal), line = first.LastIndexOf(":line ", StringComparison.Ordinal);
        return first.StartsWith("at " + type, StringComparison.Ordinal) && origin > 0 && line > origin
            && first[(origin + 4)..line].Replace('\\', '/') == test.SourceFile.Replace('\\', '/')
            && int.TryParse(first[(line + 6)..], NumberStyles.None, CultureInfo.InvariantCulture, out int observedLine) && observedLine > 0;
    }

    private static bool UnqualifiedFailure(string message, string stack)
    {
        string[] faults = ["AggregateException", "System.TimeoutException", "System.OperationCanceledException", "System.Threading.Tasks.TaskCanceledException",
            "TestCleanup", "ClassCleanup", "AssemblyCleanup", "TestInitialize", "ClassInitialize", "AssemblyInitialize", ".DisposeAsync(", ".Dispose("];
        return faults.Any(fault => message.Contains(fault, StringComparison.Ordinal) || stack.Contains(fault, StringComparison.Ordinal));
    }

    private static bool IsDiscoveryHost(string path)
    {
        foreach (string line in File.ReadLines(path))
        {
            CheckLine(line);
            int receive = line.IndexOf(ReceiveMarker, StringComparison.Ordinal);
            if (receive < 0) { continue; }
            string type = Text(Parse(line[(receive + ReceiveMarker.Length)..]), "MessageType");
            if (type == "TestDiscovery.Start") { return true; }
            if (type is "TestExecution.StartWithSources" or "TestExecution.StartWithTests") { return false; }
        }
        // Missing/unknown execution starts are rejected by the normal host reader.
        return false;
    }

    private static void ReadDiscoveryHost(string path, Dictionary<string, TestIdentity> tests, string filter, OutcomeMetadata outcomes,
        Dictionary<int, DiscoveryRequest> discoveries, Dictionary<int, NativeRequest> requests,
        Dictionary<int, NativeExit> exits, HashSet<int> observed, AuditState state, Dictionary<string, string> ownership,
        HealthcheckMutationNativeKernelEvidence? kernelEvidence)
    {
        int pid = 0, lineNumber = 0, starts = 0, completions = 0, completeLine = 0, terminates = 0, terminateLine = 0, exiting = 0, exitingLine = 0;
        string? runtime = null;
        var parents = new HashSet<int>();
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var foundOrigins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        DiscoverySpec? spec = null;
        JsonObject? complete = null;
        DiscoveryRequest? request = null;
        try
        {
            state.DiscoveryHostCount++;
            foreach (string line in File.ReadLines(path))
            {
                lineNumber++; CheckLine(line);
                int actualPid = LinePid(line);
                if (lineNumber == 1)
                {
                    pid = actualPid;
                    Require(pid > 0 && observed.Add(pid) && !requests.ContainsKey(pid), "Discovery PID is missing, duplicated or also used by mutation execution.");
                }
                if (actualPid > 0) { Require(actualPid == pid, "Mixed discovery host process identities."); }
                const string runtimeMarker = "Runtime location: ";
                int runtimeOffset = line.IndexOf(runtimeMarker, StringComparison.Ordinal);
                if (runtimeOffset >= 0)
                {
                    Require(runtime is null, "Duplicate discovery runtime observation.");
                    runtime = line[(runtimeOffset + runtimeMarker.Length)..]; state.RuntimeLocations.Add(runtime);
                }
                Match parent = ParentArgument.Match(line);
                if (!parent.Success) { parent = ParentMonitor.Match(line); }
                if (parent.Success) { parents.Add(Number(parent.Groups["pid"].Value)); }
                if (line.Contains("Testhost process exiting.", StringComparison.Ordinal)) { exiting++; exitingLine = lineNumber; }
                int receive = line.IndexOf(ReceiveMarker, StringComparison.Ordinal);
                if (receive >= 0)
                {
                    JsonObject message = Parse(line[(receive + ReceiveMarker.Length)..]);
                    string type = Text(message, "MessageType");
                    Require(!type.StartsWith("TestExecution.", StringComparison.Ordinal), "Discovery host contains a mutation/execution protocol message.");
                    if (type == "TestDiscovery.Start")
                    {
                        Require(++starts == 1 && completions == 0, "Duplicate or late discovery request.");
                        JsonObject payload = Obj(message["Payload"]);
                        Require(Text(payload, "TestCaseFilter") == filter, "Actual discovery payload filter differs.");
                        spec = ReadDiscoverySpec(payload, true, tests, filter);
                    }
                    else if (type == "TestSession.Terminate") { terminates++; terminateLine = lineNumber; }
                }
                int send = line.IndexOf(SendMarker, StringComparison.Ordinal);
                if (send < 0) { continue; }
                JsonObject sent = Parse(line[(send + SendMarker.Length)..]);
                string sentType = Text(sent, "MessageType");
                Require(!sentType.StartsWith("TestExecution.", StringComparison.Ordinal), "Execution protocol inside discovery.");
                if (sentType == "TestSession.Message")
                {
                    Require(InformationalMessage(sent, outcomes), "Actual warning/error diagnostic inside discovery.");
                    state.InformationalMessageCount++;
                    continue;
                }
                JsonArray? batch = null;
                if (sentType == "TestDiscovery.TestFound")
                { Require(completions == 0, "Discovery results follow completion."); batch = Arr(sent["Payload"]); }
                else if (sentType == "TestDiscovery.Completed")
                {
                    Require(++completions == 1, "Duplicate discovery completion.");
                    complete = Obj(sent["Payload"]); completeLine = lineNumber;
                    batch = Arr(complete["LastDiscoveredTests"]);
                }
                if (batch is null) { continue; }
                Require(starts == 1 && spec is not null && completions <= 1, "Discovery results lack a unique requested source cohort.");
                foreach (JsonNode? test in batch)
                {
                    JsonObject testCase = Obj(test);
                    string guid = ValidateTest(testCase, tests);
                    Require(spec.Selected.Contains(guid) && found.Add(guid), "Unknown, duplicate or unrequested discovery GUID.");
                    string actualSource = Origin(Text(testCase, "Source"));
                    Require(spec.Sources.Contains(actualSource) && foundOrigins.TryAdd(guid, actualSource), "Actual typed discovery Source differs from its own requested DLL origin.");
                }
            }
            Require(parents.Count == 1 && discoveries.TryGetValue(parents.Single(), out request) && request.Trusted
                && (!request.SequentialControllerClosureRequired || request.ControllerClosed), "Discovery has no unique trusted typed parent request with required sequential closure.");
            Require(spec is not null && spec.Sources.IsSubsetOf(request.Spec.Sources), "Discovery source origins differ from its actual parent.");
            Require(request.LaunchLines.Count > 0, "Discovery has no actual standard launch.");
            JsonObject? kernelProof = null;
            if (exits.TryGetValue(pid, out NativeExit? exit))
            {
                Require(exit.ControllerPid == request.ControllerPid && exit.Path == request.Path && exit.Line > request.LaunchLines.Min()
                    && exit.Code == 0 && exit.Error.Length == 0, "Discovery actual parent OS-exit callback is contradictory.");
            }
            else
            {
                Require(!request.SequentialControllerClosureRequired && request.LaunchLines.Count == 1 && kernelEvidence is not null && runtime is not null
                    && kernelEvidence.TryConfirmDiscoveryHost(pid, request.ControllerPid, path, request.Path, spec.Sources, runtime, out kernelProof),
                    "Discovery lacks both its parent OS-exit callback and a complete pinned standard-discovery kernel exit proof.");
            }
            Require(runtime is not null && (runtime.EndsWith("/Microsoft.NETCore.App/10.0.12", StringComparison.Ordinal)
                || runtime.EndsWith("\\Microsoft.NETCore.App\\10.0.12", StringComparison.Ordinal)), "Discovery subject runtime is not observed10.0.12.");
            Require(starts == 1 && completions == 1 && complete is not null && !Boolean(complete, "IsAborted")
                && Integer(complete, "TotalTests") == found.Count && DiscoverySelectionComplete(found, spec, tests), "Discovery is aborted, partial or missing declared GUID results.");
            foreach (string field in new[] { "PartiallyDiscoveredSources", "NotDiscoveredSources", "SkippedDiscoverySources" })
            { Require(Arr(complete[field]).Count == 0, "Discovery has a partial, skipped or undiscovered source."); }
            var finishedSources = new HashSet<string>(Arr(complete["FullyDiscoveredSources"]).Select(node => Origin(ScalarText(node))), StringComparer.Ordinal);
            Require(finishedSources.Count == Arr(complete["FullyDiscoveredSources"]).Count && finishedSources.SetEquals(spec.Sources), "Discovery complete source identities differ or repeat.");
            Require(terminates == 1 && exiting == 1 && terminateLine > completeLine && exitingLine > terminateLine, "Discovery lacks normal Complete/Terminate/exiting order.");
            Require(!request.ObservedSources.Overlaps(spec.Sources), "Duplicate discovery source host.");
            foreach ((string guid, string origin) in foundOrigins)
            { Require(ownership.TryAdd(guid, origin), "A declared discovery GUID has duplicate or ambiguous project ownership."); }
            request.ObservedSources.UnionWith(spec.Sources); request.HostCount++;
            state.NormalDiscoveryHostCount++;
            if (kernelProof is not null) { state.KernelDiscoveryExitProofs.Add(kernelProof); }
            state.DiscoveredGuidCount += found.Count;
        }
        catch (Exception error) when (EvidenceError(error))
        {
            if (request is not null) { request.Trusted = false; }
            state.Issue("NativeDiscoveryMalformed", $"{path}:{lineNumber}: {Bound(error.Message)}", []);
        }
    }

    private static DiscoverySpec ReadDiscoverySpec(JsonObject payload, bool sourceMap, Dictionary<string, TestIdentity> tests, string filter)
    {
        using var input = new StringReader(Text(payload, "RunSettings"));
        using XmlReader xml = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 32 * 1024 * 1024 });
        XDocument document = XDocument.Load(xml);
        XElement root = document.Root ?? throw Invalid("Discovery RunSettings are missing.");
        Require(root.Name == "RunSettings" && root.Element("RunConfiguration")?.Element("TestCaseFilter")?.Value == filter,
            "Discovery filter differs from its pinned full configuration.");
        Require(!root.Descendants().Any(element => element.Name.LocalName is "Mutant" or "MutantControl"), "Discovery must not activate a mutant selector.");
        var sources = new HashSet<string>(StringComparer.Ordinal);
        IEnumerable<JsonNode?> rawSources = sourceMap ? Obj(payload["AdapterSourceMap"]).SelectMany(pair => Arr(pair.Value)) : Arr(payload["Sources"]);
        foreach (JsonNode? source in rawSources) { Require(sources.Add(Origin(ScalarText(source))), "Duplicate discovery source origin."); }
        Require(sources.Count > 0 && sources.All(source => tests.Values.Any(test => test.Sources.Contains(source))), "Discovery DLL source is outside the original declared test projects.");
        var selected = new HashSet<string>(tests.Where(pair => pair.Value.Sources.Overlaps(sources)).Select(pair => pair.Key), StringComparer.OrdinalIgnoreCase);
        Require(selected.Count > 0, "Discovery selected cohort is empty.");
        return new DiscoverySpec(sources, selected);
    }

    private static RunSpec ReadRun(JsonObject payload, string? caseProperty, string sourceProperty,
        Dictionary<string, TestIdentity> tests, Dictionary<string, NativeDefinition> definitions, string filter)
    {
        string settings = Text(payload, "RunSettings");
        using var input = new StringReader(settings);
        using XmlReader xml = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 32 * 1024 * 1024 });
        XDocument document = XDocument.Load(xml);
        XElement root = document.Root ?? throw Invalid("RunSettings root is missing.");
        Require(root.Name == "RunSettings", "Unknown RunSettings namespace/root.");
        XElement configuration = root.Element("RunConfiguration") ?? throw Invalid("RunConfiguration is missing.");
        Require(configuration.Element("TestCaseFilter")?.Value == filter, "Actual native test filter differs from pinned configuration.");
        XElement framework = configuration.Element("TargetFrameworkVersion") ?? throw Invalid("Native target framework is missing.");
        Require(framework.Value is "net10.0" or ".NETCoreApp,Version=v10.0", "Unexpected native subject framework.");
        framework.Value = "net10.0";
        root.Elements("LoggerRunSettings").Remove();
        // VSTest reindents the settings it forwards. Preserve scalar/CDATA text
        // and xml:space; remove only formatting between actual child elements.
        foreach (XText text in root.DescendantNodes().OfType<XText>().ToArray())
        {
            if (text is not XCData && string.IsNullOrWhiteSpace(text.Value) && text.Parent?.HasElements == true
                && text.Parent.AncestorsAndSelf().Select(element => element.Attribute(XNamespace.Xml + "space")?.Value).FirstOrDefault(value => value is not null) != "preserve")
            { text.Remove(); }
        }

        var sources = new HashSet<string>(StringComparer.Ordinal);
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (caseProperty is not null)
        {
            foreach (JsonNode? test in Arr(payload[caseProperty]))
            {
                JsonObject testCase = Obj(test);
                Require(selected.Add(ValidateTest(testCase, tests)), "Duplicate requested native test GUID.");
                sources.Add(Origin(Text(testCase, "Source")));
            }
        }
        else
        {
            if (sourceProperty == "AdapterSourceMap")
            {
                foreach ((string _, JsonNode? value) in Obj(payload[sourceProperty]))
                {
                    foreach (JsonNode? source in Arr(value)) { Require(sources.Add(Origin(ScalarText(source))), "Duplicate native source DLL."); }
                }
            }
            else
            {
                foreach (JsonNode? source in Arr(payload[sourceProperty])) { Require(sources.Add(Origin(ScalarText(source))), "Duplicate requested source DLL."); }
            }
            foreach ((string guid, TestIdentity test) in tests)
            {
                if (test.Sources.Overlaps(sources)) { selected.Add(guid); }
            }
        }
        Require(selected.Count > 0 && sources.Count > 0, "Native request has no declared tests/source.");
        Require(sources.All(source => tests.Values.Any(test => test.Sources.Contains(source))), "Native source DLL is outside the declared report/test-project origin.");
        var selectors = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (XElement mutant in root.Descendants().Where(element => element.Name.LocalName == "Mutant"))
        {
            Require(mutant.Name == "Mutant" && mutant.Parent?.Name == "Parameters" && mutant.Parent.Parent?.Name == "Configuration"
                && mutant.Parent.Parent.Parent?.Name == "InProcDataCollector", "Mutant selector is outside the actual data-collector parameters.");
            string id = mutant.Attribute("id")?.Value ?? throw Invalid("Mutant selector ID is missing.");
            Require(definitions.TryGetValue(id, out NativeDefinition? definition) && definition.Status is "Killed" or "Survived" or "Timeout" or "NoCoverage", "Unknown or filtered native mutant selector.");
            string declaration = mutant.Attribute("tests")?.Value ?? throw Invalid("Mutant per-test declaration is missing.");
            var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            IEnumerable<string> declaredGuids = declaration.Length == 0 ? selected : declaration.Split(',');
            foreach (string guid in declaredGuids)
            {
                string canonical = GuidText(guid);
                Require(selected.Contains(canonical) && declared.Add(canonical), "Unknown, duplicate or unrequested per-mutant GUID declaration.");
            }
            Require(declared.Count > 0 && selectors.TryAdd(id, declared), "Empty or duplicate mutant declaration.");
        }
        if (selectors.Count > 0)
        {
            var owners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (HashSet<string> declared in selectors.Values)
            {
                foreach (string guid in declared) { Require(owners.Add(guid), "Grouped mutants overlap test GUIDs; sibling attribution is ambiguous."); }
            }
            Require(owners.SetEquals(selected), "Selected GUIDs are not exactly covered by the per-mutant declarations.");
            Require(root.Descendants("MutantControl").Count() == 1, "Native selector controller is missing or ambiguous.");
        }
        string canonicalSettings = document.ToString(SaveOptions.DisableFormatting);
        string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalSettings)));
        return new RunSpec(hash, selected, sources, selectors);
    }

    private static Dictionary<string, NativeDefinition> Definitions(JsonObject report)
    {
        var result = new Dictionary<string, NativeDefinition>(StringComparer.Ordinal);
        foreach ((string _, JsonNode? file) in Obj(report["files"]))
        {
            foreach (JsonNode? node in Arr(Obj(file)["mutants"]))
            {
                JsonObject definition = Obj(node);
                string id = Text(definition, "id");
                var killedBy = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (definition["killedBy"] is JsonArray killed)
                {
                    foreach (JsonNode? guid in killed) { Require(killedBy.Add(GuidText(ScalarText(guid))), "Duplicate reported killedBy GUID."); }
                }
                Require(result.TryAdd(id, new NativeDefinition(id, Text(definition, "status"), killedBy)), "Duplicate report mutant ID.");
            }
        }
        Require(result.Count > 0, "Native report contains no definitions.");
        return result;
    }

    private static Dictionary<string, TestIdentity> Tests(JsonObject report, JsonObject options)
    {
        string[] projects = Arr(options["test-projects"]).Select(node => Path.GetFileNameWithoutExtension(ScalarText(node).Replace('\\', '/'))).ToArray();
        Require(projects.Length > 0 && projects.All(name => !string.IsNullOrWhiteSpace(name)), "Test project identities are missing.");
        string configuration = Text(options, "configuration"), framework = Text(options, "target-framework");
        var result = new Dictionary<string, TestIdentity>(StringComparer.OrdinalIgnoreCase);
        foreach ((string path, JsonNode? node) in Obj(report["testFiles"]))
        {
            JsonArray tests = Arr(Obj(node)["tests"]);
            if (tests.Count == 0) { continue; }
            string source = Origin(path);
            int testRoot = source.LastIndexOf("/tests/", StringComparison.OrdinalIgnoreCase);
            Require(testRoot >= 0, "Native test metadata has an unsupported source origin.");
            string root = source[..testRoot];
            string[] owners = projects.Where(project => source.Contains("/" + project + "/", StringComparison.OrdinalIgnoreCase)).ToArray();
            // Linked TestAssembly.cs is compiled into each selected family.
            string[] allowed = owners.Length == 0 ? projects : owners;
            var dlls = new HashSet<string>(allowed.Select(project => Origin($"{root}/tests/{project}/bin/{configuration}/{framework}/{project}.dll")), StringComparer.Ordinal);
            foreach (JsonNode? testNode in tests)
            {
                JsonObject test = Obj(testNode);
                string id = GuidText(Text(test, "id"));
                Require(result.TryAdd(id, new TestIdentity(Text(test, "name"), source, dlls)), "Duplicate declared report GUID.");
            }
        }
        Require(result.Count > 0, "Original report lacks declared test GUID/method metadata.");
        return result;
    }

    private static string ValidateTest(JsonObject testCase, Dictionary<string, TestIdentity> tests)
    {
        string id = GuidText(Text(testCase, "Id"));
        Require(tests.TryGetValue(id, out TestIdentity? expected), "Unknown native test GUID.");
        Require(Text(testCase, "FullyQualifiedName") == expected.Method, "Actual native GUID/method differs from the report.");
        Require(Origin(Text(testCase, "CodeFilePath")) == expected.SourceFile && expected.Sources.Contains(Origin(Text(testCase, "Source"))), "Native GUID source/compiled DLL origin differs from the declared report project.");
        return id;
    }

    private static OutcomeMetadata ReadOutcomeMetadata(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        AssemblyDefinition assembly = reader.GetAssemblyDefinition();
        Require(reader.GetString(assembly.Name) == "Microsoft.VisualStudio.TestPlatform.ObjectModel", "Unknown ObjectModel assembly identity.");
        Dictionary<string, int> values = ReadEnumValues(reader, "Microsoft.VisualStudio.TestPlatform.ObjectModel", "TestOutcome");
        Dictionary<string, int> levels = ReadEnumValues(reader, "Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging", "TestMessageLevel");
        bool hasPassed = values.TryGetValue("Passed", out int passed);
        bool hasFailed = values.TryGetValue("Failed", out int failed);
        bool hasInformational = levels.TryGetValue("Informational", out int informational);
        bool hasWarning = levels.TryGetValue("Warning", out int warning);
        bool hasError = levels.TryGetValue("Error", out int error);
        Require(hasPassed && hasFailed && hasInformational && hasWarning && hasError,
            "Actual pinned outcome/message enums cannot be resolved unambiguously.");
        return new OutcomeMetadata(passed, failed, informational, warning, error, reader.GetString(assembly.Name), assembly.Version.ToString(), reader.GetGuid(reader.GetModuleDefinition().Mvid).ToString());
    }

    private static Dictionary<string, int> ReadEnumValues(MetadataReader reader, string typeNamespace, string typeName)
    {
        var values = new Dictionary<string, int>(StringComparer.Ordinal);
        int enumTypes = 0;
        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
        {
            TypeDefinition type = reader.GetTypeDefinition(handle);
            if (reader.GetString(type.Namespace) != typeNamespace || reader.GetString(type.Name) != typeName) { continue; }
            enumTypes++;
            Require(type.BaseType.Kind == HandleKind.TypeReference, "Outcome metadata is not an enum.");
            TypeReference baseType = reader.GetTypeReference((TypeReferenceHandle)type.BaseType);
            Require(reader.GetString(baseType.Namespace) == "System" && reader.GetString(baseType.Name) == "Enum", "Outcome base type is unsupported.");
            foreach (FieldDefinitionHandle fieldHandle in type.GetFields())
            {
                FieldDefinition field = reader.GetFieldDefinition(fieldHandle);
                string name = reader.GetString(field.Name);
                if (name == "value__")
                {
                    Require(reader.GetBlobBytes(field.Signature).AsSpan().SequenceEqual(new byte[] { 0x06, 0x08 }), "Outcome underlying type is not Int32.");
                    continue;
                }
                ConstantHandle constantHandle = field.GetDefaultValue();
                Require(!constantHandle.IsNil, "Outcome enum field lacks actual constant metadata.");
                Constant constant = reader.GetConstant(constantHandle);
                Require(constant.TypeCode == ConstantTypeCode.Int32, "Outcome enum constant type is unsupported.");
                BlobReader blob = reader.GetBlobReader(constant.Value);
                Require(values.TryAdd(name, blob.ReadInt32()) && blob.RemainingBytes == 0, "Duplicate or malformed outcome enum constant.");
            }
        }
        Require(enumTypes == 1 && values.Count > 0 && values.Values.Distinct().Count() == values.Count, "Actual pinned enum cannot be resolved unambiguously.");
        return values;
    }

    private static bool InformationalMessage(JsonObject message, OutcomeMetadata metadata)
    {
        JsonObject payload = Obj(message["Payload"]);
        int level = Integer(payload, "MessageLevel");
        _ = Text(payload, "Message");
        Require(level == metadata.Informational || level == metadata.Warning || level == metadata.Error, "Unknown actual diagnostic message level.");
        return level == metadata.Informational;
    }

    private static int Outcome(JsonNode? node, OutcomeMetadata metadata)
    {
        Require(node is JsonValue value && value.TryGetValue<int>(out _), "Unsupported native outcome encoding; actual numeric metadata is required.");
        int result = node!.GetValue<int>();
        Require(result == metadata.Passed || result == metadata.Failed, "Skipped/unknown native outcome is not a qualified assertion result.");
        return result;
    }

    private static int Count(JsonObject values, string key) => values[key] is null ? 0 : Integer(values, key);
    private static string Origin(string path)
    {
        string normalized = path.Replace('\\', '/');
        return normalized.Length >= 2 && normalized[1] == ':' ? normalized.ToUpperInvariant() : normalized;
    }
    private static string GuidText(string text) => Guid.TryParseExact(text, "D", out Guid value) && value != Guid.Empty ? value.ToString("D") : throw Invalid("Invalid test GUID.");
    private static int LinePid(string line) => ProcessPrefix.Match(line) is { Success: true } match ? Number(match.Groups["pid"].Value) : 0;
    private static int Number(string text) => int.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    private static JsonObject Parse(string text) => Obj(JsonNode.Parse(text));
    private static JsonObject Obj(JsonNode? value) => value as JsonObject ?? throw Invalid("Expected native JSON object.");
    private static JsonArray Arr(JsonNode? value) => value as JsonArray ?? throw Invalid("Expected native JSON array.");
    private static string Text(JsonObject value, string key) => ScalarText(value[key]);
    private static string ScalarText(JsonNode? value) => value is JsonValue scalar && scalar.TryGetValue<string>(out string? text) && text is not null ? text : throw Invalid("Expected native string.");
    private static string? OptionalText(JsonObject value, string key) => value[key] is null ? null : ScalarText(value[key]);
    private static int Integer(JsonObject value, string key) => value[key] is JsonValue scalar && scalar.TryGetValue<int>(out int number) ? number : throw Invalid("Expected native Int32.");
    private static bool Boolean(JsonObject value, string key) => value[key] is JsonValue scalar && scalar.TryGetValue<bool>(out bool result) ? result : throw Invalid("Expected native Boolean.");
    private static bool Empty(JsonNode? value) => value is null || (value is JsonValue scalar && scalar.TryGetValue<string>(out string? text) && string.IsNullOrEmpty(text));
    private static void CheckLine(string line) => Require(line.Length <= 32 * 1024 * 1024, "Native diagnostic line exceeds the bounded supported record size.");
    private static string Bound(string text) => text.Length <= 400 ? text : text[..400];
    private static InvalidDataException Invalid(string message) => new(message);
    private static void Require([DoesNotReturnIf(false)] bool condition, string message) { if (!condition) { throw Invalid(message); } }
    private static bool EvidenceError(Exception error) => error is InvalidDataException or IOException or UnauthorizedAccessException or JsonException or XmlException
        or InvalidOperationException or ArgumentException or FormatException or OverflowException or BadImageFormatException or RegexMatchTimeoutException;

    private sealed record NativeDefinition(string Id, string Status, HashSet<string> KilledBy);
    private sealed record TestIdentity(string Method, string SourceFile, HashSet<string> Sources);
    private sealed record OutcomeMetadata(int Passed, int Failed, int Informational, int Warning, int Error, string Assembly, string Version, string Mvid);
    private sealed record NativeResult(int Outcome, string? Message, string? Stack);
    private sealed record NativeExit(int ControllerPid, int Code, string Error, int Line, string Path);
    private sealed record DiscoverySpec(HashSet<string> Sources, HashSet<string> Selected);
    private sealed record DiscoveryRequest(int ControllerPid, string Path, DiscoverySpec Spec)
    {
        internal readonly List<int> LaunchLines = [];
        internal readonly HashSet<string> ObservedSources = new(StringComparer.Ordinal);
        internal bool Trusted { get; set; } = true;
        internal bool ControllerClosed { get; set; }
        internal bool SequentialControllerClosureRequired { get; set; }
        internal int HostCount { get; set; }
    }
    private sealed record NativeRequest(int ControllerPid, int RequestLine, RunSpec Run, string Path)
    {
        internal int LaunchLine { get; set; }
        internal bool ControllerTrusted { get; set; } = true;
        internal bool ControllerStopped { get; set; }
        internal string[] Ids => Run.Selectors.Keys.Order(StringComparer.Ordinal).ToArray();
    }
    private sealed record RunSpec(string SettingsHash, HashSet<string> Selected, HashSet<string> Sources,
        Dictionary<string, HashSet<string>> Selectors)
    {
        internal bool SameAs(RunSpec other) => SettingsHash == other.SettingsHash && Selected.SetEquals(other.Selected)
            && Sources.SetEquals(other.Sources) && Selectors.Count == other.Selectors.Count
            && Selectors.All(pair => other.Selectors.TryGetValue(pair.Key, out HashSet<string>? ids) && pair.Value.SetEquals(ids));
    }
    private sealed class AuditState
    {
        internal readonly HashSet<string> AssertionKilled = new(StringComparer.Ordinal);
        internal readonly HashSet<string> ReviewedSemanticKilled = new(StringComparer.Ordinal);
        internal readonly HashSet<string> CompletedBeforeAbortKilled = new(StringComparer.Ordinal);
        internal readonly HashSet<string> RuntimeLocations = new(StringComparer.Ordinal);
        internal readonly HashSet<int> ControllerProcesses = [];
        internal readonly List<IntegrityIssue> Issues = [];
        internal readonly JsonArray WitnessSamples = [];
        internal readonly JsonArray SemanticWitnessSamples = [];
        internal readonly JsonArray AbortedTailSamples = [];
        internal readonly JsonArray KernelDiscoveryExitProofs = [];
        internal readonly JsonArray CompletedBeforeAbortWitnesses = [];
        internal JsonObject? OutcomeMetadata;
        internal int HostCount, NormalHostCount, WitnessCount, SemanticWitnessCount, DiscoveryHostCount, NormalDiscoveryHostCount, DiscoveredGuidCount, InformationalMessageCount;

        internal void Issue(string code, string detail, string[] ids) => Issues.Add(new IntegrityIssue(code, detail, ids));
        internal NativeAuditResult Result()
        {
            var counts = new JsonObject();
            foreach (IGrouping<string, IntegrityIssue> group in Issues.GroupBy(issue => issue.Code)) { counts[group.Key] = group.Count(); }
            return new NativeAuditResult(AssertionKilled.Order(StringComparer.Ordinal).ToArray(), Issues.ToArray(), HostCount, NormalHostCount,
                new JsonObject
                {
                    ["scope"] = "Original native controller/host protocol only; source pins, child origin, adjustments and union approval remain separate.",
                    ["hostCount"] = HostCount,
                    ["normalHostCount"] = NormalHostCount,
                    ["directAssertionWitnessCount"] = WitnessCount,
                    ["reviewedSemanticWitnessCount"] = SemanticWitnessCount,
                    ["directAssertionKilledDefinitionCount"] = AssertionKilled.Count,
                    ["reviewedSemanticKilledDefinitionCount"] = ReviewedSemanticKilled.Count,
                    ["qualifiedKilledDefinitionCount"] = AssertionKilled.Union(ReviewedSemanticKilled, StringComparer.Ordinal).Union(CompletedBeforeAbortKilled, StringComparer.Ordinal).Count(),
                    ["discoveryHostCount"] = DiscoveryHostCount,
                    ["normalDiscoveryHostCount"] = NormalDiscoveryHostCount,
                    ["discoveredGuidCount"] = DiscoveredGuidCount,
                    ["informationalMessageCount"] = InformationalMessageCount,
                    ["outcomeMetadata"] = OutcomeMetadata,
                    ["runtimeLocations"] = new JsonArray(RuntimeLocations.Order(StringComparer.Ordinal).Select(location => (JsonNode?)JsonValue.Create(location)).ToArray()),
                    ["issueCounts"] = counts,
                    ["witnessSamplesMaximum"] = 32,
                    ["witnessSamples"] = WitnessSamples,
                    ["reviewedSemanticWitnessSamples"] = SemanticWitnessSamples,
                    ["reviewedUnqualifiedAbortedTails"] = AbortedTailSamples,
                    ["kernelDiscoveryExitProofCount"] = KernelDiscoveryExitProofs.Count,
                    ["kernelDiscoveryExitProofs"] = KernelDiscoveryExitProofs,
                    ["completedBeforeAbortWitnessCount"] = CompletedBeforeAbortWitnesses.Count,
                    ["completedBeforeAbortKilledDefinitionCount"] = CompletedBeforeAbortKilled.Count,
                    ["completedBeforeAbortWitnesses"] = CompletedBeforeAbortWitnesses,
                    ["rawReportsRewritten"] = false,
                    ["scoreComputed"] = false
                })
            {
                ReviewedSemanticKilledIds = ReviewedSemanticKilled.Order(StringComparer.Ordinal).ToArray(),
                CompletedBeforeAbortKilledIds = CompletedBeforeAbortKilled.Order(StringComparer.Ordinal).ToArray()
            };
        }
    }
}
