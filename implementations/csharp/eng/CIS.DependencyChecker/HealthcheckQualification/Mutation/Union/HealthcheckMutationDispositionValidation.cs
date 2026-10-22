using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using static CIS.Verification.HealthcheckMutationUnionModel;

namespace CIS.Verification;

// Structural/source review validation only. The maintained file reader must
// physically pin every returned closure/evidence body before emitting Adjustment.
internal static class HealthcheckMutationDispositionValidation
{
    internal const string Schema = "cis.healthcheck.source-bound-disposition/v1";
    private const string CorePrefix = "implementations/csharp/src/CIS.Core/";
    internal sealed record Pin(string Path, string Sha256, long Bytes);
    internal sealed record Review(string Id, string Kind, string Lane, string Group, string ReportSha256,
        Definition Definition, Pin[] Closure, Pin[] Evidence, string Rationale);
    internal sealed record BoundRecord(Review Review, Pin[] Closure, Pin[] Evidence);

    internal static Pin ReadReference(JsonObject value)
    {
        Fields(value, "path", "sha256", "bytes");
        string path = Text(value, "path"), hash = Text(value, "sha256");
        Need(Safe(path) && IsHash(hash) && value["bytes"] is JsonValue number && number.TryGetValue(out long _), "Malformed disposition evidence pin.");
        long bytes = value["bytes"]!.GetValue<long>();
        Need(bytes >= 0, "Negative disposition evidence size.");
        return new Pin(path, hash, bytes);
    }

    internal static BoundRecord Bind(JsonObject document, string lane, Source[] sources, IReadOnlyList<Review> catalog)
    {
        Fields(document, "schema", "reviewId", "kind", "osLane", "target", "closure", "evidence");
        Need(Text(document, "schema") == Schema, "Unsupported disposition schema.");
        string kind = Text(document, "kind"), id = Text(document, "reviewId");
        Need(kind is "Equivalent" or "ReviewedNoncritical", "Unsupported disposition kind; raw rerun/termination proof validation is not implemented by this reader.");
        Need(lane is "windows" or "ubuntu" && Text(document, "osLane") == lane, "Disposition lane differs from this complete union.");
        Review[] entries = catalog.Where(value => value.Id == id).ToArray();
        Need(entries.Length == 1, "Unknown or ambiguous maintained disposition review: " + id);
        Review review = entries[0];
        Need(review.Kind == kind && review.Lane == lane && Groups.Contains(review.Group, StringComparer.Ordinal)
            && IsHash(review.ReportSha256) && !string.IsNullOrWhiteSpace(review.Rationale), "Maintained review kind/lane/group/binding is invalid.");
        Definition definition = review.Definition;
        Need(definition.Status is "Survived" or "NoCoverage" && definition.KilledBy.Length == 0,
            "Only individually reviewed original Survived/NoCoverage definitions are supported.");

        JsonObject target = Object(document, "target");
        Fields(target, "group", "reportSha256", "definition");
        Need(Text(target, "group") == review.Group && Text(target, "reportSha256") == review.ReportSha256, "Disposition report/group differs from maintained review.");
        JsonObject native = Object(target, "definition");
        Fields(native, "id", "identity", "sourcePath", "sourceSha256", "start", "end", "mutator", "original", "replacement", "status");
        Need(Text(native, "id") == definition.Id && Text(native, "identity") == definition.Identity
            && Text(native, "sourcePath") == definition.SourcePath && Text(native, "sourceSha256") == definition.SourceSha256
            && Integer(native, "start") == definition.Start && Integer(native, "end") == definition.End
            && Text(native, "mutator") == definition.Mutator && Text(native, "original") == definition.Original
            && Text(native, "replacement") == definition.Replacement && Text(native, "status") == definition.Status,
            "Disposition exact native definition differs from maintained review.");
        Source[] candidates = sources.Where(value => value.Path == definition.SourcePath).ToArray();
        Need(candidates.Length == 1, "Disposition source is missing or ambiguous.");
        Source source = candidates[0];
        Need(source.Group == review.Group && source.Sha256 == definition.SourceSha256 && Safe(source.Path)
            && definition.Start >= 0 && definition.End > definition.Start && definition.End <= source.Text.Length
            && source.Text[definition.Start..definition.End] == definition.Original, "Disposition current source/span differs from reviewed definition.");
        byte[] utf8 = new UTF8Encoding(false, true).GetBytes(source.Text);
        byte[] bom = [0xef, 0xbb, 0xbf, .. utf8];
        Need(source.Sha256 == Digest(utf8) || source.Sha256 == Digest(bom), "Disposition source text and physical hash differ.");
        Pin[] closure = Pins(document, "closure"), evidence = Pins(document, "evidence");
        Need(SamePins(closure, review.Closure) && SamePins(evidence, review.Evidence), "Disposition closure or review evidence differs from maintained exact pins.");
        Need(evidence.Length >= 2 && evidence.Select(value => value.Sha256).Distinct(StringComparer.Ordinal).Count() >= 2,
            "A disposition requires distinct primary and independent review evidence bodies.");
        Pin[] affected = closure.Where(value => value.Path == CorePrefix + definition.SourcePath).ToArray();
        Need(affected.Length == 1 && affected[0].Sha256 == source.Sha256
            && affected[0].Bytes == (source.Sha256 == Digest(utf8) ? utf8.LongLength : bom.LongLength), "Closure lacks the actual reviewed source bytes.");
        return new BoundRecord(review, closure, evidence);
    }

