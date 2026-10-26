using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using static CIS.Verification.HealthcheckMutationUnionModel;

namespace CIS.Verification;

internal static partial class HealthcheckMutationNativeAudit
{
    // Stryker.NET 4.16, f9109e24c615a7030a3b33e5532c665c974e4ec5,
    // VsTestRunner.HandleUpdate logs this immediately before CancelTestRun.
    // A canceled completion, disable-bail:false, or a missing result is not this proof.
    private const string NativeBailCue = "Each mutant's fate has been established, we can stop.";
    private static readonly Regex EngineLogName = new(@"^log-\d{8}\.txt$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex ControllerRunnerName = new(@"^Runner (?<runner>\d+)(?:-\d+)?-log\.txt$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex EnginePrefix = new(@"^(?<time>\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{7}[+-]\d{2}:\d{2})\s+\[(?<level>[A-Z]{3})\] ", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex EngineBailMessage = new("^\"Runner (?<runner>[0-9]+)\": " + Regex.Escape(NativeBailCue) + @" \([0-9a-f]{8}\)$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex TraceClock = new(@"^TpTrace \w+: 0 : \d+, \d+, (?<date>\d{4}/\d{2}/\d{2}), (?<time>\d{2}:\d{2}:\d{2}\.\d{3}), \d+, ", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static bool IsNativeControllerHeader(string? line, NativeAuditPolicy policy) =>
        line?.Contains(", vstest.console.dll,", StringComparison.Ordinal) == true
        || policy == NativeAuditPolicy.CisNativeCompletedV1 && line?.Contains(", vstest.console.exe,", StringComparison.Ordinal) == true;

    private static bool IsNativeControllerVersionHeader(string line, NativeAuditPolicy policy) =>
        line.Contains(", vstest.console.dll, Version:", StringComparison.Ordinal)
        || policy == NativeAuditPolicy.CisNativeCompletedV1 && line.Contains(", vstest.console.exe, Version:", StringComparison.Ordinal);

    private static NativeBailEvidence? ReadNativeBailEvidence(JsonObject options, IReadOnlyList<string> paths,
        RetainedDiagnosticInputs? retained, NativeAuditPolicy policy)
    {
        if (policy != NativeAuditPolicy.CisNativeCompletedV1) { return null; }
        var evidence = new NativeBailEvidence();
        if (options["disable-bail"] is not JsonValue bail || !bail.TryGetValue<bool>(out bool disabled) || disabled)
        {
            evidence.Unsupported = "The prospective native run must explicitly enable the pinned engine's bail behavior.";
            return evidence;
        }
        int starts = 0;
        try
        {
            foreach (string path in paths.Where(path => EngineLogName.IsMatch(Path.GetFileName(path))))
            {
                int number = 0;
                foreach (string line in retained?.ReadLines(path) ?? File.ReadLines(path))
                {
                    number++;
                    CheckLine(line);
                    bool cue = line.Contains(NativeBailCue, StringComparison.Ordinal);
                    bool start = line.Contains("Stryker starting, version:", StringComparison.Ordinal);
                    bool hazard = line.Contains("Error while cancelling VsTest session.", StringComparison.Ordinal)
                        || line.Contains("Retrying the test session.", StringComparison.Ordinal)
                        || line.Contains("Rerun of the test session because computer entered power saving mode.", StringComparison.Ordinal)
                        || line.Contains("VsTest failed ", StringComparison.Ordinal);
                    if (!cue && !start && !hazard) { continue; }
                    Match prefix = EnginePrefix.Match(line);
                    Require(prefix.Success && DateTimeOffset.TryParseExact(prefix.Groups["time"].Value, "O", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out _), "Native engine intent has an unsupported timestamp envelope.");
                    if (hazard) { evidence.Unsupported = "Native engine reports cancellation failure, retry or an uncertain VSTest session."; }
                    string message = line[prefix.Length..];
                    if (start)
                    {
                        starts++;
                        Require(prefix.Groups["level"].Value == "DBG" && Regex.IsMatch(message, "^Stryker starting, version: \"4\\.16\\.0\" \\([0-9a-f]{8}\\)$",
                            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)), "Native early-bail intent requires the pinned Stryker4.16 engine log.");
                    }
                    if (!cue) { continue; }
                    Match match = EngineBailMessage.Match(message);
                    Require(prefix.Groups["level"].Value == "DBG" && match.Success, "Native early-bail cue has an unsupported engine/runner envelope.");
                    DateTimeOffset timestamp = DateTimeOffset.ParseExact(prefix.Groups["time"].Value, "O", CultureInfo.InvariantCulture);
                    // PlatformEqtTrace18.7 writes DateTime.Now with millisecond precision.
                    // Preserve the engine's originating local wall clock; converting to the
                    // auditor machine's timezone would corrupt cross-machine evidence.
                    evidence.Cues.Add(new NativeBailIntent(Number(match.Groups["runner"].Value), timestamp.DateTime, path, number));
                }
            }
            if (starts != 1) { evidence.Unsupported = "Exactly one pinned native engine startup is required for intentional-bail provenance."; }
        }
        catch (Exception error) when (EvidenceError(error))
        {
            evidence.Unsupported = Bound(error.Message);
        }
        return evidence;
    }

    private static void ObserveNativeBailRequest(NativeRequest request, string line)
    {
        request.Bail.RequestTime = NativeTraceTime(line);
    }

    private static bool DeferNativeBailControllerStop(AuditState state, NativeRequest[] active, string type, int lineNumber, string line)
    {
        if (state.Policy != NativeAuditPolicy.CisNativeCompletedV1 || type != "TestExecution.Cancel" || active.Length != 1) { return false; }
        NativeBailRequestProgress progress = active[0].Bail;
        progress.CancelCount++;
        progress.CancelLine = lineNumber;
        progress.CancelTime = NativeTraceTime(line);
        return true;
    }

    private static void ObserveNativeBailBatch(NativeBailHostProgress progress, JsonObject batch,
        Dictionary<string, TestIdentity> tests, RunSpec run, int lineNumber)
    {
        if (batch["ActiveTests"] is not JsonArray active) { return; }
        try
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonNode? node in active)
            {
                string guid = ValidateTest(Obj(node), tests);
                Require(run.Selected.Contains(guid) && seen.Add(guid), "Native active-test identities are repeated or outside the host request.");
                progress.ActiveObserved.Add(guid);
            }
        }
        catch (Exception error) when (EvidenceError(error)) { progress.Unsupported = true; }
    }

