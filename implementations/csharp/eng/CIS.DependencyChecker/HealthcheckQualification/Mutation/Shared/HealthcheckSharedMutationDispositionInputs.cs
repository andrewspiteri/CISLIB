using System.Text.Json.Nodes;
using static CIS.Verification.HealthcheckMutationUnionModel;

namespace CIS.Verification;

internal static partial class HealthcheckMutationUnionVerification
{
    private static HealthcheckMutationDispositionValidation.BoundRecord[] ReadSharedDispositionReferences(
        PinnedInputs files, JsonObject manifest, Source[] sources, string lane)
    {
        if (!manifest.TryGetPropertyValue("dispositions", out JsonNode? value)) { return []; }
        Need(value is JsonArray references && references.Count <= 128,
            "Shared dispositions require at most 128 explicit source-bound references.");
        var bodies = new Dictionary<string, HealthcheckMutationDispositionValidation.Pin>(StringComparer.Ordinal);
        long totalBytes = 0;
        foreach (JsonNode? node in value!.AsArray())
        {
            var reference = HealthcheckMutationDispositionValidation.ReadReference(Item(node));
            Need(reference.Bytes is > 0 and <= 262144, "A disposition record must contain 1 byte through 256 KiB.");
            string path = files.Pin(HealthcheckMutationDispositionValidation.Reference(reference));
            JsonObject document = files.ReadDocument(path);
            string id = String(document, "reviewId");
            var maintained = HealthcheckMutationDispositionCatalog.Entries.Where(review => review.Id == id).ToArray();
            Need(maintained.Length == 1, "Unknown or ambiguous maintained disposition review: " + id);
            var review = maintained[0];
            Need(review.Closure.Length <= 512 && review.Evidence.Length <= 32,
                "A maintained disposition has an excessive source or evidence closure.");
            foreach (var pin in review.Closure.Concat(review.Evidence).Append(reference))
            {
                Need(pin.Bytes is >= 0 and <= 16777216, "A disposition body exceeds 16 MiB.");
                if (bodies.TryGetValue(pin.Path, out var prior))
                { Need(prior == pin, "Disposition records contain conflicting physical pins."); }
                else
                {
                    bodies.Add(pin.Path, pin);
                    totalBytes += pin.Bytes;
                    Need(bodies.Count <= 8192 && totalBytes <= 67108864,
                        "The shared disposition input closure exceeds 8192 bodies or 64 MiB.");
                }
            }
        }
        // The existing reader validates the exact maintained tuple, current
        // source/contract closure and independently authored evidence, then
        // physically pins every body and registers it for the final recheck.
        return ReadDispositionReferences(files, manifest, sources, lane);
    }
}
