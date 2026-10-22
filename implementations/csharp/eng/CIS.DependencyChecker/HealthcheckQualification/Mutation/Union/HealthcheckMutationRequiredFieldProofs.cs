using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Binding = CIS.Verification.HealthcheckMutationSemanticProofs.SourceBinding;
using Context = CIS.Verification.HealthcheckMutationSemanticProofs.Context;

namespace CIS.Verification;

// A required GetProperty read can observe a missing contracted field before
// MSTest reaches Assert.AreEqual. Only reviewed original definition/test/stack
// tuples can enter here; this is not an exception-type allowance.
internal static class HealthcheckMutationRequiredFieldProofs
{
    internal sealed record ReviewedCase(Binding Binding, string JsonPath, string NativeStackSha256,
        string AdmittedInputReview, string CausalReview);

    internal static bool TryMatch(Context context, string mutantId, string guid, string method, string codeFile,
        string? message, string? stack, out JsonObject? witness)
    {
        witness = null;
        foreach (ReviewedCase review in HealthcheckMutationRequiredFieldCatalog.Cases)
        {
            if (MatchReviewedCase(review, context, mutantId, guid, method, codeFile, message, stack, out witness)) { return true; }
        }
        return false;
    }

    // This receiving seam checks parser data only. The production entry point
    // above has a fixed catalog; NativeAudit still supplies normal-host checks.
    internal static bool MatchReviewedCase(ReviewedCase review, Context context, string mutantId, string guid,
        string method, string codeFile, string? message, string? stack, out JsonObject? witness)
    {
        witness = null;
        if (string.IsNullOrWhiteSpace(review.JsonPath) || string.IsNullOrWhiteSpace(review.AdmittedInputReview)
            || string.IsNullOrWhiteSpace(review.CausalReview) || string.IsNullOrEmpty(stack)
            || review.NativeStackSha256.Length != 64 || !review.NativeStackSha256.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f')
            || message != "Test method " + method + " threw exception:\r\nSystem.Collections.Generic.KeyNotFoundException: The given key was not present in the dictionary."
            || Convert.ToHexStringLower(SHA256.HashData(new UTF8Encoding(false, true).GetBytes(stack))) != review.NativeStackSha256
            || !HealthcheckMutationSemanticProofs.TryBind(review.Binding, context, mutantId, guid, method, codeFile, out _, out string testPath)) { return false; }

        string[] frames = stack.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(value => value.Trim()).ToArray();
        if (frames.Length < 3 || frames[0] != "at System.Text.Json.JsonElement.GetProperty(String propertyName)"
            || frames[^1] != "at Microsoft.VisualStudio.TestPlatform.MSTest.TestAdapter.Execution.TestMethodInfo.ExecuteInternalAsync(Object[] arguments, CancellationTokenSource timeoutTokenSource, TestInvocationState invocationState)"
            || !frames.Any(frame => HealthcheckMutationSemanticProofs.Frame(frame, method, testPath))) { return false; }
        string[] forbidden = ["System.TimeoutException", "OperationCanceledException", "TaskCanceledException", "AggregateException",
            "TestCleanup", "ClassCleanup", "AssemblyCleanup", "TestInitialize", "ClassInitialize", "AssemblyInitialize", ".DisposeAsync(", ".Dispose("];
        if (forbidden.Any(value => stack.Contains(value, StringComparison.Ordinal))) { return false; }

        var definition = review.Binding.Definition;
        witness = new JsonObject
        {
            ["kind"] = "ReviewedMissingRequiredJsonField",
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
            ["requiredJsonPath"] = review.JsonPath,
            ["nativeStackSha256"] = review.NativeStackSha256,
            ["admittedInputReview"] = review.AdmittedInputReview,
            ["causalReview"] = review.CausalReview,
            ["pdbLineEqualsOriginalSourceLineClaim"] = false,
            ["timeoutReclassified"] = false
        };
        return true;
    }
}
