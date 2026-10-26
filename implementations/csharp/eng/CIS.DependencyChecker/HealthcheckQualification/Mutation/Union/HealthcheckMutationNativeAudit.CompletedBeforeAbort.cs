using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using static CIS.Verification.HealthcheckMutationUnionModel;

namespace CIS.Verification;

// Source-bound qualification of one reviewed completed earlier assertion. This
// closed synchronous receiving body has no owned background work or cleanup.
// The later host abort and all unfinished-work/integrity issues remain failures.
internal static partial class HealthcheckMutationNativeAudit
{
    private const string CompletedMethod = "CIS.Tests.UnitTests.HealthcheckValueContractTests.CollectionExecutionRejectsUnsupportedEnumValues";
    private const string CompletedGuid = "11206384-6ad1-887d-8382-f2ee962d54a5";
    private static readonly HealthcheckMutationSemanticProofs.SourceBinding Completed296 = new(
        "056cc405715ac814bbad9ff45e276e77708c56463beea24ad76e23d19f3bf89d",
        new Definition("296", "Healthcheck/Contracts/Collection/HealthCollectionResult.cs",
            "245a7c84b20541ac1d3dd69d7301aa7dab9103f17c11d27fe23a021fee046209", 2935, 2990,
            "Statement mutation", "throw new ArgumentOutOfRangeException(nameof(outcome));", ";", "Killed", [CompletedGuid]),
        CompletedGuid, CompletedMethod,
        "implementations/csharp/tests/CIS.Core.UnitTests/Healthcheck/Foundation/HealthcheckValueContractTests.cs",
        "e95e0791d50d8435287faf3511f8eedd00326f77e49011db4df49a91ab211498");

    private sealed class CompletedBeforeAbortProgress
    {
        internal int FirstAbortLine;
        internal bool UnknownTypedMessage;
        internal readonly Dictionary<string, int> ResultLines = new(StringComparer.OrdinalIgnoreCase);
        internal readonly List<int> Starts = [], Ends = [], Recorded = [];
        internal void ObserveLifecycle(string line, int at, int pid)
        {
            if (pid <= 0 || LinePid(line) != pid) { return; }
            if (line.EndsWith("TestExecutionRecorder.RecordStart: Starting test: " + CompletedMethod + ".", StringComparison.Ordinal)) { Starts.Add(at); }
            if (line.EndsWith("TestExecutionRecorder.RecordEnd: test: " + CompletedMethod + " execution completed.", StringComparison.Ordinal)) { Ends.Add(at); }
            if (line.EndsWith("TestExecutionRecorder.RecordResult: Received result for test: " + CompletedMethod + ".", StringComparison.Ordinal)) { Recorded.Add(at); }
        }
    }

