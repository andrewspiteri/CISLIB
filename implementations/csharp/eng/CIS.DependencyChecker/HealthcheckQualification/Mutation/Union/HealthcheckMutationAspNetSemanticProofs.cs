using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using static CIS.Verification.HealthcheckMutationUnionModel;
using S = CIS.Verification.HealthcheckMutationSemanticProofs;

namespace CIS.Verification;

// Ordinary, already-rawKilled receiving failures from one closed native run.
// NativeAudit remains responsible for selector, failed GUID, normal complete
// host and actual OS exit. This helper cannot waive lifecycle or child issues.
internal static class HealthcheckMutationAspNetSemanticProofs
{
    internal sealed record ReviewedCase(Definition Tuple, string KilledBySetSha256, string Guid, string Method,
        string Kind, string MessageSha256, string StackSha256, string[] CoreMethods,
        string AdmittedInputReview, string CausalReview, string TestSourceSha256 = TestSha256,
        string FormatterSourceSha256 = FormatterSha256, string ProtocolSourceSha256 = ProtocolSha256);

    internal const string ReportSha256 = "5f15348728dd4443acbfc6d5fd5392fb3c7fbbed45ba9d4405075436d21549c4";
    internal const string TestPath = "implementations/csharp/tests/CIS.Core.IntegrationTests/Healthcheck/Reporting/HealthcheckHttpTransportTests.cs";
    internal const string TestSha256 = "f63892cb9261f90bc0c00976327d5467ac5b801777d80e97c68594b072cd10ab";
    internal const string FormatterPath = "Healthcheck/Reporting/HealthReportFormatter.cs";
    internal const string FormatterSha256 = "82e9b67bc17492fba6d445f63404ce82ac2ffe116fb5144c35342295dcd81665";
    internal const string ProtocolPath = "Healthcheck/Reporting/HealthReportProtocol.cs";
    internal const string ProtocolSha256 = "7161117a6783177bdbd94173148902b4288b687ba80e6c78f5a009d861d5bebf";

    internal static bool TryMatch(S.Context? context, string mutantId, string guid, string method, string codeFile,
        string? message, string? stack, out JsonObject? witness)
    {
        witness = null; if (context is null) { return false; }
        foreach (ReviewedCase review in HealthcheckMutationAspNetSemanticCatalog.Cases)
        { if (Match(review, context, mutantId, guid, method, codeFile, message, stack, out witness)) { return true; } }
        return false;
    }

