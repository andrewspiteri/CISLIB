using System.Text.Json.Nodes;
using static CIS.Verification.HealthcheckMutationUnionModel;

namespace CIS.Verification;

internal static partial class HealthcheckMutationUnionVerification
{
    private static HealthcheckMutationDispositionValidation.BoundRecord[] ReadDispositionReferences(PinnedInputs files,
        JsonObject manifest, Source[] sources, string lane)
    {
        if (!manifest.TryGetPropertyValue("dispositions", out JsonNode? node)) { return []; }
        Need(node is JsonArray, "Optional dispositions must be a non-null reference array.");
        var records = new List<HealthcheckMutationDispositionValidation.BoundRecord>();
        var identities = new HashSet<string>(StringComparer.Ordinal);
        var references = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (JsonNode? value in node!.AsArray())
        {
            var pin = HealthcheckMutationDispositionValidation.ReadReference(Item(value));
            string path = files.Pin(HealthcheckMutationDispositionValidation.Reference(pin));
            Need(references.Add(Path.GetFullPath(path)), "Duplicate disposition reference.");
            files.GuardRoot(Path.GetDirectoryName(path)!);
            JsonObject document = files.ReadDocument(path);
            var bound = HealthcheckMutationDispositionValidation.Bind(document, lane, sources, HealthcheckMutationDispositionCatalog.Entries);
            Need(identities.Add(bound.Review.Definition.Identity), "Duplicate/conflicting dispositions target one source-bound definition.");
            foreach (var evidence in bound.Closure.Concat(bound.Evidence))
            {
                string body = files.Pin(HealthcheckMutationDispositionValidation.Reference(evidence));
                // Artifact bodies have immutable evidence roots. Source files at
                // the repository root already use the source inventory guards.
                if (evidence.Path.StartsWith(".artifacts/", StringComparison.Ordinal)) { files.GuardRoot(Path.GetDirectoryName(body)!); }
            }
            records.Add(bound);
        }
        return records.ToArray();
    }

    private static Adjustment[] CompleteDispositions(HealthcheckMutationDispositionValidation.BoundRecord[] records,
        Partition[] partitions, JsonArray observations)
    {
        var adjustments = new List<Adjustment>();
        foreach (var record in records)
        {
            Adjustment adjustment = HealthcheckMutationDispositionValidation.Complete(record, partitions);
            adjustments.Add(adjustment);
            observations.Add(new JsonObject
            {
                ["reviewId"] = record.Review.Id,
                ["kind"] = adjustment.Kind,
                ["group"] = record.Review.Group,
                ["definitionIdentity"] = adjustment.DefinitionIdentity,
                ["originalReportSha256"] = adjustment.OriginalReportSha256,
                ["originalStatus"] = adjustment.OriginalStatus,
                ["rationale"] = record.Review.Rationale,
                ["numeratorCredit"] = 0,
                ["denominatorDelta"] = adjustment.Kind == "Equivalent" ? -1 : 0,
                ["globalIntegrityIssuesWaived"] = false
            });
        }
        return adjustments.ToArray();
    }
}
