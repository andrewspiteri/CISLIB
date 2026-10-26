using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CIS.Verification;

// Supplies diagnostic ownership, never a normal host or kill. The ordinary
// Audit entry point has one fixed source/report/host/controller case only.
internal static partial class HealthcheckMutationNativeAudit
{
    internal sealed record AbortedTailCase(string ReportSha256, string DefinitionIdentity, string Id,
        string HostSha256, string ControllerSha256, int SelectedCount, int PreResultCount, int PreFailedCount)
    {
        internal static AbortedTailCase Unit18Selector1904 { get; } = new(
            "056cc405715ac814bbad9ff45e276e77708c56463beea24ad76e23d19f3bf89d",
            "bce78201212534522fee814c6fafa733cebf75dbd70272070aa75ae16b037fb5", "1904",
            "9813c2333f06a95bd1508067144d850d2202061393952d320f2dbbc971f193a5",
            "f46500a308dcadbf1aeefb7678a5e268e987acdcf7b2f43c0d454f94bf17dfb8", 214, 46, 43);
    }

    private sealed record AbortedTailReceipt(int TerminateLine, int ExitingLine, JsonObject Observation);
    private static readonly Regex NativeClock = new(@", (?<counter>[0-9]+), (?:testhost|vstest\.console)\.dll,",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex SessionBudget = new(@"testSessionTimeout (?<budget>[0-9]+) milliseconds\z",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static bool TryAttributeReviewedAbortedTail(AbortedTailCase review, HealthcheckMutationSemanticProofs.Context? context,
        string path, NativeRequest? request, RunSpec? firstRun, Dictionary<string, TestIdentity> tests,
        Dictionary<string, NativeDefinition> definitions, string filter, OutcomeMetadata outcomes,
        Dictionary<int, NativeExit> exits, int rejectedLine, Dictionary<string, NativeResult> firstResults,
        out AbortedTailReceipt? receipt)
    {
        receipt = null;
        try
        {
            Require(context is not null && context.ReportSha256 == review.ReportSha256
                && context.Definitions.TryGetValue(review.Id, out var definition)
                && definition.Identity == review.DefinitionIdentity && definition.Status == "Killed",
                "The one reviewed original source/report definition is required.");
            Require(request is not null && request.ControllerTrusted && firstRun is not null
                && request.Ids.SequenceEqual([review.Id]) && request.Run.SameAs(firstRun),
                "An actual unique trusted sole-selector parent/host request is required.");
            Require(HashNative(path) == review.HostSha256 && HashNative(request.Path) == review.ControllerSha256,
                "The exact reviewed original host/controller bytes are required.");
            JsonObject[] raw = Obj(context.Report["files"]).SelectMany(pair => Arr(Obj(pair.Value)["mutants"]))
                .Select(Obj).Where(value => Text(value, "id") == review.Id).ToArray();
            Require(raw.Length == 1 && Text(raw[0], "status") == "Killed", "Original raw selector binding differs.");
            var covered = new HashSet<string>(Arr(raw[0]["coveredBy"]).Select(value => GuidText(ScalarText(value))), StringComparer.OrdinalIgnoreCase);
            Require(covered.Count == Arr(raw[0]["coveredBy"]).Count && covered.Count == review.SelectedCount && request.Run.Selected.SetEquals(covered)
                && request.Run.Selectors[review.Id].SetEquals(covered), "The complete original covered/requested GUID closure differs.");
            int actualHostPid = HostPid(path);
            Require(exits.TryGetValue(actualHostPid, out NativeExit? actualExit) && actualExit.ControllerPid == request.ControllerPid
                && actualExit.Path == request.Path && actualExit.Line > request.LaunchLine && actualExit.Code == 0 && actualExit.Error.Length == 0,
                "The actual unique launching-parent OSexit0 is required.");

            // Read only the actual controller request's interval. No FIFO pairing
            // or guessing that an unrelated Abort belongs to this host is used.
            int controllerLine = 0, timeouts = 0, sends = 0, timeoutLine = 0, sendLine = 0;
            long timeoutTick = 0, sendTick = 0, exitTick = 0;
            int expectedBudget = 0;
            foreach (string line in File.ReadLines(request.Path))
            {
                controllerLine++;
                if (controllerLine < request.RequestLine) { continue; }
                if (controllerLine > actualExit.Line) { break; }
                CheckLine(line);
                int parsedPid = LinePid(line);
                if (parsedPid == 0)
                {
                    Require(!line.Contains(RequestMarker, StringComparison.Ordinal) && !line.Contains("TestRunRequest.OnTestSessionTimeout:", StringComparison.Ordinal)
                        && !line.Contains("TestRequestSender.SendTestRunAbort:", StringComparison.Ordinal), "Unassigned controller protocol line.");
                    continue;
                }
                Require(parsedPid == request.ControllerPid, "Controller interval PID differs.");
                int typedOffset = line.IndexOf(RequestMarker, StringComparison.Ordinal);
                if (typedOffset >= 0)
                {
                    int json = line.IndexOf('{', typedOffset); Require(json >= 0, "Controller typed envelope is missing.");
                    JsonObject typed = Parse(line[json..]);
                    if (controllerLine == request.RequestLine)
                    { Require(Text(typed, "MessageType") == "TestExecution.GetTestRunnerProcessStartInfoForRunSelected", "Unexpected original request type."); }
                    else if (controllerLine == request.LaunchLine)
                    {
                        JsonObject launch = Obj(typed["Payload"]);
                        Require(Text(typed, "MessageType") == "TestExecution.CustomTestHostLaunchCallback"
                            && Integer(launch, "HostProcessId") == actualHostPid && Empty(launch["ErrorMessage"]), "Unexpected original launch callback.");
                    }
                    else { throw Invalid("Unknown or overlapping controller request in the reviewed host interval."); }
                }
                if (controllerLine == request.RequestLine)
                {
                    int json = line.IndexOf('{'); Require(json >= 0, "Original request envelope is missing.");
                    JsonObject payload = Obj(Parse(line[json..])["Payload"]);
                    XDocument xml = XDocument.Parse(Text(payload, "RunSettings"));
                    Require(int.TryParse(xml.Root?.Element("RunConfiguration")?.Element("TestSessionTimeout")?.Value,
                        NumberStyles.None, CultureInfo.InvariantCulture, out expectedBudget) && expectedBudget > 0,
                        "Actual native session budget is missing.");
                }
                if (line.Contains("TestRunRequest.OnTestSessionTimeout: calling cancellation as test run exceeded testSessionTimeout ", StringComparison.Ordinal))
                {
                    Match budget = SessionBudget.Match(line);
                    Require(budget.Success && Number(budget.Groups["budget"].Value) == expectedBudget, "Actual timeout budget differs.");
                    timeouts++; timeoutLine = controllerLine; timeoutTick = Tick(line);
                }
                if (line.Contains("TestRequestSender.SendTestRunAbort: Sending test run abort.", StringComparison.Ordinal))
                { sends++; sendLine = controllerLine; sendTick = Tick(line); }
                Require(!line.Contains("TestRunRequest.Cancel:", StringComparison.Ordinal)
                    && !line.Contains("\"MessageType\":\"TestExecution.Cancel\"", StringComparison.Ordinal), "Cancellation is not this reviewed abort sequence.");
                if (controllerLine == actualExit.Line) { exitTick = Tick(line); }
            }
            Require(timeouts == 1 && sends == 1 && timeoutLine > request.LaunchLine && sendLine > timeoutLine
                && actualExit.Line > sendLine && timeoutTick <= sendTick, "One ordered native budget-expiration/Abort interval is required.");

            int lineNumber = 0, starts = 0, aborts = 0, completions = 0, lateBatches = 0, terminates = 0, exiting = 0, protocols = 0, initializations = 0, runtimes = 0;
            int completeLine = 0, lateLine = 0, terminateLine = 0, exitingLine = 0;
            long abortTick = 0, completeTick = 0, lateTick = 0, terminateTick = 0, exitingTick = 0;
            var all = new Dictionary<string, NativeResult>(StringComparer.OrdinalIgnoreCase);
            var before = new Dictionary<string, NativeResult>(StringComparer.OrdinalIgnoreCase);
            var late = new Dictionary<string, NativeResult>(StringComparer.OrdinalIgnoreCase);
            var activeAfter = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var parents = new HashSet<int>();
            foreach (string line in File.ReadLines(path))
            {
                lineNumber++; CheckLine(line);
                int parsedPid = LinePid(line);
                if (parsedPid == 0)
                {
                    Require(!line.Contains(ReceiveMarker, StringComparison.Ordinal) && !line.Contains(SendMarker, StringComparison.Ordinal)
                        && !line.Contains("Testhost process exiting.", StringComparison.Ordinal), "Unassigned host protocol line.");
                    continue;
                }
                Require(parsedPid == actualHostPid, "Mixed host PID in the reviewed tail.");
                Match parent = ParentArgument.Match(line);
                if (!parent.Success) { parent = ParentMonitor.Match(line); }
                if (parent.Success) { parents.Add(Number(parent.Groups["pid"].Value)); }
                const string runtimeMarker = "Runtime location: ";
                int runtimeOffset = line.IndexOf(runtimeMarker, StringComparison.Ordinal);
                if (runtimeOffset >= 0)
                {
                    runtimes++;
                    Require(line[(runtimeOffset + runtimeMarker.Length)..].Replace('\\', '/').EndsWith("/Microsoft.NETCore.App/10.0.12", StringComparison.Ordinal),
                        "Actual subject runtime is outside the reviewed version.");
                }
                if (line.Contains("Testhost process exiting.", StringComparison.Ordinal))
                { exiting++; exitingLine = lineNumber; exitingTick = Tick(line); }
                int receive = line.IndexOf(ReceiveMarker, StringComparison.Ordinal);
                if (receive >= 0)
                {
                    JsonObject message = Parse(line[(receive + ReceiveMarker.Length)..]);
                    string type = Text(message, "MessageType");
                    switch (type)
                    {
                        case "ProtocolVersion":
                            protocols++;
                            Require(protocols == 1 && starts == 0 && completions == 0 && ValidProtocolVersion(message), "Unexpected protocol initialization."); break;
                        case "TestExecution.Initialize":
                            initializations++;
                            Require(initializations == 1 && starts == 0 && completions == 0, "Initialization outside its original phase."); break;
                        case "TestExecution.StartWithTests":
                            starts++;
                            Require(starts == 1 && completions == 0 && request.Run.SameAs(ReadRun(Obj(message["Payload"]), "Tests", "Sources", tests, definitions, filter)),
                                "Host start/request identity differs."); break;
                        case "TestExecution.Abort":
                            aborts++; abortTick = Tick(line);
                            Require(starts == 1 && completions == 0 && aborts == 1 && abortTick >= sendTick && message["Payload"] is null,
                                "Abort is not uniquely after the actual parent budget expiration."); break;
                        case "TestSession.Terminate":
                            terminates++; terminateLine = lineNumber; terminateTick = Tick(line);
                            Require(completions == 1 && lateBatches == 1 && terminates == 1 && message["Payload"] is null,
                                "Terminate must follow the one late post-abort batch."); break;
                        default: throw Invalid("Unknown or nonreviewed received native message in aborted-tail case.");
                    }
                }
                int send = line.IndexOf(SendMarker, StringComparison.Ordinal);
                if (send < 0) { continue; }
                JsonObject sent = Parse(line[(send + SendMarker.Length)..]);
                string sentType = Text(sent, "MessageType");
                if (sentType == "TestSession.Message")
                {
                    Require(completions == 0 && InformationalMessage(sent, outcomes), "Unknown or noninformational tail diagnostic.");
                    continue;
                }
                Require(starts == 1 && terminates == 0, "Result outside the unique started, not terminated execution.");
                JsonObject payload = Obj(sent["Payload"]);
                if (sentType == "TestExecution.Completed")
                {
                    completions++; completeLine = lineNumber; completeTick = Tick(line);
                    JsonObject completed = Obj(payload["TestRunCompleteArgs"]), last = Obj(payload["LastRunTests"]);
                    Require(completions == 1 && aborts == 1 && Boolean(completed, "IsAborted") && !Boolean(completed, "IsCanceled")
                        && completed.ContainsKey("Error") && completed["Error"] is null
                        && Arr(last["NewTestResults"]).Count == 0 && Arr(last["ActiveTests"]).Count == 0,
                        "Only the original aborted, error-free completion with no final batch is reviewed.");
                    CheckStats(Obj(completed["TestRunStatistics"]), all, outcomes);
                    CheckStats(Obj(last["TestRunStatistics"]), all, outcomes);
                    foreach ((string id, NativeResult result) in all) { before.Add(id, result); }
                    continue;
                }
                Require(sentType == "TestExecution.StatsChange", "Unknown sent native message in aborted-tail case.");
                JsonArray values = Arr(payload["NewTestResults"]);
                bool isLate = completions == 1;
                if (isLate)
                {
                    lateBatches++; lateLine = lineNumber; lateTick = Tick(line);
                    Require(lateBatches == 1 && lineNumber == rejectedLine && values.Count == 1,
                        "Exactly one reviewed late result must cause the original rejection.");
                }
                else { Require(aborts == 0, "An unreviewed pre-completion result arrived during abort."); }
                foreach (JsonNode? node in values)
                {
                    JsonObject result = Obj(node); string id = ValidateTest(Obj(result["TestCase"]), tests);
                    Require(request.Run.Selected.Contains(id), "Late or early result GUID has no selected owner.");
                    var parsed = new NativeResult(Outcome(result["Outcome"], outcomes), OptionalText(result, "ErrorMessage"), OptionalText(result, "ErrorStackTrace"));
                    Require(all.TryAdd(id, parsed), "Duplicate GUID before/after completed.");
                    if (isLate)
                    {
                        Require(parsed.Outcome == outcomes.Failed && !definitions[review.Id].KilledBy.Contains(id),
                            "This late failed result must remain distinct from the original killedBy set.");
                        late.Add(id, parsed);
                    }
                }
                CheckStats(Obj(payload["TestRunStatistics"]), all, outcomes);
                if (isLate)
                {
                    foreach (JsonNode? node in Arr(payload["ActiveTests"]))
                    {
                        string id = ValidateTest(Obj(node), tests);
                        Require(request.Run.Selected.Contains(id) && !all.ContainsKey(id) && activeAfter.Add(id), "Late active GUID is unknown, duplicated or already completed.");
                    }
                    Require(activeAfter.Count == 1, "The exact reviewed tail has one still-active declared case.");
                }
            }
            Require(protocols == 1 && initializations == 1 && runtimes == 1 && parents.SetEquals([request.ControllerPid])
                && starts == 1 && aborts == 1 && completions == 1 && lateBatches == 1 && terminates == 1 && exiting == 1
                && completeLine < lateLine && lateLine < terminateLine && terminateLine < exitingLine
                && abortTick <= completeTick && completeTick < lateTick && lateTick < terminateTick && terminateTick <= exitingTick && exitingTick < exitTick,
                "Actual post-abort Complete, late batch, Terminate and parent OSexit must be unique and ordered.");
            Require(before.Count == review.PreResultCount && before.Values.Count(value => value.Outcome == outcomes.Failed) == review.PreFailedCount
                && before.Where(pair => pair.Value.Outcome == outcomes.Failed).Select(pair => pair.Key).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(definitions[review.Id].KilledBy)
                && before.Count < request.Run.Selected.Count && firstResults.Count == before.Count
                && firstResults.All(pair => before.TryGetValue(pair.Key, out NativeResult? value) && value == pair.Value),
                "Every original pre-completion failed GUID/result and native killedBy association must remain exactly retained.");
            receipt = new AbortedTailReceipt(terminateLine, exitingLine, new JsonObject
            {
                ["scope"] = "Reviewed attribution of an incomplete aborted native session; all results remain unqualified.",
                ["originalReportSha256"] = review.ReportSha256,
                ["definitionIdentity"] = review.DefinitionIdentity,
                ["selectorId"] = review.Id,
                ["hostPath"] = path,
                ["hostSha256"] = review.HostSha256,
                ["controllerPath"] = request.Path,
                ["controllerSha256"] = review.ControllerSha256,
                ["requestedGuidCount"] = request.Run.Selected.Count,
                ["sessionTimeoutMs"] = expectedBudget,
                ["preCompletedResults"] = ResultNodes(before),
                ["lateResults"] = ResultNodes(late),
                ["lateActiveGuids"] = new JsonArray(activeAfter.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray()),
                ["rawKilledByGuids"] = new JsonArray(definitions[review.Id].KilledBy.Order(StringComparer.Ordinal).Select(id => (JsonNode?)JsonValue.Create(id)).ToArray()),
                ["completedLine"] = completeLine,
                ["lateResultLine"] = lateLine,
                ["terminateLine"] = terminateLine,
                ["exitingLine"] = exitingLine,
                ["parentOsExitCode"] = actualExit.Code,
                ["normalHostIncrement"] = 0,
                ["killCredit"] = 0,
                ["originalRawRecordsChanged"] = false
            });
            return true;
        }
        catch (Exception error) when (EvidenceError(error)) { return false; }
    }

    private static JsonArray ResultNodes(Dictionary<string, NativeResult> values) => new(values.OrderBy(pair => pair.Key, StringComparer.Ordinal)
        .Select(pair => (JsonNode)new JsonObject
        {
            ["guid"] = pair.Key,
            ["outcome"] = pair.Value.Outcome,
            ["message"] = pair.Value.Message,
            ["stack"] = pair.Value.Stack
        }).ToArray());

    private static void CheckStats(JsonObject statistics, Dictionary<string, NativeResult> values, OutcomeMetadata outcomes)
    {
        JsonObject counts = Obj(statistics["Stats"]);
        Require(Integer(statistics, "ExecutedTests") == values.Count
            && Count(counts, "Passed") == values.Values.Count(value => value.Outcome == outcomes.Passed)
            && Count(counts, "Failed") == values.Values.Count(value => value.Outcome == outcomes.Failed)
            && counts.All(pair => pair.Key is "Passed" or "Failed" || Integer(counts, pair.Key) == 0),
            "Each pre/post-completion statistics batch must equal the unique retained actual results.");
    }

    private static int HostPid(string path) => LinePid(File.ReadLines(path).First());
    private static bool ValidProtocolVersion(JsonObject message) => message["Payload"] is JsonValue value && value.TryGetValue<int>(out int protocol) && protocol == 7;
    private static string HashNative(string path)
    { using FileStream input = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(input)); }
    private static long Tick(string line)
    {
        Match match = NativeClock.Match(line);
        Require(match.Success && long.TryParse(match.Groups["counter"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out _), "Actual native monotonic trace counter is missing.");
        return long.Parse(match.Groups["counter"].Value, NumberStyles.None, CultureInfo.InvariantCulture);
    }
}
