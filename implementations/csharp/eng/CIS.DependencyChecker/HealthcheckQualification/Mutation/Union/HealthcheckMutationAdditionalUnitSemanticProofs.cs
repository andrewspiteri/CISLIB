using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Binding = CIS.Verification.HealthcheckMutationSemanticProofs.SourceBinding;
using Context = CIS.Verification.HealthcheckMutationSemanticProofs.Context;

namespace CIS.Verification;

// This is a finite catalog of reviewed observations, never an exception-type
// allowance. NativeAudit must establish the normal native host and failed GUID
// before calling this matcher. No lifecycle issue is resolved here.
internal static class HealthcheckMutationAdditionalUnitSemanticProofs
{
    internal sealed record CallFrame(string Method, string? SourcePath);

    internal sealed record ReviewedCase(Binding Binding, string ExceptionType, string NativeMessageSha256,
        string NativeStackSha256, CallFrame[] ThrowRoute, string AdmittedInputReview, string ContractRouteReview,
        string CausalReview);

    internal static bool TryMatch(Context? context, string mutantId, string guid, string method, string codeFile,
        string? message, string? stack, out JsonObject? witness)
    {
        witness = null;
        if (context is null) { return false; }
        foreach (ReviewedCase review in HealthcheckMutationAdditionalUnitSemanticCatalog.Cases)
        {
            if (MatchReviewedCase(review, context, mutantId, guid, method, codeFile, message, stack, out witness)) { return true; }
        }
        return false;
    }

    // Independent receiving-test seam; only the fixed catalog above is used by
    // the maintained entry point. Hashes cover the exact parsed native strings,
    // including line endings, instrumented PDB locators and trailing newlines.
    internal static bool MatchReviewedCase(ReviewedCase review, Context context, string mutantId, string guid,
        string method, string codeFile, string? message, string? stack, out JsonObject? witness)
    {
        witness = null;
        if (string.IsNullOrWhiteSpace(review.AdmittedInputReview) || string.IsNullOrWhiteSpace(review.ContractRouteReview)
            || string.IsNullOrWhiteSpace(review.CausalReview) || string.IsNullOrWhiteSpace(review.ExceptionType)
            || string.IsNullOrEmpty(message) || string.IsNullOrEmpty(stack)
            || !Hash(review.NativeMessageSha256) || !Hash(review.NativeStackSha256)
            || Sha(message) != review.NativeMessageSha256 || Sha(stack) != review.NativeStackSha256
            || !message.StartsWith("Test method " + method + " threw exception:\r\n" + review.ExceptionType + ": ", StringComparison.Ordinal)
            || !HealthcheckMutationSemanticProofs.TryBind(review.Binding, context, mutantId, guid, method, codeFile,
                out string corePath, out string testPath)) { return false; }

        string[] forbidden = ["System.TimeoutException", "OperationCanceledException", "TaskCanceledException", "AggregateException",
            "TestCleanup", "ClassCleanup", "AssemblyCleanup", "TestInitialize", "ClassInitialize", "AssemblyInitialize", ".DisposeAsync(", ".Dispose("];
        if (forbidden.Any(value => message.Contains(value, StringComparison.Ordinal) || stack.Contains(value, StringComparison.Ordinal))) { return false; }
        string[] frames = stack.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(value => value.Trim()).ToArray();
        if (review.ThrowRoute.Length < 2 || frames.Length < review.ThrowRoute.Length
            || !review.ThrowRoute.Any(frame => frame.SourcePath?.StartsWith("implementations/csharp/src/CIS.Core/", StringComparison.Ordinal) == true)
            || review.ThrowRoute[^1].Method != method || review.ThrowRoute[^1].SourcePath != review.Binding.TestSourcePath) { return false; }

        string coreSuffix = "/implementations/csharp/src/CIS.Core/" + review.Binding.Definition.SourcePath;
        string nativeRoot = corePath[..^coreSuffix.Length];
        for (int index = 0; index < review.ThrowRoute.Length; index++)
        {
            CallFrame frame = review.ThrowRoute[index];
            if (frame.SourcePath is null)
            {
                if (frames[index] != frame.Method) { return false; }
            }
            else if (!SafeSource(frame.SourcePath)
                || !HealthcheckMutationSemanticProofs.Frame(frames[index], frame.Method, nativeRoot + "/" + frame.SourcePath)) { return false; }
        }
        if (nativeRoot + "/" + review.Binding.TestSourcePath != testPath) { return false; }

        var definition = review.Binding.Definition;
        witness = new JsonObject
        {
            ["kind"] = "ReviewedAdditionalUnitSemanticException",
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
            ["exceptionType"] = review.ExceptionType,
            ["nativeMessageSha256"] = review.NativeMessageSha256,
            ["nativeStackSha256"] = review.NativeStackSha256,
            ["admittedInputReview"] = review.AdmittedInputReview,
            ["contractRouteReview"] = review.ContractRouteReview,
            ["causalReview"] = review.CausalReview,
            ["pdbLineEqualsOriginalSourceLineClaim"] = false,
            ["timeoutReclassified"] = false
        };
        return true;
    }

    private static bool SafeSource(string path) => path.StartsWith("implementations/csharp/", StringComparison.Ordinal)
        && !path.Contains('\\') && !path.Split('/').Any(part => part is "" or "." or "..");
    private static bool Hash(string hash) => hash.Length == 64 && hash.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f');
    private static string Sha(string value) => Convert.ToHexStringLower(SHA256.HashData(new UTF8Encoding(false, true).GetBytes(value)));
}
