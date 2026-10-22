using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using static CIS.Verification.HealthcheckMutationFiniteAssertionValidation;
using static CIS.Verification.HealthcheckMutationUnionModel;
using C = CIS.Verification.HealthcheckMutationFiniteAspNetCatalog;

namespace CIS.Verification;

internal static class HealthcheckMutationFiniteAspNetValidation
{
    private static string Text(JsonObject value, string name) => HealthcheckMutationFiniteAssertionValidation.Text(value, name);

    internal static void Document(JsonObject document)
    {
        Fields(document, "schema", "proofKind", "osLane", "original", "evidence", "sources", "toolchain");
        Need(Text(document, "schema") == C.Schema && Text(document, "proofKind") == ProofKind && Text(document, "osLane") == "ubuntu", "Unsupported finite ASP profile/lane.");
        JsonObject original = Object(document, "original"); Fields(original, "reportSha256", "definitionIdentities");
        Need(Text(original, "reportSha256") == C.OriginalReport && original["definitionIdentities"] is JsonArray identities
            && identities.Select(value => value?.GetValue<string>()).SequenceEqual(C.Targets.Select(value => value.Identity)), "ASP supplement must bind all four exact original definitions in order.");
        Fields(Object(document, "evidence"), "root", "inventory");
        Fields(Object(document, "sources"), "originalRoot", "originalManifest");
        Fields(Object(document, "toolchain"), "strykerCli", "strykerCore", "objectModel");
        foreach (JsonObject section in new[] { Object(document, "evidence"), Object(document, "sources"), Object(document, "toolchain") })
        {
            foreach ((string key, JsonNode? value) in section)
            {
                if (key.EndsWith("Root", StringComparison.Ordinal) || key == "root") { Need(value is JsonValue scalar && scalar.TryGetValue<string>(out string? text) && !string.IsNullOrWhiteSpace(text), "Missing physical finite input root."); }
                else { _ = HealthcheckMutationDispositionValidation.ReadReference(value as JsonObject ?? throw new InvalidDataException("A finite reference must be an object.")); }
            }
        }
    }

    internal static void Configuration(JsonObject parent, JsonObject finite)
    {
        Fields(parent, "stryker-config"); Fields(finite, "stryker-config");
        JsonObject before = Object(parent, "stryker-config"), after = Object(finite, "stryker-config");
        string[] changed = before.Select(pair => pair.Key).Union(after.Select(pair => pair.Key), StringComparer.Ordinal)
            .Where(key => !JsonNode.DeepEquals(before[key], after[key])).Order(StringComparer.Ordinal).ToArray();
        Need(changed.SequenceEqual(["mutate", "test-case-filter"]), "ASP finite config may change only source ranges and cohort.");
        Need(after["mutate"] is JsonArray ranges && ranges.Select(value => value?.GetValue<string>()).SequenceEqual(new[]
        { C.SourcePath + "{1194..1245}", C.SourcePath + "{5499..5505}" }), "ASP finite UTF16 ranges differ.");
        Need(Text(after, "test-case-filter") == C.Filter && Text(after, "mutation-level") == "Complete"
            && Text(after, "coverage-analysis") == "off" && Text(after, "configuration") == "Release" && Text(after, "target-framework") == "net10.0"
            && Text(after, "test-runner") == "vstest" && (int?)after["concurrency"] == 2 && (bool?)after["disable-bail"] == true
            && (int?)after["additional-timeout"] == 750000, "ASP finite execution or wait settings differ.");
        JsonObject threshold = Object(after, "thresholds"); Need((int?)threshold["high"] == 100 && (int?)threshold["low"] == 90 && (int?)threshold["break"] == 90, "ASP finite thresholds differ.");
        Need(after["test-projects"] is JsonArray projects && projects.Count == 1 && (string?)projects[0] == "../../tests/CIS.Core.IntegrationTests/CIS.Core.IntegrationTests.csproj", "ASP finite family differs.");
    }

