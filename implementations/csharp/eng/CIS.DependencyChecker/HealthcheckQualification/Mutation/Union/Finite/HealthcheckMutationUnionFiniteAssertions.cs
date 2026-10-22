using System.Globalization;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using static CIS.Verification.HealthcheckMutationUnionModel;
using Finite = CIS.Verification.HealthcheckMutationFiniteAssertionValidation;

namespace CIS.Verification;

internal static partial class HealthcheckMutationUnionVerification
{
    private sealed record FiniteAssertionRecord(JsonObject Document, Source[] Sources, Dictionary<string, string> Paths,
        string[] Logs, string CaptureRoot, JsonObject? CurrentCapture, bool CurrentCancellation = false);

    private static FiniteAssertionRecord[] ReadFiniteAssertionReferences(PinnedInputs files, JsonObject manifest, Source[] sources, string lane)
    {
        if (!manifest.TryGetPropertyValue("assertionReruns", out JsonNode? node)) { return []; }
        Need(node is JsonArray, "Optional assertionReruns must be a non-null reference array.");
        var records = new List<FiniteAssertionRecord>(); var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonNode? value in node!.AsArray())
        {
            var reference = HealthcheckMutationDispositionValidation.ReadReference(Item(value));
            string path = files.Pin(HealthcheckMutationDispositionValidation.Reference(reference));
            Need(references.Add(Path.GetFullPath(path)) && records.Count < 2, "Duplicate/conflicting finite assertion reference.");
            files.GuardRoot(Path.GetDirectoryName(path)!);
            JsonObject document = files.ReadDocument(path);
            string identity = String(Item(document["original"]), "definitionIdentity");
            Need(records.All(record => String(Item(record.Document["original"]), "definitionIdentity") != identity), "Duplicate finite proof for one original definition.");
            if ((string?)document["schema"] == HealthcheckMutationFiniteCancellationEvidence.Schema)
            { records.Add(ReadFiniteCancellation(files, document, sources, lane)); continue; }
            Finite.Spec spec = HealthcheckMutationFiniteAssertionCatalog.IdleWithdrawal;
            Finite.Document(document, spec);
            Need(lane == "ubuntu", "The supported finite assertion belongs only to the Ubuntu lane.");
            var paths = new Dictionary<string, string>(StringComparer.Ordinal);
            void PinField(string section, string name, string key, string? expected = null)
            {
                JsonObject pin = Item(Item(document[section])[name]);
                var strict = HealthcheckMutationDispositionValidation.ReadReference(pin);
                Need(expected is null || strict.Sha256 == expected, "Finite evidence fingerprint differs: " + key);
                string physical = files.Pin(HealthcheckMutationDispositionValidation.Reference(strict));
                files.GuardRoot(Path.GetDirectoryName(physical)!); paths.Add(key, physical);
            }
            PinField("finite", "report", "report", spec.FiniteReportSha256);
            PinField("finite", "configuration", "configuration", HealthcheckMutationFiniteAssertionCatalog.FiniteConfigSha256);
            PinField("finite", "nativeExit", "nativeExit"); PinField("finite", "invocation", "invocation");
            PinField("finite", "processTrace", "processTrace", HealthcheckMutationFiniteAssertionCatalog.ProcessTraceSha256);
            PinField("sources", "beforeManifest", "beforeManifest", spec.ManifestSha256);
            PinField("sources", "afterManifest", "afterManifest", spec.ManifestSha256);
            foreach (string phase in new[] { "initialTrx", "initialExit", "freshTrx", "freshExit" }) { PinField("baselines", phase, phase); }
            PinField("toolchain", "strykerCore", "strykerCore", HealthcheckMutationFiniteAssertionCatalog.StrykerCoreSha256);
            PinField("toolchain", "strykerCli", "strykerCli", HealthcheckMutationFiniteAssertionCatalog.StrykerCliSha256);
            PinField("toolchain", "objectModel", "objectModel", HealthcheckMutationFiniteAssertionCatalog.ObjectModelSha256);
            foreach (string role in new[] { "backup", "backupPaths", "copyInventory", "preFreshMarker", "sourceCore", "testCore", "unitAssembly", "unitDeps", "unitRuntimeConfig" })
            { PinField("capture", role, role); }
            JsonObject capture = Item(document["capture"]); JsonObject? currentCapture = null; string captureRoot = "";
            if (capture.ContainsKey("roleProvenance")) { PinField("capture", "roleProvenance", "roleProvenance", spec.RoleHistorySha256); }
            if (capture.ContainsKey("postFreshObservation"))
            {
                PinField("capture", "postFreshObservation", "postFreshObservation", HealthcheckMutationFiniteAssertionCatalog.CaptureSha256);
                currentCapture = files.ReadDocument(paths["postFreshObservation"]); captureRoot = Path.GetDirectoryName(paths["postFreshObservation"])!;
                files.TrackInventory(captureRoot);
                foreach (JsonObject row in Items(currentCapture, "files").Select(Item))
                {
                    string origin = String(row, "nativeOrigin"), role = String(row, "role");
                    Need(role.All(char.IsAsciiLetter), "Unsafe finite capture role.");
                    string name = role + "-" + origin[(origin.LastIndexOf('/') + 1)..];
                    Need(Portable(name) == name && !name.Contains('/'), "Unsafe finite captured copy.");
                    files.Pin(new JsonObject { ["path"] = Path.Combine(captureRoot, name), ["sha256"] = String(row, "sha256"), ["bytes"] = Integer(row, "bytes") });
                }
            }
            string[] logs = ReadLogs(files, new JsonObject { ["logsManifest"] = document["logsManifest"]!.DeepClone() });
            string nativeRoot = String(Item(document["finite"]), "nativeSourceRoot");
            Need(nativeRoot == HealthcheckMutationFiniteAssertionCatalog.NativeRoot, "Finite report native source origin differs.");
            Need(File.ReadAllText(paths["nativeExit"]).Trim() == "0", "Finite mutation process did not close normally.");
            string parentPath = MaintainedConfig(files, "unit-runtime");
            Need(Hash(parentPath) == HealthcheckMutationFiniteAssertionCatalog.ParentConfigSha256, "Finite full-parent configuration fingerprint changed.");
            JsonObject parent = files.ReadDocument(parentPath), finiteConfig = files.ReadDocument(paths["configuration"]);
            Finite.Configuration(parent, finiteConfig, spec.Original.SourcePath, spec.Original.Start, spec.Original.End);
            ValidateFiniteSources(files, document, paths, sources, spec);
            VerifyFiniteCopyInventory(paths, nativeRoot);
            VerifyFiniteInvocation(paths, nativeRoot);
            records.Add(new(document, sources, paths, logs, captureRoot, currentCapture));
        }
        return records.ToArray();
    }

    private static Adjustment[] CompleteFiniteAssertions(FiniteAssertionRecord[] records, Partition[] originals, JsonArray observations)
    {
        Need(originals.Length == Groups.Length && originals.Select(value => value.Group).ToHashSet(StringComparer.Ordinal).SetEquals(Groups), "Finite proof cannot replace a complete original partition.");
        var adjustments = new List<Adjustment>(); Finite.Spec spec = HealthcheckMutationFiniteAssertionCatalog.IdleWithdrawal;
        foreach (FiniteAssertionRecord record in records)
        {
            if (record.CurrentCancellation)
            { Adjustment? cancellation = CompleteFiniteCancellation(record, originals, observations); if (cancellation is not null) { adjustments.Add(cancellation); } continue; }
            Dictionary<string, string> paths = record.Paths; Partition original = originals.Single(value => value.Group == "unit-runtime");
            JsonObject report = JsonNode.Parse(File.ReadAllBytes(paths["report"]))!.AsObject(), config = JsonNode.Parse(File.ReadAllBytes(paths["configuration"]))!.AsObject();
            string nativeRoot = String(Item(record.Document["finite"]), "nativeSourceRoot");
            Definition[] definitions = Finite.Definitions(report, Hash(paths["report"]), nativeRoot, record.Sources, original, spec);
            VerifyFiniteTestSource(report, spec);
            NativeAuditResult audit = HealthcheckMutationNativeAudit.Audit(report, config, record.Logs, paths["objectModel"]);
            Finite.NativeWitness(audit, definitions, spec, record.Logs);
            string[] cases = Finite.Baselines(paths["initialTrx"], paths["freshTrx"], paths["initialExit"], paths["freshExit"], paths["preFreshMarker"], spec);
            string[] reportCases = Item(report["testFiles"]).SelectMany(pair => Items(Item(pair.Value), "tests").Select(value => String(Item(value), "id") + "\t" + String(Item(value), "name"))).Order(StringComparer.Ordinal).ToArray();
            Need(cases.SequenceEqual(reportCases), "Finite report differs from its initial/fresh native cohort.");
            var historical = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["sourceCore"] = paths["sourceCore"],
                ["testCore"] = paths["testCore"],
                ["actualBackup"] = paths["backup"],
                ["unitAssembly"] = paths["unitAssembly"],
                ["unitDeps"] = paths["unitDeps"],
                ["unitRuntimeConfig"] = paths["unitRuntimeConfig"]
            };
            Finite.CurrentBodyObservation? current = record.CurrentCapture is null ? null : Finite.CurrentCapture(record.CurrentCapture, record.CaptureRoot, nativeRoot, historical, spec);
            if (current is not null)
            {
                XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
                string finished = (string)XDocument.Load(paths["freshTrx"]).Descendants(ns + "Times").Single().Attribute("finish")!;
                Need(Finite.Utc(finished) < Finite.Utc(String(current.Details, "observedUtc")), "Current body capture must be labelled after the existing fresh baseline.");
            }
            JsonObject restoration = Finite.Capture(paths["backup"], paths["sourceCore"], paths["testCore"], paths["backupPaths"], nativeRoot, spec,
                paths.GetValueOrDefault("roleProvenance"), current);
            bool complete = ((JsonArray)restoration["proofDeficits"]!).Count == 0;
            if (complete) { adjustments.Add(new(spec.Original.Identity, spec.OriginalReportSha256, "Timeout", "AssertionKill")); }
            observations.Add(new JsonObject
            {
                ["proofKind"] = Finite.ProofKind,
                ["originalDefinitionIdentity"] = spec.Original.Identity,
                ["originalReportSha256"] = spec.OriginalReportSha256,
                ["originalStatus"] = "Timeout",
                ["finiteReportSha256"] = spec.FiniteReportSha256,
                ["validated"] = complete,
                ["appliedKind"] = complete ? "AssertionKill" : null,
                ["directAssertionValidated"] = true,
                ["finiteCases"] = cases.Length,
                ["finiteGeneratedDefinitions"] = definitions.Length,
                ["finiteRawCounts"] = new JsonObject(definitions.GroupBy(value => value.Status).Select(group => KeyValuePair.Create<string, JsonNode?>(group.Key, JsonValue.Create(group.Count())))),
                ["nativeDetails"] = audit.Observations.DeepClone(),
                ["restoration"] = restoration,
                ["currentBodyObservation"] = current?.Details,
                ["numeratorDelta"] = complete ? 1 : 0,
                ["denominatorDelta"] = 0,
                ["rawReportRewritten"] = false,
                ["globalIntegrityIssuesWaived"] = false,
                ["scope"] = "One original definition; finite report is not a fourth full partition."
            });
        }
        return adjustments.ToArray();
    }

    private static void ValidateFiniteSources(PinnedInputs files, JsonObject document, Dictionary<string, string> paths, Source[] current, Finite.Spec spec)
    {
        var manifests = new List<JsonObject>();
        foreach (string phase in new[] { "before", "after" })
        {
            string root = files.Resolve(String(Item(document["sources"]), phase + "Root")); files.GuardRoot(root);
            JsonObject manifest = files.ReadDocument(paths[phase + "Manifest"]); manifests.Add(manifest);
            JsonObject[] entries = Items(manifest, "files").Select(Item).ToArray();
            Need(String(manifest, "inputsSha256") == spec.InputDigest && entries.Length == spec.InputCount
                && entries.Select(entry => String(entry, "path")).Distinct(StringComparer.Ordinal).Count() == entries.Length, "Finite source inventory is incomplete/duplicate.");
            foreach (JsonObject entry in entries)
            {
                string relative = String(entry, "path"); _ = Portable(relative);
                JsonObject pin = (JsonObject)entry.DeepClone(); pin["path"] = Path.Combine(root, relative); files.Pin(pin);
                if (relative.StartsWith(CorePrefix, StringComparison.Ordinal) && relative.EndsWith(".cs", StringComparison.Ordinal))
                {
                    var actual = (JsonObject)entry.DeepClone(); actual["path"] = Path.Combine(files.Repository, relative); files.Pin(actual);
                }
            }
            foreach (Source source in current)
            { Need(entries.Single(entry => String(entry, "path") == CorePrefix + source.Path)["sha256"]!.GetValue<string>() == source.Sha256, "Finite/current production source closure differs."); }
            Need(entries.Single(entry => String(entry, "path") == spec.HarnessPath)["sha256"]!.GetValue<string>() == spec.HarnessSha256, "Finite receiving harness differs.");
        }
        Need(JsonNode.DeepEquals(manifests[0], manifests[1]), "Finite source inventories changed after the native run.");
    }

    private static void VerifyFiniteCopyInventory(Dictionary<string, string> paths, string nativeRoot)
    {
        string task = HealthcheckMutationFiniteAssertionCatalog.TaskRoot; var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in File.ReadLines(paths["copyInventory"]))
        {
            Need(line.Length > 66 && line[64..66] == "  " && entries.TryAdd(line[66..], line[..64]), "Malformed/duplicate finite retained-copy inventory.");
        }
        var names = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["backup"] = "CIS.Core.dll.stryker-unchanged",
            ["backupPaths"] = "actual-backup-paths.txt",
            ["sourceCore"] = "source-CIS.Core.dll",
            ["testCore"] = "test-CIS.Core.dll",
            ["unitAssembly"] = "CIS.Core.UnitTests.dll",
            ["unitDeps"] = "CIS.Core.UnitTests.deps.json",
            ["unitRuntimeConfig"] = "CIS.Core.UnitTests.runtimeconfig.json",
            ["preFreshMarker"] = "observed-before-fresh.txt"
        };
        Need(entries.Count == names.Count && names.All(pair => entries.GetValueOrDefault(task + "/server-finite-unit-22b-originals/" + pair.Value) == Hash(paths[pair.Key])), "Finite actual retained-copy capture inventory differs.");
        Need(nativeRoot == HealthcheckMutationFiniteAssertionCatalog.NativeRoot, "Unknown finite origin.");
    }

    private static void VerifyFiniteInvocation(Dictionary<string, string> paths, string nativeRoot)
    {
        string[] lines = File.ReadAllLines(paths["invocation"]); Need(lines.Length == 5, "Finite original invocation inventory differs.");
        Need(Finite.Utc(lines[0]) < Finite.Utc(File.ReadAllText(paths["preFreshMarker"]).Trim()), "Finite invocation/body capture ordering differs.");
        string task = HealthcheckMutationFiniteAssertionCatalog.TaskRoot;
        Need(lines[1] == HealthcheckMutationFiniteAssertionCatalog.FiniteConfigSha256 + "  " + task + "/finite-unit-22/unit.json"
            && lines[2] == "41d2496272065810d8a672d2c59f480ff8e9368fae56bd89f7ea0984b78657b5  " + task + "/finite-unit-22/plan.json"
            && lines[3] == HealthcheckMutationFiniteAssertionCatalog.StrykerCliSha256 + "  " + task + "/release-next-stryker-4.16.0/Stryker.CLI.dll"
            && lines[4] == HealthcheckMutationFiniteAssertionCatalog.StrykerCoreSha256 + "  " + task + "/release-next-stryker-4.16.0/Stryker.Core.dll", "Finite configuration/tool invocation fingerprint differs.");
        Need(nativeRoot.StartsWith(task + "/finite-unit-22/source/", StringComparison.Ordinal), "Finite invocation is outside its native workspace.");
        string trace = File.ReadAllText(paths["processTrace"]);
        string first = trace.Split('\n', 2)[0];
        Need(first.Contains("execve(\"" + task + "/dotnet/dotnet\", [\"dotnet\", \"" + task + "/release-next-stryker-4.16.0/Stryker.CLI.dll\", \"--config-file\", \"" + task + "/finite-unit-22/unit.json\"", StringComparison.Ordinal)
            && trace.Contains("exit_group(0", StringComparison.Ordinal), "Finite kernel process trace does not corroborate the original native command/closure.");
    }

    private static void VerifyFiniteTestSource(JsonObject report, Finite.Spec spec)
    {
        JsonObject[] files = Item(report["testFiles"]).Where(pair => pair.Key.Replace('\\', '/').EndsWith("/" + spec.HarnessPath, StringComparison.Ordinal)).Select(pair => Item(pair.Value)).ToArray();
        Need(files is [JsonObject source] && Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(String(source, "source")))) == spec.HarnessSha256,
            "Finite embedded receiving test source differs.");
    }
}
