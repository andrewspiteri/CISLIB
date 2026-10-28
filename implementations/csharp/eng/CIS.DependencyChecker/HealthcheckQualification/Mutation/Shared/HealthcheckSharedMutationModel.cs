using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using static CIS.Verification.HealthcheckMutationUnionModel;

namespace CIS.Verification;

// Only the physical receiver constructs these records after checking shared CIS
// evidence. They are not JSON completion/approval flags supplied by a campaign.
internal static class HealthcheckSharedMutationModel
{
    internal sealed record Batch(Source Source, string ReportSha256, Definition[] Definitions,
        string[] VerifiedKilledIds, string[] VerifiedTimeoutKilledIds, IntegrityIssue[] Issues);

    internal sealed record Review(string DefinitionIdentity, string OriginalReportSha256, string Kind);

    internal static JsonObject Evaluate(Source[] sources, Batch[] batches, Review[] reviews)
    {
        Need(sources.Length > 0 && sources.Select(source => source.Path).Distinct(StringComparer.Ordinal).Count() == sources.Length,
            "The expected source inventory is empty or duplicated.");
        Need(sources.Select(source => source.Group).ToHashSet(StringComparer.Ordinal).SetEquals(Groups),
            "The expected inventory must contain the three qualification groups.");
        var expected = sources.ToDictionary(source => source.Path, StringComparer.Ordinal);
        var bySource = new Dictionary<string, Batch>(StringComparer.Ordinal);
        foreach (Batch batch in batches)
        {
            Need(expected.TryGetValue(batch.Source.Path, out Source? current) && current == batch.Source,
                "A batch is outside the current source/group inventory.");
            Need(bySource.TryAdd(batch.Source.Path, batch), "Duplicate original evidence for a source is forbidden.");
            Need(Hash(batch.ReportSha256), "An original native report hash is malformed.");
            Need(batch.Definitions.All(definition => definition.SourcePath == batch.Source.Path
                && definition.SourceSha256 == batch.Source.Sha256), "A per-file batch contains foreign definitions.");
            Need(batch.Definitions.Select(definition => definition.Id).Distinct(StringComparer.Ordinal).Count() == batch.Definitions.Length,
                "A native report contains duplicate per-file IDs.");
            Need(batch.VerifiedKilledIds.Distinct(StringComparer.Ordinal).Count() == batch.VerifiedKilledIds.Length
                && batch.VerifiedKilledIds.All(id => batch.Definitions.Any(definition => definition.Id == id && definition.Status == "Killed")),
                "Verified native kills must bind original Killed definitions.");
            Need(batch.VerifiedTimeoutKilledIds.Distinct(StringComparer.Ordinal).Count() == batch.VerifiedTimeoutKilledIds.Length
                && batch.VerifiedTimeoutKilledIds.All(id => batch.Definitions.Any(definition => definition.Id == id && definition.Status == "Timeout")),
                "Verified rerun credit must bind distinct original Timeout definitions.");
        }
        Need(reviews.Select(review => (review.DefinitionIdentity, review.OriginalReportSha256)).Distinct().Count() == reviews.Length,
            "Duplicate survivor dispositions are forbidden.");
        foreach (Review review in reviews)
        {
            Need(review.Kind is "Equivalent" or "ReviewedNoncritical", "Unsupported survivor disposition.");
            Need(batches.Any(batch => batch.ReportSha256 == review.OriginalReportSha256
                && batch.Definitions.Any(definition => definition.Identity == review.DefinitionIdentity
                    && definition.Status is "Survived" or "NoCoverage")), "A disposition has a stale or foreign original binding.");
        }
        string[] missing = expected.Keys.Except(bySource.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (missing.Length > 0)
        {
            return new JsonObject
            {
                ["schema"] = "cis.healthcheck.shared-mutation/v1",
                ["complete"] = false,
                ["qualified"] = false,
                ["verifiedSourceCount"] = bySource.Count,
                ["expectedSourceCount"] = sources.Length,
                ["verifiedSources"] = JsonSerializer.SerializeToNode(bySource.Keys.Order(StringComparer.Ordinal).ToArray()),
                ["missingSources"] = JsonSerializer.SerializeToNode(missing),
                ["aggregate"] = null,
                ["reason"] = "Every current source requires verified original evidence before a qualification score is available.",
            };
        }

        var partitions = new List<Partition>();
        var adjustments = new List<Adjustment>();
        var origins = new JsonArray();
        var receiverHolds = new JsonArray();
        var partitionReceipts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string group in Groups)
        {
            Batch[] selected = batches.Where(batch => batch.Source.Group == group).OrderBy(batch => batch.Source.Path, StringComparer.Ordinal).ToArray();
            string receiptHash = Digest(JsonSerializer.Serialize(selected.Select(batch => new
            {
                batch.Source.Path,
                batch.Source.Sha256,
                batch.ReportSha256,
                definitions = batch.Definitions.Select(definition => definition.Identity).Order(StringComparer.Ordinal),
            })));
            partitionReceipts.Add(group, receiptHash);
            var definitions = new List<Definition>();
            var killed = new List<string>();
            var issues = new List<IntegrityIssue>();
            foreach (Batch batch in selected)
            {
                var ids = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (Definition definition in batch.Definitions.OrderBy(definition => definition.Id, StringComparer.Ordinal))
                {
                    // Numeric IDs are report-local. Remapping is in memory only;
                    // the original report and its complete origin remain intact.
                    string id = definitions.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    ids.Add(definition.Id, id);
                    definitions.Add(definition with { Id = id });
                    if (batch.VerifiedKilledIds.Contains(definition.Id, StringComparer.Ordinal)) { killed.Add(id); }
                    if (batch.VerifiedTimeoutKilledIds.Contains(definition.Id, StringComparer.Ordinal))
                    { adjustments.Add(new(definition.Identity, receiptHash, definition.Status, "AssertionKill")); }
                    foreach (Review review in reviews.Where(review => review.OriginalReportSha256 == batch.ReportSha256
                        && review.DefinitionIdentity == definition.Identity))
                    { adjustments.Add(new(definition.Identity, receiptHash, definition.Status, review.Kind)); }
                    origins.Add(new JsonObject
                    {
                        ["group"] = group,
                        ["sourcePath"] = batch.Source.Path,
                        ["originalReportSha256"] = batch.ReportSha256,
                        ["nativeId"] = definition.Id,
                        ["definitionIdentity"] = definition.Identity,
                        ["compositeId"] = id,
                    });
                }
                foreach (IntegrityIssue issue in batch.Issues)
                {
                    Need(issue.MutantIds.All(ids.ContainsKey), "An integrity hold names an unknown original mutant.");
                    // A completed assertion cannot repair genuine execution,
                    // recovery, current-input or child-origin evidence failures.
                    // Preserve the original selectors for diagnosis, while the
                    // score evaluator receives an unresolvable partition hold.
                    receiverHolds.Add(new JsonObject
                    {
                        ["group"] = group,
                        ["sourcePath"] = batch.Source.Path,
                        ["originalReportSha256"] = batch.ReportSha256,
                        ["code"] = issue.Code,
                        ["detail"] = issue.Detail,
                        ["nativeIds"] = JsonSerializer.SerializeToNode(issue.MutantIds),
                    });
                    issues.Add(issue with { MutantIds = [] });
                }
            }
            partitions.Add(new(group, receiptHash, selected.Select(batch => batch.Source).ToArray(), definitions.ToArray(), killed.ToArray(), issues.ToArray()));
        }
        JsonObject result = HealthcheckMutationUnionModel.Evaluate(sources, partitions.ToArray(), adjustments.ToArray());
        result["schema"] = "cis.healthcheck.shared-mutation/v1";
        result["verifiedSourceCount"] = bySource.Count;
        result["expectedSourceCount"] = sources.Length;
        result["origins"] = origins;
        result["receiverHolds"] = receiverHolds;
        // The old pure arithmetic accepts one digest per logical partition. In
        // this protocol it binds a composite receipt, never an invented native report.
        foreach (JsonNode? node in result["partitions"]!.AsArray())
        {
            JsonObject partition = node!.AsObject();
            partition.Remove("reportSha256");
            partition.Remove("originalReportSha256");
            partition["partitionReceiptSha256"] = partitionReceipts[partition["group"]!.GetValue<string>()];
        }
        return result;
    }

    private static bool Hash(string value) => value.Length == 64 && value.All(character => char.IsAsciiDigit(character) || character is >= 'a' and <= 'f');
    private static string Digest(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static void Need(bool condition, string message) { if (!condition) { throw new InvalidDataException(message); } }
}