    private static void ObserveCompletedDirectAssertionBeforeLaterAbort(CompletedBeforeAbortProgress progress,
        HealthcheckMutationSemanticProofs.Context? context, string path, int pid, NativeRequest? request, RunSpec? run,
        HashSet<int> parents, string? runtime, bool malformed, bool diagnosticsNormal, int starts, int completions,
        int aborts, int cancels, int completeLine, int terminateLine, int exitingLine, int terminates, int exiting,
        JsonObject? completion, JsonArray? active, Dictionary<string, NativeResult> results, Dictionary<string, TestIdentity> tests,
        Dictionary<string, NativeDefinition> definitions, OutcomeMetadata outcomes, NativeExit? exit, AuditState state)
    {
        try
        {
            if (context is null || request is null || run is null || malformed || !diagnosticsNormal || progress.UnknownTypedMessage
                || !request.ControllerTrusted || !request.Run.SameAs(run) || parents.Count != 1 || !parents.Contains(request.ControllerPid)
                || runtime is null || !(runtime.EndsWith("/Microsoft.NETCore.App/10.0.12", StringComparison.Ordinal)
                    || runtime.EndsWith("\\Microsoft.NETCore.App\\10.0.12", StringComparison.Ordinal))
                || starts != 1 || completions != 1 || aborts != 1 || cancels != 0 || progress.FirstAbortLine <= 0
                || completion is null || !Boolean(completion, "IsAborted") || Boolean(completion, "IsCanceled")
                || !completion.ContainsKey("Error") || completion["Error"] is not null
                || terminates != 1 || exiting != 1 || terminateLine <= completeLine || exitingLine <= terminateLine
                || completeLine <= progress.FirstAbortLine || exit is null || exit.ControllerPid != request.ControllerPid
                || exit.Path != request.Path || exit.Line <= request.LaunchLine || exit.Code != 0 || exit.Error.Length != 0
                || !results.TryGetValue(CompletedGuid, out NativeResult? result) || result.Outcome != outcomes.Failed
                || !progress.ResultLines.TryGetValue(CompletedGuid, out int resultLine) || resultLine >= progress.FirstAbortLine
                || progress.Starts.Count != 1 || progress.Ends.Count != 1 || progress.Recorded.Count != 1
                || !(progress.Starts[0] < progress.Ends[0] && progress.Ends[0] < progress.Recorded[0] && progress.Recorded[0] < resultLine)
                || !tests.TryGetValue(CompletedGuid, out TestIdentity? receiving) || !DirectAssertion(result, receiving)
                || !definitions.TryGetValue("296", out NativeDefinition? raw) || raw.Status != "Killed" || !raw.KilledBy.Contains(CompletedGuid)) { return; }
            string[] owners = run.Selectors.Where(pair => pair.Value.Contains(CompletedGuid)).Select(pair => pair.Key).ToArray();
            if (!owners.SequenceEqual(new[] { "296" }) || !run.Selected.Contains(CompletedGuid)
                || !HealthcheckMutationSemanticProofs.TryBind(Completed296, context, "296", CompletedGuid, receiving.Method,
                    receiving.SourceFile, out string corePath, out string testPath)
                || TextDigest(result.Message!) != "045e875433dbc74616b09d825564854f47cb8a79246762fe9584dc0e12c37e9e"
                || TextDigest(result.Stack!) != "8954e502eca056acdba08f6ffade3632a79f5c20c0fcad23194cb4a901f1d1a4") { return; }
            JsonObject statistics = Obj(completion["TestRunStatistics"]), counts = Obj(statistics["Stats"]);
            if (Integer(statistics, "ExecutedTests") != results.Count || Count(counts, "Passed") != results.Values.Count(value => value.Outcome == outcomes.Passed)
                || Count(counts, "Failed") != results.Values.Count(value => value.Outcome == outcomes.Failed)
                || counts.Any(pair => pair.Key is not ("Passed" or "Failed") && (!(pair.Value is JsonValue value) || !value.TryGetValue<int>(out int n) || n != 0))
                || active is null) { return; }
            var activeGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonNode? value in active)
            {
                string guid = ValidateTest(Obj(value), tests);
                if (!activeGuids.Add(guid) || !run.Selected.Contains(guid) || results.ContainsKey(guid)) { return; }
            }
            state.CompletedBeforeAbortKilled.Add("296");
            state.CompletedBeforeAbortWitnesses.Add(new JsonObject
            {
                ["kind"] = "CompletedDirectAssertionBeforeLaterAbort",
                ["mutantId"] = "296",
                ["guid"] = CompletedGuid,
                ["method"] = receiving.Method,
                ["reportSha256"] = context.ReportSha256,
                ["definitionIdentity"] = Completed296.Definition.Identity,
                ["corePath"] = corePath,
                ["testPath"] = testPath,
                ["hostPath"] = path,
                ["hostPid"] = pid,
                ["controllerPid"] = request.ControllerPid,
                ["startLine"] = progress.Starts[0],
                ["endLine"] = progress.Ends[0],
                ["recordedResultLine"] = progress.Recorded[0],
                ["returnedResultLine"] = resultLine,
                ["laterAbortLine"] = progress.FirstAbortLine,
                ["abortedCompleteLine"] = completeLine,
                ["hostRemainsAborted"] = true,
                ["normalHostCountDelta"] = 0,
                ["closedBodyReview"] = "Exact pinned CollectionExecutionRejectsUnsupportedEnumValues synchronously validates outcome -1 and4; no async operations, shared state, owned cleanup or deadline assertion.",
                ["qualificationScope"] = "RawKilled296 only; numerator composed once with other qualified categories. No Timeout reclassification.",
                ["remainingHostAndUnfinishedIssuesResolved"] = 0,
                ["gateApproval"] = false
            });
        }
        catch (Exception error) when (EvidenceError(error)) { /* Failed source/body/protocol binding adds no qualified ID and changes no issue. */ }
    }

    private static string TextDigest(string text) => Convert.ToHexStringLower(SHA256.HashData(new UTF8Encoding(false, true).GetBytes(text)));
}
