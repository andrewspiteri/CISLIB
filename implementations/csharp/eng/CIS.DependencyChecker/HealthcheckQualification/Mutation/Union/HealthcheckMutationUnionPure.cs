using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace CIS.Verification;

internal static partial class HealthcheckMutationUnionModel
{
    private static readonly string[] Statuses = ["Killed", "Timeout", "Survived", "NoCoverage", "CompileError", "Ignored", "RuntimeError", "Pending"];

    internal static Definition[] ParseReport(JsonObject report, string group, string reportSha256,
        string nativeSourceRoot, Source[] expectedSources)
        => ParseReportCore(report, group, reportSha256, nativeSourceRoot, expectedSources,
            expectedSources.Where(source => source.Group == group).Select(source => source.Path).ToHashSet(StringComparer.Ordinal));

    internal static Definition[] ParseFileReport(JsonObject report, string group, string reportSha256,
        string nativeSourceRoot, Source[] expectedSources, string sourcePath)
    {
        Require(expectedSources.Any(source => source.Group == group && source.Path == sourcePath), "Foreign per-file source scope.");
        return ParseReportCore(report, group, reportSha256, nativeSourceRoot, expectedSources, new(StringComparer.Ordinal) { sourcePath });
    }

    private static Definition[] ParseReportCore(JsonObject report, string group, string reportSha256,
        string nativeSourceRoot, Source[] expectedSources, HashSet<string> reportSources)
    {
        Require(Groups.Contains(group, StringComparer.Ordinal) && IsHash(reportSha256), "Unknown partition or malformed original report hash.");
        Require(Text(report, "schemaVersion") == "2", "Unsupported native mutation schema.");
        Dictionary<string, Source> expected = ValidateSources(expectedSources);
        string root = nativeSourceRoot.Replace('\\', '/').TrimEnd('/');
        Require(root.StartsWith('/') || (root.Length >= 3 && char.IsAsciiLetter(root[0]) && root[1..3] == ":/"), "Native source root must be absolute.");
        Require(!root.Split('/').Contains("..", StringComparer.Ordinal), "Unsafe native source root.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var identities = new HashSet<string>(StringComparer.Ordinal);
        var definitions = new List<Definition>();
        foreach ((string nativePath, JsonNode? node) in Object(report, "files"))
        {
            string portable = nativePath.Replace('\\', '/');
            Require(portable.StartsWith(root + "/", StringComparison.Ordinal), "Native file is outside its declared original Core root.");
            string relative = portable[(root.Length + 1)..];
            Require(SafeRelative(relative), "Unsafe native file path.");
            Require(node is JsonObject, "Native file must be an object.");
            JsonObject file = node!.AsObject();
            JsonArray mutants = Array(file, "mutants");
            if (!expected.TryGetValue(relative, out Source? source) || source.Group != group || !reportSources.Contains(relative))
            {
                Require(mutants.Count == 0 && Text(file, "source") == "File ignored by mutate filter", "A report contains definitions or source outside its disjoint configured scope.");
                continue;
            }
            Require(seen.Add(relative), "Duplicate native source identity.");
            Require(Text(file, "source") == source.Text, "Native embedded source does not match the pinned physical source.");
            foreach (JsonNode? mutantNode in mutants)
            {
                Require(mutantNode is JsonObject, "Native definition must be an object.");
                JsonObject mutant = mutantNode!.AsObject();
                string id = Text(mutant, "id"), status = Text(mutant, "status"), mutator = Text(mutant, "mutatorName");
                Require(IsId(id) && ids.Add(id), "Invalid or duplicate partition-local native ID.");
                Require(Statuses.Contains(status, StringComparer.Ordinal) && mutator.Length > 0, "Unknown native status or empty operator.");
                JsonObject location = Object(mutant, "location");
                JsonObject start = Object(location, "start"), end = Object(location, "end");
                int from = Offset(source.Text, Number(start, "line"), Number(start, "column"));
                int to = Offset(source.Text, Number(end, "line"), Number(end, "column"));
                Require(to > from, "Native mutation span must be nonempty and forward.");
                string[] killedBy = [];
                if (mutant.TryGetPropertyValue("killedBy", out JsonNode? killers))
                {
                    Require(killers is JsonArray, "Native killedBy must be a GUID array.");
                    killedBy = killers!.AsArray().Select(n =>
                    {
                        Require(n is JsonValue value && value.TryGetValue(out string? _), "Native killedBy is not a GUID string.");
                        string raw = n!.GetValue<string>();
                        Require(Guid.TryParse(raw, out _), "Malformed native killedBy GUID.");
                        return Guid.Parse(raw).ToString();
                    }).ToArray();
                    Require(killedBy.Distinct(StringComparer.Ordinal).Count() == killedBy.Length, "Duplicate native killedBy GUID.");
                }
                string? reason = mutant["statusReason"] is null ? null : Text(mutant, "statusReason");
                var definition = new Definition(id, relative, source.Sha256, from, to, mutator,
                    source.Text[from..to], Text(mutant, "replacement"), status, killedBy, reason);
                Require(identities.Add(definition.Identity), "Duplicate source-bound mutation definition.");
                definitions.Add(definition);
            }
        }
        Require(seen.SetEquals(reportSources), "A configured shipping source is missing from the native report.");
        return definitions.ToArray();
    }

    internal static JsonObject Evaluate(Source[] expectedSources, Partition[] partitions, Adjustment[] adjustments)
    {
        Dictionary<string, Source> expected = ValidateSources(expectedSources);
        Require(partitions.Length == Groups.Length && partitions.Select(p => p.Group).ToHashSet(StringComparer.Ordinal).SetEquals(Groups), "Exactly one complete report per configured partition is required.");
        var all = new Dictionary<string, (Partition Partition, Definition Definition)>(StringComparer.Ordinal);
        foreach (Partition partition in partitions)
        {
            Require(IsHash(partition.ReportSha256), "Invalid original report hash.");
            Require(partition.Sources.Select(s => s.Path).Distinct(StringComparer.Ordinal).Count() == partition.Sources.Length, "Repeated partition source.");
            Require(partition.Sources.Select(s => s.Path).ToHashSet(StringComparer.Ordinal).SetEquals(expected.Values.Where(s => s.Group == partition.Group).Select(s => s.Path)), "Missing, overlapping or foreign source scope.");
            foreach (Source source in partition.Sources)
            { Require(expected.TryGetValue(source.Path, out Source? value) && value == source && source.Group == partition.Group, "Stale source hash/text or foreign partition."); }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (Definition definition in partition.Definitions)
            {
                Require(IsId(definition.Id) && ids.Add(definition.Id), "Duplicate or invalid partition-local native ID.");
                Require(expected.TryGetValue(definition.SourcePath, out Source? source) && source.Group == partition.Group
                    && source.Sha256 == definition.SourceSha256, "Definition is outside its exact pinned partition.");
                Require(definition.Start >= 0 && definition.End > definition.Start && definition.End <= source!.Text.Length
                    && source.Text[definition.Start..definition.End] == definition.Original, "Definition span/original text differs.");
                Require(Statuses.Contains(definition.Status, StringComparer.Ordinal) && !string.IsNullOrEmpty(definition.Mutator), "Unknown definition status/operator.");
                Require(all.TryAdd(definition.Identity, (partition, definition)), "Duplicate source-bound definition identity.");
            }
            Require(partition.QualifiedKilledIds.Distinct(StringComparer.Ordinal).Count() == partition.QualifiedKilledIds.Length, "Duplicate qualified native ID.");
            Require(partition.QualifiedKilledIds.All(id => partition.Definitions.Any(d => d.Id == id && d.Status == "Killed")), "An audit cannot turn a non-killed native definition into a kill without an attestation.");
            foreach (IntegrityIssue issue in partition.IntegrityIssues)
            { Require(issue.MutantIds.Distinct(StringComparer.Ordinal).Count() == issue.MutantIds.Length && issue.MutantIds.All(ids.Contains), "An integrity issue has duplicate/unknown native selector IDs."); }
        }
        var accepted = new Dictionary<string, Adjustment>(StringComparer.Ordinal);
        foreach (Adjustment adjustment in adjustments)
        {
            Require(accepted.TryAdd(adjustment.DefinitionIdentity, adjustment), "Duplicate/conflicting adjustments are forbidden.");
            Require(all.TryGetValue(adjustment.DefinitionIdentity, out var binding), "An adjustment does not identify an original definition.");
            Require(binding.Partition.ReportSha256 == adjustment.OriginalReportSha256 && binding.Definition.Status == adjustment.OriginalStatus, "Adjustment original report/status binding differs.");
            bool eligible = adjustment.Kind switch
            {
                "AssertionKill" => adjustment.OriginalStatus is "Killed" or "Timeout" or "Survived" or "NoCoverage",
                "Equivalent" or "ReviewedNoncritical" => adjustment.OriginalStatus is "Survived" or "NoCoverage",
                "NonTermination" => adjustment.OriginalStatus == "Timeout",
                _ => false
            };
            Require(eligible, "Unsupported adjustment kind/original status.");
        }
        var raw = Counts();
        var effective = Counts();
        effective["UnqualifiedKilled"] = 0;
        effective["ExcludedEquivalent"] = 0;
        var results = new JsonArray();
        var unresolved = new JsonArray();
        var issues = new JsonArray();
        long numerator = 0, denominator = 0;
        foreach (Partition partition in partitions.OrderBy(p => p.Group, StringComparer.Ordinal))
        {
            var partRaw = Counts();
            long partNumerator = 0, partDenominator = 0;
            foreach (Definition definition in partition.Definitions)
            {
                raw[definition.Status]++; partRaw[definition.Status]++;
                accepted.TryGetValue(definition.Identity, out Adjustment? adjustment);
                bool valid = definition.Status is "Killed" or "Timeout" or "Survived" or "NoCoverage";
                bool excluded = adjustment?.Kind == "Equivalent";
                bool killed = adjustment?.Kind == "AssertionKill" || (definition.Status == "Killed" && partition.QualifiedKilledIds.Contains(definition.Id, StringComparer.Ordinal));
                if (valid && !excluded) { partDenominator++; }
                if (killed) { partNumerator++; effective["Killed"]++; }
                else if (excluded) { effective["ExcludedEquivalent"]++; }
                else if (definition.Status == "Killed") { effective["UnqualifiedKilled"]++; }
                else { effective[definition.Status]++; }
                string? reason = definition.Status switch
                {
                    "Killed" when !killed => "Native label lacks an exact normal semantic assertion witness.",
                    "Timeout" when !killed && adjustment?.Kind != "NonTermination" => "Timeout requires an isolated reviewed assertion or confirmed nontermination disposition.",
                    "Survived" or "NoCoverage" when !killed && !excluded && adjustment?.Kind != "ReviewedNoncritical" => "Surviving or uncovered definition has no individual reviewed disposition.",
                    "RuntimeError" => "Native runtime error remains unresolved.",
                    "Pending" => "Original native execution is incomplete.",
                    _ => null
                };
                if (reason is not null)
                {
                    unresolved.Add(new JsonObject
                    {
                        ["group"] = partition.Group,
                        ["id"] = definition.Id,
                        ["definitionIdentity"] = definition.Identity,
                        ["rawStatus"] = definition.Status,
                        ["reason"] = reason
                    });
                }
            }
            foreach (IntegrityIssue issue in partition.IntegrityIssues)
            {
                bool resolved = issue.MutantIds.Length > 0 && issue.MutantIds.All(id =>
                {
                    Definition definition = partition.Definitions.Single(d => d.Id == id);
                    return accepted.TryGetValue(definition.Identity, out Adjustment? value) && value.Kind is "AssertionKill" or "NonTermination";
                });
                if (!resolved) { issues.Add(new JsonObject { ["group"] = partition.Group, ["code"] = issue.Code, ["detail"] = issue.Detail, ["mutantIds"] = new JsonArray(issue.MutantIds.Select(id => JsonValue.Create(id)).ToArray()) }); }
            }
            numerator += partNumerator; denominator += partDenominator;
            long partRawDenominator = ValidDenominator(partRaw);
            results.Add(new JsonObject
            {
                ["group"] = partition.Group,
                ["reportSha256"] = partition.ReportSha256,
                ["rawCounts"] = JsonSerializerNode(partRaw),
                ["rawConservativePercent"] = Percent(partRaw["Killed"], partRawDenominator),
                ["effectiveNumerator"] = partNumerator,
                ["effectiveDenominator"] = partDenominator
            });
        }
        bool complete = raw["Pending"] == 0;
        bool threshold = MeetsThreshold(numerator, denominator);
        long rawDenominator = ValidDenominator(raw), nativeDetected = raw["Killed"] + raw["Timeout"];
        return new JsonObject
        {
            ["schema"] = "cis.healthcheck.mutation-union/v1-development",
            ["complete"] = complete,
            ["qualified"] = complete && threshold && unresolved.Count == 0 && issues.Count == 0,
            ["requiredConservativePercent"] = 90,
            ["rawCounts"] = JsonSerializerNode(raw),
            ["rawAggregate"] = new JsonObject
            {
                ["killed"] = raw["Killed"],
                ["denominator"] = rawDenominator,
                ["nativeDetected"] = nativeDetected,
                ["nativePercent"] = Percent(nativeDetected, rawDenominator),
                ["conservativePercent"] = Percent(raw["Killed"], rawDenominator)
            },
            ["aggregate"] = new JsonObject
            {
                ["numerator"] = numerator,
                ["denominator"] = denominator,
                ["percent"] = Percent(numerator, denominator),
                ["thresholdMet"] = threshold
            },
            ["effectiveCounts"] = JsonSerializerNode(effective),
            ["partitions"] = results,
            ["issues"] = issues,
            ["unresolved"] = unresolved
        };
    }

    internal static bool MeetsThreshold(long killed, long denominator)
    {
        Require(killed >= 0 && denominator >= 0 && killed <= denominator, "Invalid conservative numerator/denominator.");
        return denominator > 0 && (BigInteger)killed * 10 >= (BigInteger)denominator * 9;
    }

    private static Dictionary<string, Source> ValidateSources(Source[] sources)
    {
        Require(sources.Length > 0 && sources.Select(s => s.Group).ToHashSet(StringComparer.Ordinal).SetEquals(Groups), "The complete three-group source catalog is required.");
        var result = new Dictionary<string, Source>(StringComparer.Ordinal);
        foreach (Source source in sources)
        {
            Require(SafeRelative(source.Path) && source.Path.StartsWith("Healthcheck/", StringComparison.Ordinal)
                && source.Path.EndsWith(".cs", StringComparison.Ordinal) && IsHash(source.Sha256) && source.Text is not null
                && result.TryAdd(source.Path, source), "Malformed or overlapping source catalog entry.");
            byte[] utf8 = new UTF8Encoding(false, true).GetBytes(source.Text);
            string textHash = Convert.ToHexStringLower(SHA256.HashData(utf8));
            // ReadAllText omits an optional UTF8 BOM. Both physical encodings
            // preserve the exact native C# text; the file reader binds which
            // bytes actually existed, without newline normalization.
            byte[] withBom = [0xef, 0xbb, 0xbf, .. utf8];
            Require(source.Sha256 == textHash || source.Sha256 == Convert.ToHexStringLower(SHA256.HashData(withBom)), "Source text does not match its UTF8 physical hash.");
        }
        return result;
    }

    private static int Offset(string source, int line, int column)
    {
        Require(line > 0 && column > 0, "Source locations are one-based UTF16 positions.");
        int start = 0;
        for (int current = 1; current < line; current++)
        { int newline = source.IndexOf('\n', start); Require(newline >= 0, "Source line is outside the pinned text."); start = newline + 1; }
        int end = source.IndexOf('\n', start);
        if (end < 0) { end = source.Length; }
        if (end > start && source[end - 1] == '\r') { end--; }
        Require(column - 1 <= end - start, "Source column is outside the pinned line.");
        int offset = start + column - 1;
        Require(offset == 0 || offset == source.Length || !char.IsLowSurrogate(source[offset]) || !char.IsHighSurrogate(source[offset - 1]), "Mutation boundary splits a UTF16 surrogate pair.");
        return offset;
    }

    private static bool IsId(string id) => int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out int number)
        && number >= 0 && number.ToString(CultureInfo.InvariantCulture) == id;
    private static bool IsHash(string hash) => hash.Length == 64 && hash.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f');
    private static bool SafeRelative(string path) => !string.IsNullOrWhiteSpace(path) && !path.Contains('\\') && !path.StartsWith('/')
        && !path.Contains(':') && path.Split('/').All(segment => segment.Length > 0 && segment is not ("." or ".."));
    private static string Text(JsonObject value, string name) => value[name] is JsonValue item && item.TryGetValue(out string? text) && text is not null ? text : throw new InvalidDataException("Missing/malformed string: " + name);
    private static int Number(JsonObject value, string name) => value[name] is JsonValue item && item.TryGetValue(out int number) ? number : throw new InvalidDataException("Missing/malformed integer: " + name);
    private static JsonObject Object(JsonObject value, string name) => value[name] as JsonObject ?? throw new InvalidDataException("Missing/malformed object: " + name);
    private static JsonArray Array(JsonObject value, string name) => value[name] as JsonArray ?? throw new InvalidDataException("Missing/malformed array: " + name);
    private static Dictionary<string, long> Counts() => Statuses.ToDictionary(s => s, _ => 0L, StringComparer.Ordinal);
    private static long ValidDenominator(Dictionary<string, long> counts) => counts["Killed"] + counts["Timeout"] + counts["Survived"] + counts["NoCoverage"];
    private static double? Percent(long numerator, long denominator) => denominator == 0 ? null : numerator * 100d / denominator;
    private static JsonNode JsonSerializerNode(Dictionary<string, long> counts) => System.Text.Json.JsonSerializer.SerializeToNode(counts)!;
    private static void Require([DoesNotReturnIf(false)] bool condition, string message) { if (!condition) { throw new InvalidDataException(message); } }
}