    internal static bool Match(ReviewedCase review, S.Context context, string mutantId, string guid, string method,
        string codeFile, string? message, string? stack, out JsonObject? witness)
    {
        witness = null;
        if (string.IsNullOrWhiteSpace(review.AdmittedInputReview) || string.IsNullOrWhiteSpace(review.CausalReview)
            || string.IsNullOrEmpty(message) || string.IsNullOrEmpty(stack) || mutantId != review.Tuple.Id || guid != review.Guid || method != review.Method
            || context.ReportSha256 != ReportSha256 || !context.Definitions.TryGetValue(mutantId, out Definition? actual)
            || actual.Status != "Killed" || actual.Identity != review.Tuple.Identity || KilledByHash(actual.KilledBy) != review.KilledBySetSha256
            || HashText(message) != review.MessageSha256 || HashText(stack) != review.StackSha256
            || !Collateral(context.ReviewedSources, review.Tuple, review.FormatterSourceSha256, review.ProtocolSourceSha256)) { return false; }
        Definition exact = review.Tuple with { KilledBy = actual.KilledBy.ToArray() };
        if (!S.TryBind(new(ReportSha256, exact, review.Guid, review.Method, TestPath, review.TestSourceSha256), context, mutantId, guid, method, codeFile,
            out string corePath, out string testPath)) { return false; }
        string[] forbidden = ["TimeoutException", "OperationCanceledException", "TaskCanceledException", "AggregateException", "TestCleanup", "ClassCleanup", "AssemblyCleanup",
            "TestInitialize", "ClassInitialize", "AssemblyInitialize", ".DisposeAsync(", ".Dispose("];
        if (forbidden.Any(value => message.Contains(value, StringComparison.Ordinal) || stack.Contains(value, StringComparison.Ordinal))) { return false; }
        string[] frames = stack.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(value => value.Trim()).ToArray();
        string adapter = "at Microsoft.VisualStudio.TestPlatform.MSTest.TestAdapter.Execution.TestMethodInfo.ExecuteInternalAsync(";
        if (frames.Length < 4 || !frames[^1].StartsWith(adapter, StringComparison.Ordinal)) { return false; }
        string? exceptionType, origin;
        if (review.Kind == "BusinessResponse404")
        {
            exceptionType = "System.Net.Http.HttpRequestException"; origin = "System.Net.Http.HttpResponseMessage.EnsureSuccessStatusCode";
            string expected = "Test method " + method + " threw exception:\r\n" + exceptionType + ": Response status code does not indicate success: 404 (Not Found).";
            if (message != expected || review.CoreMethods.Length != 0 || frames.Length != 5 || frames[0] != "at " + origin + "()"
                || !S.Frame(frames[1], "CIS.Tests.IntegrationTests.Healthcheck.Reporting.HealthcheckHttpTransportTests.Host.GetStringAsync", testPath)
                || !S.Frame(frames[2], method, testPath) || !S.Frame(frames[3], method, testPath)) { return false; }
        }
        else if (review.Kind == "AdapterInvalidErrorCode")
        {
            exceptionType = "System.ArgumentOutOfRangeException"; origin = "CIS.Healthcheck.HealthReportFormatter.FormatError";
            string expected = "Test method " + method + " threw exception:\r\n" + exceptionType + ": Specified argument was out of the range of valid values. (Parameter 'code')";
            if (message != expected || review.CoreMethods.Length is < 2 or > 3 || review.CoreMethods[0] != origin || frames.Length != review.CoreMethods.Length + 2) { return false; }
            string root = corePath[..^review.Tuple.SourcePath.Length];
            for (int index = 0; index < review.CoreMethods.Length; index++)
            {
                string path = index == 0 ? root + FormatterPath : corePath;
                if (!S.Frame(frames[index], review.CoreMethods[index], path)) { return false; }
            }
            if (!S.Frame(frames[review.CoreMethods.Length], method, testPath)) { return false; }
        }
        else { return false; }
        witness = new JsonObject
        {
            ["kind"] = "ReviewedUnexpectedHttpContractFailure",
            ["failureCategory"] = review.Kind,
            ["originalReportSha256"] = ReportSha256,
            ["definitionIdentity"] = actual.Identity,
            ["sourcePath"] = actual.SourcePath,
            ["sourceSha256"] = actual.SourceSha256,
            ["mutantId"] = mutantId,
            ["rawStatus"] = "Killed",
            ["guid"] = guid,
            ["method"] = method,
            ["testSourcePath"] = TestPath,
            ["testSourceTextSha256"] = review.TestSourceSha256,
            ["exceptionType"] = exceptionType,
            ["actualOriginMethod"] = origin,
            ["coreOriginException"] = review.Kind == "AdapterInvalidErrorCode",
            ["observedHttpStatus"] = review.Kind == "BusinessResponse404" ? 404 : null,
            ["messageSha256"] = review.MessageSha256,
            ["stackSha256"] = review.StackSha256,
            ["admittedInputReview"] = review.AdmittedInputReview,
            ["causalReview"] = review.CausalReview,
            ["collateralFormatterSha256"] = review.FormatterSourceSha256,
            ["collateralProtocolSha256"] = review.ProtocolSourceSha256,
            ["pdbLineEqualsOriginalSourceLineClaim"] = false,
            ["timeoutReclassified"] = false,
            ["childPropagationQualifiedByThisWitness"] = false,
            ["originalFullChildAuditStillRequired"] = true
        };
        return true;
    }

    // The CLI supplies its complete actual source-checked 46-file closure.
    // Context clones the immutable strings/records and the source array before
    // a matcher can inspect it; absent collateral cannot enter this catalog.
    internal static bool Collateral(IReadOnlyList<Source> sources, Definition tuple, string formatterHash, string protocolHash)
    {
        foreach ((string path, string hash, string group) in new[] { (tuple.SourcePath, tuple.SourceSha256, "aspnet"), (FormatterPath, formatterHash, "unit-runtime"), (ProtocolPath, protocolHash, "unit-runtime") })
        {
            Source[] matches = sources.Where(value => value.Path == path).ToArray();
            if (matches.Length != 1 || matches[0].Group != group || matches[0].Sha256 != hash || HashText(matches[0].Text) != hash) { return false; }
        }
        return true;
    }

    internal static string KilledByHash(IEnumerable<string> guids) => HashText(string.Join('\n', guids.Order(StringComparer.Ordinal)) + "\n");
    internal static string HashText(string text)
    {
        try { return Convert.ToHexStringLower(SHA256.HashData(new UTF8Encoding(false, true).GetBytes(text))); }
        catch (EncoderFallbackException) { return ""; }
    }
}
