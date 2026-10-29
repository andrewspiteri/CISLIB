using System.Text.Json.Nodes;
using static CIS.Verification.HealthcheckMutationUnionModel;
using Shared = CIS.Verification.HealthcheckSharedMutationModel;
using Validation = CIS.Verification.HealthcheckMutationDispositionValidation;

namespace CIS.Verification;

// Only accepts records produced by the maintained strict physical review reader.
// It does not turn a narrative proposal or diagnostic source variant into credit.
internal static class HealthcheckSharedMutationDispositions
{
    internal static Shared.Review[] Bind(Validation.BoundRecord[] records, Shared.Batch[] batches, JsonArray observations)
    {
        Need(records.Length <= 128, "Too many shared source-bound dispositions.");
        var identities = new HashSet<(string Definition, string Report)>();
        var result = new List<Shared.Review>();
        foreach (var record in records)
        {
            Validation.Review review = record.Review;
            Definition expected = review.Definition;
            Need(review.Kind is "Equivalent" or "ReviewedNoncritical"
                && expected.Status is "Survived" or "NoCoverage" && expected.KilledBy.Length == 0,
                "Shared source reviews require an eligible, individually maintained miss disposition.");
            Need(identities.Add((expected.Identity, review.ReportSha256)),
                "Duplicate or conflicting shared source-bound dispositions.");
            Shared.Batch[] matching = batches.Where(batch => batch.ReportSha256 == review.ReportSha256
                && batch.Source.Path == expected.SourcePath && batch.Source.Sha256 == expected.SourceSha256
                && batch.Source.Group == review.Group).ToArray();
            Need(matching.Length == 1, "A disposition has no unique verified original per-file report.");
            Definition[] definitions = matching[0].Definitions.Where(definition => definition.Id == expected.Id).ToArray();
            Need(definitions.Length == 1 && SameDefinition(definitions[0], expected),
                "A disposition differs from its original native ID, full definition or status.");
            result.Add(new(expected.Identity, review.ReportSha256, review.Kind));
            observations.Add(new JsonObject
            {
                ["reviewId"] = review.Id,
                ["kind"] = review.Kind,
                ["group"] = review.Group,
                ["sourcePath"] = expected.SourcePath,
                ["originalReportSha256"] = review.ReportSha256,
                ["nativeId"] = expected.Id,
                ["definitionIdentity"] = expected.Identity,
                ["originalStatus"] = expected.Status,
                ["rationale"] = review.Rationale,
                ["numeratorCredit"] = 0,
                ["denominatorDelta"] = review.Kind == "Equivalent" ? -1 : 0,
                ["globalIntegrityIssuesWaived"] = false
            });
        }
        return result.ToArray();
    }

    private static bool SameDefinition(Definition actual, Definition expected)
        => actual.Id == expected.Id && actual.Identity == expected.Identity
        && actual.SourcePath == expected.SourcePath && actual.SourceSha256 == expected.SourceSha256
        && actual.Start == expected.Start && actual.End == expected.End && actual.Mutator == expected.Mutator
        && actual.Original == expected.Original && actual.Replacement == expected.Replacement
        && actual.Status == expected.Status && actual.KilledBy.Length == 0;

    private static void Need(bool condition, string message)
    { if (!condition) { throw new InvalidDataException(message); } }
}
