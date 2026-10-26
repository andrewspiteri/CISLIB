using System.Text.Json.Nodes;
using Context = CIS.Verification.HealthcheckMutationSemanticProofs.Context;
using Review = CIS.Verification.HealthcheckMutationAdditionalUnitSemanticProofs.ReviewedCase;

namespace CIS.Verification;

// Four exact supplied-output observations, not an IOException allowance.
// NativeAudit separately establishes each complete normal host and failed GUID.
// Campaign controller exits1 and all child/lifecycle failures remain unchanged.
internal static class HealthcheckMutationPipeOutputProofs
{
    internal static bool TryMatch(Context context, string mutantId, string guid, string method, string codeFile,
        string? message, string? stack, out JsonObject? witness)
    {
        witness = null;
        foreach (Review review in HealthcheckMutationPipeOutputCatalog.Cases)
        {
            if (review.Binding.Definition.Id == mutantId && review.Binding.Guid == guid
                && MatchReviewedCase(review, context, mutantId, guid, method, codeFile, message, stack, out witness))
            { return true; }
        }
        return false;
    }

    // Typed receiving seam; only the immutable catalog above is admitted by
    // TryMatch. Reuse the existing source/definition/hash/exception proof first.
    internal static bool MatchReviewedCase(Review review, Context context, string mutantId, string guid,
        string method, string codeFile, string? message, string? stack, out JsonObject? witness)
    {
        witness = null;
        const string coreMethod = "CIS.Healthcheck.Pipes.HealthProbeClient.RunAsync";
        const string coreSource = "implementations/csharp/src/CIS.Core/Healthcheck/Pipes/HealthProbeClient.cs";
        if (review.ExceptionType != "System.IO.IOException" || review.ThrowRoute.Length != 2
            || review.ThrowRoute[0].Method != coreMethod || review.ThrowRoute[0].SourcePath != coreSource
            || review.ThrowRoute[1].Method != method || review.ThrowRoute[1].SourcePath != review.Binding.TestSourcePath
            || !HealthcheckMutationAdditionalUnitSemanticProofs.MatchReviewedCase(review, context, mutantId, guid,
                method, codeFile, message, stack, out JsonObject? matched)
            || !HealthcheckMutationSemanticProofs.TryBind(review.Binding, context, mutantId, guid, method, codeFile,
                out string corePath, out string testPath)) { return false; }

        // These are exact historical observation bytes, not a public prose promise.
        if (message != "Test method " + method + " threw exception:\r\nSystem.IO.IOException: private output path") { return false; }
        string[] expectedKilledBy = review.Binding.Definition.KilledBy;
        if (!context.Definitions[mutantId].KilledBy.SequenceEqual(expectedKilledBy, StringComparer.Ordinal)) { return false; }
        JsonObject file = context.Report["files"]!.AsObject()
            .Single(pair => pair.Key.Replace('\\', '/') == corePath).Value!.AsObject();
        JsonObject raw = file["mutants"]!.AsArray().OfType<JsonObject>()
            .Single(value => value["id"] is JsonValue id && id.TryGetValue(out string? text) && text == mutantId);
        if (!raw["killedBy"]!.AsArray().Select(value => value!.GetValue<string>())
            .SequenceEqual(expectedKilledBy, StringComparer.Ordinal)) { return false; }

        string[] frames = stack!.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(value => value.Trim()).ToArray();
        string receiving = "at " + method + "() in " + testPath + ":line ";
        if (frames.Length != 5
            || frames[0] != "at " + coreMethod + "(String[] arguments, Stream standardOutput, TextWriter standardError, CancellationToken cancellationToken) in " + corePath + ":line 89"
            || frames[1] != receiving + "77" || frames[2] != receiving + "81" || frames[3] != receiving + "81"
            || frames[4] != "at Microsoft.VisualStudio.TestPlatform.MSTest.TestAdapter.Execution.TestMethodInfo.ExecuteInternalAsync(Object[] arguments, CancellationTokenSource timeoutTokenSource, TestInvocationState invocationState)")
        { return false; }

        matched!["kind"] = "ReviewedPipeOutputFailure";
        matched["coreOriginMethod"] = coreMethod;
        matched["receivingPdbLine"] = 77;
        matched["repeatedUnwindFramesAloneProveCleanup"] = false;
        matched["replacementTextReparsedAsSource"] = false;
        matched["childFailuresDischarged"] = false;
        matched["wholeCampaignApproval"] = false;
        witness = matched;
        return true;
    }
}
