using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Binding = CIS.Verification.HealthcheckMutationSemanticProofs.SourceBinding;
using Context = CIS.Verification.HealthcheckMutationSemanticProofs.Context;

namespace CIS.Verification;

// Four original Pipes20 guards have individually reviewed receiving failures.
// This supplies no process, child, restoration or gate approval: NativeAudit
// must first establish the complete normal host and its actual failed GUID.
internal static class HealthcheckMutationPipeGuardProofs
{
    internal enum GuardKind { PositiveClock, DefaultStartCancellation, NonStoppingStart }

    internal sealed record ReviewedCase(Binding Binding, GuardKind Guard, string NativeMessageSha256,
        string NativeStackSha256, int ReceivingLine, string ContractReview, string AdmittedInputReview, string CausalReview);

    internal static bool TryMatch(Context context, string mutantId, string guid, string method, string codeFile,
        string? message, string? stack, out JsonObject? witness)
    {
        witness = null;
        foreach (ReviewedCase review in HealthcheckMutationPipeGuardCatalog.Cases)
        {
            if (review.Binding.Definition.Id == mutantId && review.Binding.Guid == guid
                && MatchReviewedCase(review, context, mutantId, guid, method, codeFile, message, stack, out witness))
            { return true; }
        }
        return false;
    }

    // Explicit parser receiving seam. Only the immutable catalog above is used
    // by the maintained entry point; a synthetic typed review is not a catalog.
    internal static bool MatchReviewedCase(ReviewedCase review, Context context, string mutantId, string guid,
        string method, string codeFile, string? message, string? stack, out JsonObject? witness)
    {
        witness = null;
        if (string.IsNullOrWhiteSpace(review.ContractReview) || string.IsNullOrWhiteSpace(review.AdmittedInputReview)
            || string.IsNullOrWhiteSpace(review.CausalReview) || review.ReceivingLine <= 0
            || string.IsNullOrEmpty(message) || string.IsNullOrEmpty(stack)
            || !IsHash(review.NativeMessageSha256) || !IsHash(review.NativeStackSha256)
            || Hash(message) != review.NativeMessageSha256 || Hash(stack) != review.NativeStackSha256
            || !HealthcheckMutationSemanticProofs.TryBind(review.Binding, context, mutantId, guid, method, codeFile,
                out string corePath, out string testPath)
            || !KilledByOrderMatches(review.Binding, context, mutantId, corePath)) { return false; }

        string exception;
        string coreMethod;
        int coreIndex;
        switch (review.Guard)
        {
            case GuardKind.PositiveClock:
                exception = "System.ArgumentException";
                coreMethod = "CIS.Healthcheck.HealthPipeServer..ctor";
                coreIndex = 0;
                break;
            case GuardKind.DefaultStartCancellation:
                exception = "System.ArgumentOutOfRangeException";
                coreMethod = "CIS.Healthcheck.HealthPipeServer.StartAsync";
                coreIndex = 1;
                break;
            case GuardKind.NonStoppingStart:
                exception = "System.InvalidOperationException";
                coreMethod = "CIS.Healthcheck.HealthPipeServer.StartAsync";
                coreIndex = 0;
                break;
            default: return false;
        }

        string prefix = "Test method " + method + " threw exception:\r\n" + exception + ": ";
        if (!message.StartsWith(prefix, StringComparison.Ordinal)
            || message[prefix.Length..].Contains('\r') || message[prefix.Length..].Contains('\n')) { return false; }
        string[] frames = stack.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(value => value.Trim()).ToArray();
        int receivingIndex = coreIndex + 1;
        if (frames.Length < receivingIndex + 2
            || (coreIndex == 1 && frames[0] != "at System.Threading.Tasks.Task.FromCanceled(CancellationToken cancellationToken)")
            || !HealthcheckMutationSemanticProofs.Frame(frames[coreIndex], coreMethod, corePath)
            || !HealthcheckMutationSemanticProofs.Frame(frames[receivingIndex], method, testPath)
            || !frames[receivingIndex].EndsWith(":line " + review.ReceivingLine.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            || frames[^1] != "at Microsoft.VisualStudio.TestPlatform.MSTest.TestAdapter.Execution.TestMethodInfo.ExecuteInternalAsync(Object[] arguments, CancellationTokenSource timeoutTokenSource, TestInvocationState invocationState)")
        { return false; }
        string[] forbidden = ["System.TimeoutException", "OperationCanceledException", "TaskCanceledException", "AggregateException",
            "TestCleanup", "ClassCleanup", "AssemblyCleanup", "TestInitialize", "ClassInitialize", "AssemblyInitialize", ".DisposeAsync(", ".Dispose("];
        if (forbidden.Any(value => message.Contains(value, StringComparison.Ordinal) || stack.Contains(value, StringComparison.Ordinal))) { return false; }

        var definition = review.Binding.Definition;
        witness = new JsonObject
        {
            ["kind"] = "ReviewedPipeGuardFailure",
            ["originalReportSha256"] = context.ReportSha256,
            ["definitionIdentity"] = definition.Identity,
            ["sourcePath"] = definition.SourcePath,
            ["sourceSha256"] = definition.SourceSha256,
            ["mutantId"] = mutantId,
            ["rawStatus"] = "Killed",
            ["guid"] = guid,
            ["method"] = method,
            ["testSourcePath"] = review.Binding.TestSourcePath,
            ["testSourceTextSha256"] = review.Binding.TestSourceSha256,
            ["reviewedGuard"] = review.Guard.ToString(),
            ["exceptionType"] = exception,
            ["coreOriginMethod"] = coreMethod,
            ["nativeMessageSha256"] = review.NativeMessageSha256,
            ["nativeStackSha256"] = review.NativeStackSha256,
            ["receivingPdbLine"] = review.ReceivingLine,
            ["contractReview"] = review.ContractReview,
            ["admittedInputReview"] = review.AdmittedInputReview,
            ["causalReview"] = review.CausalReview,
            ["pdbLineEqualsOriginalSourceLineClaim"] = false,
            ["timeoutReclassified"] = false,
            ["childFailuresDischarged"] = false,
            ["wholeCampaignApproval"] = false
        };
        return true;
    }

    private static bool KilledByOrderMatches(Binding review, Context context, string mutantId, string corePath)
    {
        string[] expected = review.Definition.KilledBy;
        if (!context.Definitions[mutantId].KilledBy.SequenceEqual(expected, StringComparer.Ordinal)) { return false; }
        JsonObject file = context.Report["files"]!.AsObject()
            .Single(pair => pair.Key.Replace('\\', '/') == corePath).Value!.AsObject();
        JsonObject raw = file["mutants"]!.AsArray().OfType<JsonObject>()
            .Single(value => value["id"] is JsonValue id && id.TryGetValue(out string? text) && text == mutantId);
        return raw["killedBy"]!.AsArray().Select(value => value!.GetValue<string>()).SequenceEqual(expected, StringComparer.Ordinal);
    }

    private static bool IsHash(string value) => value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(new UTF8Encoding(false, true).GetBytes(value)));
}
