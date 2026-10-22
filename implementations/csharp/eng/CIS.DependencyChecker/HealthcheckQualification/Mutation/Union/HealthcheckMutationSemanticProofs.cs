using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using static CIS.Verification.HealthcheckMutationUnionModel;

namespace CIS.Verification;

// This matcher supplies no process/outcome approval. NativeAudit invokes it only
// after the complete normal host, exact selector, reported failed GUID and actual
// OS-exit checks. The CLI supplies the SHA of the pinned ORIGINAL report bytes
// and definitions already validated against the actual full source inventory.
internal static class HealthcheckMutationSemanticProofs
{
    internal readonly record struct SourceBinding(string ReportSha256, Definition Definition, string Guid, string Method,
        string TestSourcePath, string TestSourceSha256);

    internal sealed record ReviewedCase(string ReportSha256, Definition Definition, string Guid, string Method,
        string TestSourcePath, string TestSourceSha256, string ExceptionType, string[] CoreCallMethods,
        string AdmittedInputReview, string CausalReview)
    {
        internal SourceBinding Binding => new(ReportSha256, Definition, Guid, Method, TestSourcePath, TestSourceSha256);
    }

    internal sealed class Context
    {
        internal Context(string reportSha256, Definition[] definitions, JsonObject report)
        {
            ReportSha256 = reportSha256;
            Definitions = definitions.ToDictionary(value => value.Id, value => value with { KilledBy = value.KilledBy.ToArray() }, StringComparer.Ordinal);
            Report = report.DeepClone().AsObject();
        }

        internal string ReportSha256 { get; }
        internal Dictionary<string, Definition> Definitions { get; }
        internal JsonObject Report { get; }
    }

    internal static Context? CreateContext(JsonObject report, string? originalReportSha256, Definition[]? sourceCheckedDefinitions)
    {
        if (originalReportSha256 is null && sourceCheckedDefinitions is null) { return null; }
        if (originalReportSha256 is null || sourceCheckedDefinitions is null || sourceCheckedDefinitions.Length == 0
            || originalReportSha256.Length != 64 || !originalReportSha256.All(Uri.IsHexDigit)
            || sourceCheckedDefinitions.Select(value => value.Id).Distinct(StringComparer.Ordinal).Count() != sourceCheckedDefinitions.Length)
        { throw new InvalidDataException("Semantic native review requires the pinned original report SHA and unique source-checked definitions together."); }
        return new Context(originalReportSha256, sourceCheckedDefinitions, report);
    }

    internal static bool TryMatch(Context? context, string mutantId, string guid, string method, string codeFile,
        string? message, string? stack, out JsonObject? witness)
    {
        witness = null;
        if (context is null) { return false; }
        foreach (ReviewedCase review in HealthcheckMutationSemanticCatalog.Cases)
        {
            if (MatchReviewedCase(review, context, mutantId, guid, method, codeFile, message, stack, out witness)) { return true; }
        }
        return HealthcheckMutationRequiredFieldProofs.TryMatch(context, mutantId, guid, method, codeFile, message, stack, out witness)
            || HealthcheckMutationAdditionalUnitSemanticProofs.TryMatch(context, mutantId, guid, method, codeFile, message, stack, out witness)
            || HealthcheckMutationPipeGuardProofs.TryMatch(context, mutantId, guid, method, codeFile, message, stack, out witness);
    }