    private static bool TryNativeEarlyBail(AuditState state, NativeRequest? request, RunSpec? run,
        Dictionary<string, NativeResult> results, Dictionary<string, NativeDefinition> definitions, OutcomeMetadata outcomes,
        NativeBailHostProgress progress, JsonObject? completion, JsonArray? finalActive, bool malformed, out JsonObject? witness)
    {
        witness = null;
        if (state.Policy != NativeAuditPolicy.CisNativeCompletedV1 || request is null || !request.ControllerStopped) { return false; }
        string? problem = NativeEarlyBailProblem(state.NativeBail, request, run, results, definitions, outcomes, progress,
            completion, finalActive, malformed, out NativeBailIntent? intent);
        if (problem is not null)
        {
            state.Issue("NativeEarlyBailUnproven", problem, request.Ids);
            return false;
        }
        witness = new JsonObject
        {
            ["controllerPid"] = request.ControllerPid,
            ["controllerPath"] = request.Path,
            ["requestLine"] = request.RequestLine,
            ["controllerCancelLine"] = request.Bail.CancelLine,
            ["hostCancelLine"] = progress.CancelLine,
            ["enginePath"] = intent!.Path,
            ["engineLine"] = intent.Line,
            ["engineRunner"] = intent.Runner,
            ["engineCue"] = NativeBailCue,
            ["selectedTestCount"] = run!.Selected.Count,
            ["completedResultCount"] = results.Count,
            ["unexecutedSelectedTestCount"] = run.Selected.Count - results.Count,
            ["mutantIds"] = new JsonArray(request.Ids.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray())
        };
        return true;
    }

