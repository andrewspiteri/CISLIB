using System.Text.Json.Nodes;
using Context = CIS.Verification.HealthcheckMutationSemanticProofs.Context;
using Review = CIS.Verification.HealthcheckMutationAdditionalUnitSemanticProofs.ReviewedCase;

namespace CIS.Verification;

// Continuation of the finite, exact Unit18 catalog. The established byte/source/
// receiving-route matcher is shared; native lifecycle admission remains outside.
internal static class HealthcheckMutationUnitContinuation2Proofs
{
    internal static bool TryMatch(Context? context, string mutantId, string guid, string method, string codeFile,
        string? message, string? stack, out JsonObject? witness)
    {
        witness = null;
        if (context is null) { return false; }
        foreach (Review review in HealthcheckMutationUnitContinuation2Catalog.Cases)
        {
            if (MatchReviewedCase(review, context, mutantId, guid, method, codeFile, message, stack, out witness)) { return true; }
        }
        return false;
    }

    internal static bool MatchReviewedCase(Review review, Context context, string mutantId, string guid,
        string method, string codeFile, string? message, string? stack, out JsonObject? witness)
    {
        if (!HealthcheckMutationAdditionalUnitSemanticProofs.MatchReviewedCase(review, context, mutantId, guid,
            method, codeFile, message, stack, out witness)) { return false; }
        witness!["kind"] = "ReviewedUnitContinuation2SemanticException";
        return true;
    }
}