    // The typed case parameter is an independent receiving-test seam. Shipping
    // NativeAudit can enter only through TryMatch and the fixed C# catalog above.
    internal static bool MatchReviewedCase(ReviewedCase review, Context context, string mutantId, string guid,
        string method, string codeFile, string? message, string? stack, out JsonObject? witness)
    {
        witness = null;
        if (string.IsNullOrWhiteSpace(review.AdmittedInputReview) || string.IsNullOrWhiteSpace(review.CausalReview)
            || string.IsNullOrEmpty(message) || string.IsNullOrEmpty(stack)
            || !TryBind(review.Binding, context, mutantId, guid, method, codeFile, out string corePath, out string testPath)) { return false; }
        Definition actual = context.Definitions[mutantId];

        string prefix = "Test method " + method + " threw exception:\r\n" + review.ExceptionType + ": ";
        if (!message.StartsWith(prefix, StringComparison.Ordinal) || message[prefix.Length..].Contains('\n')
            || message[prefix.Length..].Contains('\r') || review.ExceptionType != "System.ArgumentException"
            || review.CoreCallMethods.Length != 3) { return false; }
        string[] frames = stack.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(value => value.Trim()).ToArray();
        if (frames.Length < 4) { return false; }
        for (int index = 0; index < review.CoreCallMethods.Length; index++)
        {
            if (!Frame(frames[index], review.CoreCallMethods[index], corePath)) { return false; }
        }
        if (!Frame(frames[3], method, testPath)) { return false; }
        string[] forbidden = ["System.TimeoutException", "OperationCanceledException", "TaskCanceledException", "AggregateException",
            "TestCleanup", "ClassCleanup", "AssemblyCleanup", "TestInitialize", "ClassInitialize", "AssemblyInitialize", ".DisposeAsync(", ".Dispose("];
        if (forbidden.Any(value => message.Contains(value, StringComparison.Ordinal) || stack.Contains(value, StringComparison.Ordinal))) { return false; }

        witness = new JsonObject
        {
            ["kind"] = "ReviewedUnexpectedCoreException",
            ["originalReportSha256"] = context.ReportSha256,
            ["definitionIdentity"] = actual.Identity,
            ["sourcePath"] = actual.SourcePath,
            ["sourceSha256"] = actual.SourceSha256,
            ["mutantId"] = mutantId,
            ["rawStatus"] = "Killed",
            ["guid"] = guid,
            ["method"] = method,
            ["testSourcePath"] = review.TestSourcePath,
            ["testSourceTextSha256"] = review.TestSourceSha256,
            ["exceptionType"] = review.ExceptionType,
            ["coreOriginMethod"] = review.CoreCallMethods[0],
            ["coreCallMethods"] = new JsonArray(review.CoreCallMethods.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()),
            ["admittedInputReview"] = review.AdmittedInputReview,
            ["causalReview"] = review.CausalReview,
            ["pdbLineEqualsOriginalSourceLineClaim"] = false,
            ["timeoutReclassified"] = false
        };
        return true;
    }

