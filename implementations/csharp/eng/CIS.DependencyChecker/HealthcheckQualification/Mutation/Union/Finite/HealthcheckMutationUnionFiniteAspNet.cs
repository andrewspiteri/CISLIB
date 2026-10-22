using System.Text.Json.Nodes;
using static CIS.Verification.HealthcheckMutationUnionModel;
using A = CIS.Verification.HealthcheckMutationFiniteAspNetCatalog;
using E = CIS.Verification.HealthcheckMutationFiniteAspNetEvidence;
using F = CIS.Verification.HealthcheckMutationFiniteAssertionValidation;
using V = CIS.Verification.HealthcheckMutationFiniteAspNetValidation;

namespace CIS.Verification;

internal static partial class HealthcheckMutationUnionVerification
{
    private static FiniteAssertionRecord ReadFiniteAspNet(PinnedInputs files, JsonObject document, Source[] sources, string lane)
    {
        V.Document(document); Need(lane == "ubuntu", "The fixed ASP finite proof is Ubuntu-only.");
        var paths = new Dictionary<string, string>(StringComparer.Ordinal);
        string Pin(JsonObject descriptor, string expected)
        {
            var reference = HealthcheckMutationDispositionValidation.ReadReference(descriptor);
            Need(reference.Sha256 == expected, "Finite ASP reference fingerprint differs.");
            string path = files.Pin(HealthcheckMutationDispositionValidation.Reference(reference)); files.GuardRoot(Path.GetDirectoryName(path)!); return path;
        }
        JsonObject evidence = Item(document["evidence"]), sourceSection = Item(document["sources"]), tools = Item(document["toolchain"]);
        string root = files.Resolve(String(evidence, "root")); files.GuardRoot(root); paths.Add("evidenceRoot", root);
        string inventory = Pin(Item(evidence["inventory"]), A.Inventory); paths.Add("inventory", inventory);
        JsonObject[] entries = Items(files.ReadDocument(inventory), "entries").Select(Item).Where(value => String(value, "Kind") != "Directory").ToArray();
        Need(entries.Length == 277 && entries.Select(value => String(value, "Path")).Distinct(StringComparer.Ordinal).Count() == 277, "Finite ASP physical evidence inventory differs.");
        foreach (JsonObject entry in entries)
        {
            Need(String(entry, "Kind") == "RegularFile", "Unsupported ASP evidence entry kind."); string relative = E.Safe(String(entry, "Path"));
            string path = files.Pin(new JsonObject { ["path"] = Path.Combine(root, relative), ["sha256"] = String(entry, "Sha256"), ["bytes"] = Integer(entry, "Bytes") }); paths.Add(relative, path);
        }
        foreach (string directory in new[] { A.Run + "-native/logs", A.Run + "-before-bodies", A.Run + "-after-native-bodies", A.Run + "-fresh-bodies", A.Run + "-children" })
        {
            string actualRoot = Path.Combine(root, directory); files.TrackInventory(actualRoot);
            string[] expected = entries.Select(value => String(value, "Path")).Where(value => value.StartsWith(directory + "/", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToArray();
            Need(Directory.EnumerateFiles(actualRoot, "*", SearchOption.AllDirectories).Select(path => directory + "/" + Path.GetRelativePath(actualRoot, path).Replace('\\', '/')).Order(StringComparer.Ordinal).SequenceEqual(expected), "Finite ASP relevant physical inventory has missing/foreign files.");
        }
        paths.Add("strykerCli", Pin(Item(tools["strykerCli"]), A.StrykerCli)); paths.Add("strykerCore", Pin(Item(tools["strykerCore"]), A.StrykerCore)); paths.Add("objectModel", Pin(Item(tools["objectModel"]), A.ObjectModel));
        string originalManifest = Pin(Item(sourceSection["originalManifest"]), A.OriginalManifest), originalRoot = files.Resolve(String(sourceSection, "originalRoot")); files.GuardRoot(originalRoot);
        JsonObject original = files.ReadDocument(originalManifest), derived = files.ReadDocument(paths[A.Run + "-inputs-before-native/source-inputs.json"]);
        V.SourceDelta(original, derived);
        foreach (string phase in new[] { "before-native", "after-native", "after-fresh" })
        { Need(Hash(paths[A.Run + "-inputs-" + phase + "/source-inputs.json"]) == A.DerivedManifest, "ASP contemporaneous source inventory differs."); }
        JsonObject[] inputRows = Items(original, "files").Select(Item).ToArray();
        Need(inputRows.Count(row => String(row, "path").StartsWith(CorePrefix, StringComparison.Ordinal) && String(row, "path").EndsWith(".cs", StringComparison.Ordinal)) == 146, "ASP full Core source inventory differs.");
        foreach (JsonObject row in inputRows)
        {
            string relative = String(row, "path"), native = Portable(relative); var pin = (JsonObject)row.DeepClone(); pin["path"] = Path.Combine(originalRoot, native); files.Pin(pin);
            if (ShippingInput(relative)) { var shipping = (JsonObject)row.DeepClone(); shipping["path"] = Path.Combine(files.Repository, native); files.Pin(shipping); }
        }
        foreach (Source source in sources)
        { Need(inputRows.Single(row => String(row, "path") == CorePrefix + source.Path)["sha256"]!.GetValue<string>() == source.Sha256, "ASP finite/current shipping Healthcheck differs."); }
        Need(Hash(paths["aspnet-finite-payload-28/http-tests.cs.txt"]) == A.NewTest, "Actual finite test overlay differs.");
        string parent = MaintainedConfig(files, "aspnet"); Need(Hash(parent) == A.ParentConfig, "ASP full parent config fingerprint differs.");
        Need(Hash(paths[A.Run + "-plan/aspnet.json"]) == A.FiniteConfig && Hash(paths[A.Run + "-native/reports/mutation-report.json"]) == A.FiniteReport, "ASP finite raw report/config differs.");
        V.Configuration(files.ReadDocument(parent), files.ReadDocument(paths[A.Run + "-plan/aspnet.json"]));
        JsonArray logInventory = JsonNode.Parse(File.ReadAllBytes(paths[A.Run + "-parent-audit/native-logs.before.json"]))!.AsArray();
        string[] logs = logInventory.Select(value =>
        {
            JsonObject row = Item(value); string origin = String(row, "path"); Need(origin.StartsWith(A.Task + A.Run + "-native/logs/", StringComparison.Ordinal), "ASP native log origin differs.");
            string relative = E.Safe(origin[A.Task.Length..]); string path = paths[relative]; Need(Hash(path) == String(row, "sha256") && new FileInfo(path).Length == Integer(row, "bytes"), "ASP native log inventory/body differs."); return path;
        }).ToArray();
        Need(logs.Length == 10 && logs.Distinct(StringComparer.Ordinal).Count() == 10, "ASP complete native log inventory differs.");
        // The fixed inventory pins the raw argv/plan/phase files too. Inspect
        // them rather than importing a previous review's qualified Boolean.
        JsonObject invocation = files.ReadDocument(paths[A.Run + "-argv.json"]), plan = files.ReadDocument(paths[A.Run + "-plan/plan.json"]);
        Need(String(invocation, "cwd") == A.NativeRoot && String(invocation, "requestedSubjectRuntime") == "10.0.12" && String(invocation, "requestedSdk") == "10.0.401"
            && (bool?)invocation["CisMutationTesting"] == true && String(plan, "sourceRoot") == A.SourceRoot && (bool?)plan["actualHttpOverlayApplied"] == true
            && String(plan, "derivedManifestSha256") == A.DerivedManifest, "ASP actual invocation/overlay scope differs.");
        string[] argv = Items(invocation, "argv").Select(value => value!.GetValue<string>()).ToArray();
        Need(argv.SequenceEqual(new[] { "strace", "--seccomp-bpf", "-f", "-ttt", "-s", "8192", "-e", "trace=process", "-o", A.Task + A.Run + "-process.log", A.Task + "dotnet/dotnet", "exec", "--fx-version", "8.0.31", "--roll-forward", "Disable", A.Task + "release-next-stryker-4.16.0/Stryker.CLI.dll", "--config-file", A.Task + A.Run + "-plan/aspnet.json", "--output", A.Task + A.Run + "-native", "--log-to-file", "--diag" }), "ASP actual native argv differs.");
        foreach (string phase in new[] { "native", "baseline", "fresh", "after-native-bodies", "fresh-bodies", "inputs-before-native", "inputs-after-native", "inputs-after-fresh", "tools.after" })
        { Need(File.ReadAllText(paths[A.Run + "-" + phase + ".exit"]).Trim() == "0", "ASP required actual native phase failed: " + phase); }
        return new(document, sources, paths, logs, root, null, false, true);
    }

    private static Adjustment[] CompleteFiniteAspNet(FiniteAssertionRecord record, Partition[] originals, JsonArray observations)
    {
        string root = record.CaptureRoot; string At(string relative) => record.Paths[relative]; JsonObject Read(string relative) => JsonNode.Parse(File.ReadAllBytes(At(relative)))!.AsObject();
        Partition original = originals.Single(value => value.Group == "aspnet"); JsonObject report = Read(A.Run + "-native/reports/mutation-report.json"), config = Read(A.Run + "-plan/aspnet.json");
        Definition[] definitions = V.Definitions(report, record.Sources, original);
        NativeAuditResult audit = HealthcheckMutationNativeAudit.Audit(report, config, record.Logs, record.Paths["objectModel"]);
        JsonObject native = E.Native(audit, record.Logs, At(A.Run + "-process.log")); string[] cases = E.Baselines(root);
        DateTimeOffset Time(string name) => F.Utc(File.ReadAllText(At(A.Run + "-" + name + ".utc")).Trim());
        JsonObject restored = E.Captures(Path.Combine(root, A.Run + "-before-bodies"), Path.Combine(root, A.Run + "-after-native-bodies"), Path.Combine(root, A.Run + "-fresh-bodies"),
            Read(A.Run + "-before-bodies/capture.json"), Read(A.Run + "-after-native-bodies/capture.json"), Read(A.Run + "-fresh-bodies/capture.json"),
            Time("native.started"), Time("native.completed"), Time("baseline.completed"), Time("fresh.started"), Time("fresh.completed"));
        Need(File.ReadAllText(At(A.Run + "-child-audit.exit")).Trim() == "1" && File.ReadAllText(At(A.Run + "-child-audit.log")).Contains("No bounded actual child launch evidence", StringComparison.Ordinal), "ASP empty finite child audit was not retained as unqualified.");
        Need(File.ReadAllText(At(A.Run + "-instrumented-bodies.exit")).Trim() == "1", "ASP active-body observation limitation changed.");
        JsonObject session = Read(A.Run + "-children/session.json"); Need(String(session, "LanguageRoot") == A.LanguageRoot && String(session, "ConfigurationSha256") == A.FiniteConfig
            && String(session, "StrykerCoreSha256") == A.StrykerCore && String(session, "CoreLibrarySha256") == "26304a2985357b9ee277f273667fcd9c892edae3ee6eba76f739033e95eb39b3", "ASP actual child session/runtime pins differ.");
        Adjustment[] adjustments = A.Targets.Select(target => new Adjustment(target.Identity, A.OriginalReport, "Killed", "AssertionKill")).ToArray();
        int additional = A.Targets.Count(target => !original.QualifiedKilledIds.Contains(target.Id, StringComparer.Ordinal));
        observations.Add(new JsonObject
        {
            ["proofKind"] = F.ProofKind,
            ["profile"] = A.Schema,
            ["originalReportSha256"] = A.OriginalReport,
            ["originalDefinitionIdentities"] = new JsonArray(A.Targets.Select(target => (JsonNode?)JsonValue.Create(target.Identity)).ToArray()),
            ["finiteReportSha256"] = A.FiniteReport,
            ["validated"] = true,
            ["appliedKind"] = "AssertionKill",
            ["validatedAdjustmentCount"] = adjustments.Length,
            ["finiteCases"] = cases.Length,
            ["finiteGeneratedDefinitions"] = definitions.Length,
            ["finiteRawKilled"] = 4,
            ["finiteRawIgnored"] = 95,
            ["nativeDetails"] = native,
            ["restoration"] = restored,
            ["numeratorDelta"] = additional,
            ["denominatorDelta"] = 0,
            ["finiteChildQualified"] = false,
            ["shellPhaseTimestampPrecision"] = "Preserved whole UTC seconds; completed+one second is the exclusive upper bound, not an invented observed timestamp.",
            ["finiteInProcessOnly"] = true,
            ["originalFullChildAuditStillRequired"] = true,
            ["historicalInjectedBodyClaim"] = false,
            ["rawReportRewritten"] = false,
            ["globalIntegrityIssuesWaived"] = false,
            ["scope"] = "Four exact original ASP definitions; no fourth partition and no blanket original ASP issue waiver."
        });
        return adjustments;
    }
}
