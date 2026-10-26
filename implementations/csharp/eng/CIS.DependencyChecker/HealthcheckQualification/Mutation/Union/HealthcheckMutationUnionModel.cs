using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CIS.Verification;

// HC12 is one source-complete module gate. Each OS remains a separate lane.
internal static partial class HealthcheckMutationUnionModel
{
    internal static readonly string[] Groups = ["unit-runtime", "pipes", "aspnet"];

    // Old manifests keep their original evidence rule. Prospective manifests must
    // explicitly select the shared CIS native completed-result policy.
    internal enum NativeAuditPolicy { LegacySourceProofs = 1, CisNativeCompletedV1 = 2 }

    internal sealed record Source(string Path, string Sha256, string Group, string Text);

    internal sealed record Definition(string Id, string SourcePath, string SourceSha256,
        int Start, int End, string Mutator, string Original, string Replacement, string Status,
        string[] KilledBy, string? StatusReason = null)
    {
        // Native numeric IDs have meaning only within one original report.
        internal string Identity => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            new object[] { SourcePath, SourceSha256, Start, End, Mutator, Original, Replacement })));
    }

    // Empty MutantIds denotes an unassigned/global integrity failure. Such a
    // failure cannot be repaired by an unrelated per-definition attestation.
    internal sealed record IntegrityIssue(string Code, string Detail, string[] MutantIds);

    internal sealed record Partition(string Group, string ReportSha256, Source[] Sources,
        Definition[] Definitions, string[] QualifiedKilledIds, IntegrityIssue[] IntegrityIssues);

    // This is the internal result of evidence validation, not a JSON approval
    // flag. The file reader must validate the original binding and proof first.
    internal sealed record Adjustment(string DefinitionIdentity, string OriginalReportSha256,
        string OriginalStatus, string Kind);

    internal sealed record NativeAuditResult(string[] AssertionKilledIds, IntegrityIssue[] Issues,
        int HostCount, int NormalHostCount, JsonObject Observations)
    {
        public string[] ReviewedSemanticKilledIds { get; init; } = [];
        public string[] CompletedBeforeAbortKilledIds { get; init; } = [];
        public string[] NativeFailedKilledIds { get; init; } = [];
        public string[] QualifiedKilledIds => AssertionKilledIds.Concat(ReviewedSemanticKilledIds)
            .Concat(CompletedBeforeAbortKilledIds)
            .Concat(NativeFailedKilledIds)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }
}