    internal static void SourceDelta(JsonObject original, JsonObject derived)
    {
        JsonObject[] Rows(JsonObject manifest) => manifest["files"] is JsonArray entries ? entries.Select(value => value!.AsObject()).ToArray() : throw new InvalidDataException("Missing source manifest entries.");
        JsonObject[] first = Rows(original), last = Rows(derived);
        Need(first.Length == 1442 && last.Length == 1442 && Text(derived, "inputsSha256") == C.DerivedDigest, "ASP finite full source inventory differs.");
        var before = first.ToDictionary(row => Text(row, "path"), StringComparer.Ordinal); var after = last.ToDictionary(row => Text(row, "path"), StringComparer.Ordinal);
        Need(before.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(after.Keys), "ASP finite source path set changed.");
        string[] changed = before.Keys.Where(path => !JsonNode.DeepEquals(before[path], after[path])).ToArray();
        Need(changed.SequenceEqual([C.TestPath]) && Text(before[C.TestPath], "sha256") == C.OldTest && Text(after[C.TestPath], "sha256") == C.NewTest, "ASP finite source delta is not the one reviewed detector overlay.");
    }

    internal static Definition[] Definitions(JsonObject report, Source[] sources, Partition original)
    {
        Need(original.Group == "aspnet" && original.ReportSha256 == C.OriginalReport && sources.Count(source => source.Path == C.SourcePath && source.Sha256 == C.SourceSha256) == 1, "ASP original report/current source differs.");
        Definition[] finite = ParseReport(report, "aspnet", C.FiniteReport, C.NativeRoot, sources);
        Need(finite.Length == 99 && original.Definitions.Length == 99 && finite.Select(value => value.Identity).ToHashSet(StringComparer.Ordinal).SetEquals(original.Definitions.Select(value => value.Identity)), "ASP finite generation must retain all99 original definitions.");
        foreach (Definition target in C.Targets)
        {
            Definition old = original.Definitions.Single(value => value.Identity == target.Identity), current = finite.Single(value => value.Identity == target.Identity);
            Need(old.Id == target.Id && old.Status == "Killed" && current.Id == target.Id && current.Status == "Killed", "ASP finite target tuple/status differs.");
            string[] expected = C.DirectGuids(target.Id).Concat(target.Id is "195" or "196" ? C.CancellationGuids : []).Order(StringComparer.Ordinal).ToArray();
            Need(current.KilledBy.Order(StringComparer.Ordinal).SequenceEqual(expected), "ASP finite killedBy must be the exact own-cohort failed GUIDs.");
        }
        Need(finite.Where(value => !C.Targets.Any(target => target.Identity == value.Identity)).All(value => value.Status == "Ignored" && value.StatusReason == "Removed by mutate filter"), "ASP finite has a selected sibling or altered ignored definition.");
        JsonObject[] testFiles = Object(report, "testFiles").Select(pair => pair.Value!.AsObject()).ToArray();
        JsonObject[] receiving = Object(report, "testFiles").Where(pair => pair.Key == C.SourceRoot + "/" + C.TestPath).Select(pair => pair.Value!.AsObject()).ToArray();
        Need(receiving.Length == 1 && Sha(Text(receiving[0], "source")) == C.NewTest, "ASP native embedded receiving source differs.");
        string[] cases = testFiles.SelectMany(file => file["tests"]!.AsArray()).Select(value => value!["id"]!.GetValue<string>() + "\t" + value["name"]!.GetValue<string>()).Order(StringComparer.Ordinal).ToArray();
        Need(cases.SequenceEqual(C.Cases), "ASP finite report own six-case cohort differs.");
        return finite;
    }

    internal static bool AdmitsSymbolRole(string role, string before, string after) => role == C.TestRoles + "CIS.Core.pdb" && before == C.StalePdb && after == C.CleanPdb;

    internal static bool Secondary(string id, int pid, string guid, string method, int outcome, string message, string stack) =>
        ((id == "195" && pid == 949732) || (id == "196" && pid == 949748)) && C.CancellationGuids.Contains(guid, StringComparer.Ordinal)
        && method == C.Cancellation && outcome == 2
        && Sha(message) == "0fd564bc7d3aeec6c03e4da406119864c2b8f43e16ccb0f4eff8f19ac5fec210"
        && Sha(stack) == "bd33c538931b852fe629ffabeecf43f2e7a9d3a012530b13b263980f57120ddc";

    internal static void Cases(string[] initial, string[] fresh, string[] report)
    { Need(initial.SequenceEqual(C.Cases) && fresh.SequenceEqual(C.Cases) && report.SequenceEqual(C.Cases), "ASP finite discovery/report/initial/fresh own cohort differs."); }

    internal static string Sha(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