    private static string? NativeEarlyBailProblem(NativeBailEvidence? evidence, NativeRequest request, RunSpec? run,
        Dictionary<string, NativeResult> results, Dictionary<string, NativeDefinition> definitions, OutcomeMetadata outcomes,
        NativeBailHostProgress progress, JsonObject? completion, JsonArray? finalActive, bool malformed, out NativeBailIntent? intent)
    {
        intent = null;
        if (evidence is null || evidence.Unsupported is not null) { return evidence?.Unsupported ?? "Original pinned native engine early-bail evidence is missing."; }
        if (malformed || !request.ControllerTrusted || run is null || run.Selectors.Count == 0 || !request.Run.SameAs(run))
        { return "Native early-bail request and host selector provenance is incomplete or ambiguous."; }
        if (request.Bail.CancelCount != 1 || progress.CancelCount != 1 || progress.AbortCount != 0 || progress.Unsupported
            || request.Bail.RequestTime is not DateTime begin || request.Bail.CancelTime is not DateTime cancel
            || progress.CancelTime is not DateTime hostCancel || cancel < begin || hostCancel.AddMilliseconds(1) < cancel)
        { return "Native early-bail requires one ordered controller cancel and one subject-host cancel with original timestamps."; }
        if (completion is null || completion["IsAborted"]?.GetValueKind() != System.Text.Json.JsonValueKind.False
            || completion["IsCanceled"]?.GetValueKind() != System.Text.Json.JsonValueKind.True
            || !completion.ContainsKey("Error") || completion["Error"] is not null || finalActive is not { Count: 0 }
            || progress.CompleteLine <= progress.CancelLine || !progress.ActiveObserved.IsSubsetOf(results.Keys))
        { return "Native early-bail has aborted/error completion, an active unfinished test, or lacks cancel-before-complete order."; }
        Match runner = ControllerRunnerName.Match(Path.GetFileName(request.Path));
        if (!runner.Success) { return "Native early-bail controller filename does not identify the engine runner generation."; }
        int runnerId = Number(runner.Groups["runner"].Value);
        // VSTest truncates its wall-clock timestamp to milliseconds. The engine
        // cue must fall inside this one actual request/subject-cancel interval;
        // the pinned source emits it before calling CancelTestRun. Independent
        // logger clock samples cannot prove submillisecond controller receipt
        // order, whereas the subject's actual cancel bounds this exact host.
        // Duplicate
        // matching cues (including overlapping generations) cannot be guessed away.
        NativeBailIntent[] candidates = evidence.Cues.Where(cue => cue.Runner == runnerId && cue.Time >= begin && cue.Time < hostCancel.AddMilliseconds(1)).ToArray();
        if (candidates.Length != 1) { return "No unique original engine resolved-mutants cue joins this controller request/cancel interval."; }
        NativeBailIntent candidate = candidates[0];
        if (evidence.Consumed.Contains((candidate.Path, candidate.Line))) { return "One native early-bail cue cannot authorize multiple host requests or controller generations."; }
        foreach ((string owner, HashSet<string> selected) in run.Selectors)
        {
            if (!definitions.TryGetValue(owner, out NativeDefinition? definition) || definition.Status != "Killed"
                || !selected.Any(guid => definition.KilledBy.Contains(guid) && results.TryGetValue(guid, out NativeResult? result)
                    && result.Outcome == outcomes.Failed && !NativeFrameworkFailure(result)
                    && progress.ResultLines.TryGetValue(guid, out int resultLine) && resultLine < progress.CancelLine
                    && progress.ResultTimes.TryGetValue(resultLine, out DateTime failedAt) && failedAt <= candidate.Time
                    && failedAt < cancel.AddMilliseconds(1) && failedAt < hostCancel.AddMilliseconds(1)))
            { return "Every active native selector must already have its exact reported killedBy completed Failed result before engine intent and host cancellation."; }
        }
        intent = candidate;
        return null;
    }

    private static void RecordNativeBailWitness(AuditState state, JsonObject witness)
    {
        string path = (string)witness["enginePath"]!;
        int line = (int)witness["engineLine"]!;
        state.NativeBail!.Consumed.Add((path, line));
        state.NativeBailWitnessCount++;
        if (state.NativeBailWitnesses.Count < 32) { state.NativeBailWitnesses.Add(witness); }
    }

    private static DateTime? NativeTraceTime(string line)
    {
        Match match = TraceClock.Match(line);
        return match.Success && DateTime.TryParseExact(match.Groups["date"].Value + " " + match.Groups["time"].Value,
            "yyyy/MM/dd HH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime value) ? value : null;
    }

    private sealed record NativeBailIntent(int Runner, DateTime Time, string Path, int Line);
    private sealed class NativeBailEvidence
    {
        internal readonly List<NativeBailIntent> Cues = [];
        internal readonly HashSet<(string Path, int Line)> Consumed = [];
        internal string? Unsupported;
    }
    private sealed class NativeBailRequestProgress
    {
        internal DateTime? RequestTime, CancelTime;
        internal int CancelCount, CancelLine;
    }
    private sealed class NativeBailHostProgress
    {
        internal DateTime? CancelTime;
        internal int CancelCount, CancelLine, AbortCount, CompleteLine;
        internal bool Unsupported;
        internal readonly Dictionary<string, int> ResultLines = new(StringComparer.OrdinalIgnoreCase);
        internal readonly Dictionary<int, DateTime> ResultTimes = [];
        internal readonly HashSet<string> ActiveObserved = new(StringComparer.OrdinalIgnoreCase);

        internal void Observe(string line, int number)
        {
            // Unsupported clocks/envelopes prevent the prospective exemption;
            // this passive observer never grants credit or throws.
            try
            {
                int receive = line.IndexOf(ReceiveMarker, StringComparison.Ordinal);
                if (receive >= 0)
                {
                    string type = Text(Parse(line[(receive + ReceiveMarker.Length)..]), "MessageType");
                    if (type == "TestExecution.Cancel") { CancelCount++; CancelLine = number; CancelTime = NativeTraceTime(line); }
                    else if (type == "TestExecution.Abort") { AbortCount++; }
                }
                int send = line.IndexOf(SendMarker, StringComparison.Ordinal);
                if (send < 0) { return; }
                string sent = Text(Parse(line[(send + SendMarker.Length)..]), "MessageType");
                if (sent is not ("TestExecution.StatsChange" or "TestExecution.Completed")) { return; }
                if (sent == "TestExecution.Completed") { CompleteLine = number; }
                if (NativeTraceTime(line) is DateTime timestamp) { ResultTimes[number] = timestamp; }
            }
            catch (Exception error) when (EvidenceError(error)) { Unsupported = true; }
        }
    }
}