    // Both reviewed exception and required-field observations must bind the
    // exact original report, source span and actual receiving test first.
    // This does not approve an exception, host lifecycle or outcome by itself.
    internal static bool TryBind(SourceBinding review, Context context, string mutantId, string guid,
        string method, string codeFile, out string corePath, out string testPath)
    {
        corePath = testPath = "";
        Definition expected = review.Definition;
        if (context.ReportSha256 != review.ReportSha256 || expected.Status != "Killed" || mutantId != expected.Id
            || guid != review.Guid || method != review.Method || !context.Definitions.TryGetValue(mutantId, out Definition? actual)
            || actual.Status != "Killed" || actual.Identity != expected.Identity || actual.Id != expected.Id
            || !SameGuids(actual.KilledBy, expected.KilledBy) || !actual.KilledBy.Contains(guid, StringComparer.Ordinal)) { return false; }

        string suffix = "/implementations/csharp/src/CIS.Core/" + expected.SourcePath;
        if (context.Report["files"] is not JsonObject files || context.Report["testFiles"] is not JsonObject testFiles) { return false; }
        KeyValuePair<string, JsonNode?>[] candidates = files.Where(pair => Origin(pair.Key).EndsWith(suffix, StringComparison.Ordinal)).ToArray();
        if (candidates.Length != 1 || candidates[0].Value is not JsonObject file
            || file["source"] is not JsonValue sourceValue || !sourceValue.TryGetValue(out string? source) || source is null
            || TextSha256(source) != expected.SourceSha256 || expected.Start < 0 || expected.End > source.Length || expected.End <= expected.Start
            || source[expected.Start..expected.End] != expected.Original || file["mutants"] is not JsonArray mutants) { return false; }
        corePath = Origin(candidates[0].Key);
        string nativeRoot = corePath[..^suffix.Length];
        testPath = nativeRoot + "/" + review.TestSourcePath;
        if (Origin(codeFile) != testPath) { return false; }
        JsonObject[] rawDefinitions = mutants.OfType<JsonObject>().Where(value => Scalar(value["id"]) == mutantId).ToArray();
        if (rawDefinitions.Length != 1 || !RawDefinitionMatches(rawDefinitions[0], expected, source)) { return false; }
        string boundTestPath = testPath;
        KeyValuePair<string, JsonNode?>[] testCandidates = testFiles.Where(pair => Origin(pair.Key) == boundTestPath).ToArray();
        if (testCandidates.Length != 1 || testCandidates[0].Value is not JsonObject testFile
            || testFile["source"] is not JsonValue testSourceValue || !testSourceValue.TryGetValue(out string? testSource) || testSource is null
            || TextSha256(testSource) != review.TestSourceSha256 || testFile["tests"] is not JsonArray tests) { return false; }
        JsonObject[] declared = tests.OfType<JsonObject>().Where(value => Scalar(value["id"]) == guid).ToArray();
        return declared.Length == 1 && Scalar(declared[0]["name"]) == method;
    }

    private static bool RawDefinitionMatches(JsonObject raw, Definition expected, string source)
    {
        if (Scalar(raw["status"]) != "Killed" || Scalar(raw["mutatorName"]) != expected.Mutator
            || Scalar(raw["replacement"]) != expected.Replacement || raw["killedBy"] is not JsonArray killed
            || !SameGuids(killed.Select(Scalar).ToArray(), expected.KilledBy)
            || raw["location"] is not JsonObject location || location["start"] is not JsonObject start || location["end"] is not JsonObject end)
        { return false; }
        return Offset(source, start) == expected.Start && Offset(source, end) == expected.End;
    }

    private static int Offset(string source, JsonObject position)
    {
        if (position["line"] is not JsonValue lineValue || !lineValue.TryGetValue(out int line)
            || position["column"] is not JsonValue columnValue || !columnValue.TryGetValue(out int column) || line < 1 || column < 1) { return -1; }
        int offset = 0;
        for (int current = 1; current < line; current++)
        {
            int next = source.IndexOf('\n', offset);
            if (next < 0) { return -1; }
            offset = next + 1;
        }
        int end = source.IndexOf('\n', offset);
        if (end < 0) { end = source.Length; }
        return column - 1 <= end - offset ? offset + column - 1 : -1;
    }

    internal static bool Frame(string frame, string method, string path)
    {
        int separator = frame.LastIndexOf(" in ", StringComparison.Ordinal), line = frame.LastIndexOf(":line ", StringComparison.Ordinal);
        return frame.StartsWith("at " + method + "(", StringComparison.Ordinal) && separator > 0 && line > separator
            && Origin(frame[(separator + 4)..line]) == path
            && int.TryParse(frame[(line + 6)..], NumberStyles.None, CultureInfo.InvariantCulture, out int observedLine) && observedLine > 0;
    }

    private static bool SameGuids(string[] left, string[] right) => left.Length == right.Length
        && left.Distinct(StringComparer.Ordinal).Count() == left.Length
        && left.ToHashSet(StringComparer.Ordinal).SetEquals(right);
    private static string TextSha256(string text) => Convert.ToHexStringLower(SHA256.HashData(new UTF8Encoding(false, true).GetBytes(text)));
    private static string Origin(string path) => path.Replace('\\', '/');
    private static string Scalar(JsonNode? node) => node is JsonValue value && value.TryGetValue(out string? text) && text is not null ? text : "";
}