    internal static Adjustment Complete(BoundRecord bound, Partition[] partitions)
    {
        Need(partitions.Length == Groups.Length && partitions.Select(value => value.Group).ToHashSet(StringComparer.Ordinal).SetEquals(Groups),
            "Disposition application requires all three complete source-checked native partitions.");
        Review review = bound.Review;
        Partition[] groups = partitions.Where(value => value.Group == review.Group).ToArray();
        Need(groups.Length == 1 && groups[0].ReportSha256 == review.ReportSha256, "Disposition is bound to another original report.");
        Definition[] definitions = groups[0].Definitions.Where(value => value.Identity == review.Definition.Identity).ToArray();
        Need(definitions.Length == 1 && definitions[0].Id == review.Definition.Id && definitions[0].Status == review.Definition.Status
            && definitions[0].KilledBy.Length == 0, "Disposition live native definition/status differs from the reviewed original.");
        return new Adjustment(review.Definition.Identity, review.ReportSha256, review.Definition.Status, review.Kind);
    }

    internal static JsonObject Reference(Pin pin) => new() { ["path"] = pin.Path, ["sha256"] = pin.Sha256, ["bytes"] = pin.Bytes };
    private static Pin[] Pins(JsonObject value, string name)
    {
        Need(value[name] is JsonArray, "Disposition pins must be arrays.");
        Pin[] result = value[name]!.AsArray().Select(node => ReadReference(node as JsonObject ?? throw new InvalidDataException("Expected disposition pin object."))).ToArray();
        Need(result.Length > 0 && result.Select(pin => pin.Path).Distinct(StringComparer.Ordinal).Count() == result.Length, "Empty or duplicate disposition evidence closure.");
        return result;
    }
    private static bool SamePins(Pin[] left, Pin[] right) => right.Length > 0 && left.Length == right.Length
        && right.Select(pin => pin.Path).Distinct(StringComparer.Ordinal).Count() == right.Length
        && left.ToHashSet().SetEquals(right);
    private static void Fields(JsonObject value, params string[] fields) => Need(value.Count == fields.Length
        && value.Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal).SetEquals(fields), "Unexpected or missing disposition fields; approval/qualification flags are not evidence.");
    private static bool Safe(string path) => !string.IsNullOrWhiteSpace(path) && !path.StartsWith('/') && !path.Contains('\\') && !path.Contains(':')
        && path.Split('/').All(part => part.Length > 0 && part is not ("." or ".."));
    private static bool IsHash(string hash) => hash.Length == 64 && hash.All(value => char.IsAsciiDigit(value) || value is >= 'a' and <= 'f');
    private static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static string Text(JsonObject value, string name) => value[name] is JsonValue text && text.TryGetValue(out string? item) && item is not null
        ? item : throw new InvalidDataException("Missing disposition string: " + name);
    private static int Integer(JsonObject value, string name) => value[name] is JsonValue number && number.TryGetValue(out int item)
        ? item : throw new InvalidDataException("Missing disposition integer: " + name);
    private static JsonObject Object(JsonObject value, string name) => value[name] as JsonObject ?? throw new InvalidDataException("Missing disposition object: " + name);
    private static void Need(bool condition, string message) { if (!condition) { throw new InvalidDataException(message); } }
}
