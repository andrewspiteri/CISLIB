using System.Text.Json.Nodes;
using static CIS.Verification.HealthcheckMutationUnionModel;
using Cancellation = CIS.Verification.HealthcheckMutationFiniteCancellationEvidence;
using Catalog = CIS.Verification.HealthcheckMutationFiniteAssertionCatalog;
using Finite = CIS.Verification.HealthcheckMutationFiniteAssertionValidation;

namespace CIS.Verification;

internal static partial class HealthcheckMutationUnionVerification
{
    private static FiniteAssertionRecord ReadFiniteCancellation(PinnedInputs files, JsonObject document, Source[] sources, string lane)
    {
        Cancellation.Document(document); Need(lane == "ubuntu", "Finite cancellation evidence is Ubuntu-only.");
        var paths = new Dictionary<string, string>(StringComparer.Ordinal);
        void Pin(string section, string field, string? expected = null)
        {
            var reference = HealthcheckMutationDispositionValidation.ReadReference(Item(Item(document[section])[field]));
            Need(expected is null || reference.Sha256 == expected, "Finite cancellation fingerprint differs: " + field);
            string path = files.Pin(HealthcheckMutationDispositionValidation.Reference(reference));
            files.GuardRoot(Path.GetDirectoryName(path)!); paths.Add(field, path);
        }
        Pin("finite", "report", Catalog.CurrentCancellation.FiniteReportSha256);
        Pin("finite", "configuration", Catalog.CancellationConfigSha256);
        Pin("finite", "nativeProcess", Catalog.CancellationProcessSha256);
        Pin("sources", "beforeManifest", Catalog.CurrentCancellation.ManifestSha256);
        Pin("sources", "afterManifest", Catalog.CurrentCancellation.ManifestSha256);
        foreach (string field in new[] { "initialTrx", "initialProcess", "freshTrx", "freshProcess", "freshBuildProcess" }) { Pin("baselines", field, Catalog.CancellationPhaseSha256(field)); }
        Pin("toolchain", "strykerCore", Catalog.StrykerCoreSha256); Pin("toolchain", "strykerCli", Catalog.StrykerCliSha256);
        Pin("toolchain", "objectModel", Catalog.CancellationObjectModelSha256);
        Pin("capture", "receipt", Catalog.CancellationCaptureSha256); Pin("capture", "marker", Catalog.CancellationPhaseSha256("marker"));
        Pin("capture", "controllerSource", Catalog.CancellationControllerSha256);
        Pin("capture", "controllerAssembly", Catalog.CancellationControllerAssemblySha256);
        foreach (string field in new[] { "backupAbsence", "sourceAfterNative", "sourceAfterFresh" }) { Pin("capture", field, Catalog.CancellationPhaseSha256(field)); }
        string bodyRoot = files.Resolve(String(Item(document["capture"]), "bodyRoot")); files.TrackInventory(bodyRoot);
        JsonObject receipt = files.ReadDocument(paths["receipt"]);
        foreach (JsonObject row in Items(receipt, "bodies").Select(Item))
        {
            string relative = String(row, "relativeRole");
            files.Pin(new JsonObject { ["path"] = Path.Combine(bodyRoot, Portable(relative)), ["sha256"] = String(row, "sha256"), ["bytes"] = Integer(row, "bytes") });
        }
        string[] bodies = Directory.EnumerateFiles(bodyRoot, "*", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(bodyRoot, path).Replace('\\', '/')).Order(StringComparer.Ordinal).ToArray();
        Need(bodies.SequenceEqual(Cancellation.BodyRoles.Order(StringComparer.Ordinal)), "Actual finite body-role inventory is incomplete/foreign.");
        string[] kernelPaths = Items(Item(document["finite"]), "kernelTraces").Select(value =>
        {
            var reference = HealthcheckMutationDispositionValidation.ReadReference(Item(value));
            string path = files.Pin(HealthcheckMutationDispositionValidation.Reference(reference));
            string name = Path.GetFileName(path);
            Need(name.StartsWith("actual-process.", StringComparison.Ordinal) && name["actual-process.".Length..].All(char.IsAsciiDigit), "Unknown finite kernel trace name.");
            return path;
        }).ToArray();
        Need(kernelPaths.Length > 0 && kernelPaths.Distinct(StringComparer.Ordinal).Count() == kernelPaths.Length
            && kernelPaths.Select(Path.GetDirectoryName).Distinct(StringComparer.Ordinal).Count() == 1, "Empty/duplicate/split finite kernel inventory.");
        string kernelRoot = Path.GetDirectoryName(kernelPaths[0])!; files.TrackInventory(kernelRoot);
        Need(Directory.EnumerateFiles(kernelRoot, "actual-process.*").Order(StringComparer.Ordinal).SequenceEqual(kernelPaths.Order(StringComparer.Ordinal)), "Finite kernel inventory omits actual process records.");
        // The full log inventory remains separate from the kernel inventory.
        // Neither a selected host list nor an invented parent status is allowed.
        string[] logs = ReadLogs(files, new JsonObject { ["logsManifest"] = document["logsManifest"]!.DeepClone() });
        Need(Cancellation.InventoryFingerprint(kernelRoot, kernelPaths) == Catalog.CancellationKernelInventorySha256
            && Cancellation.InventoryFingerprint(Path.GetDirectoryName(logs[0])!, logs) == Catalog.CancellationLogInventorySha256, "Finite actual kernel/log byte inventories differ.");
        paths.Add("kernelRoot", kernelRoot);
        JsonObject? postFresh = null;
        if (Item(document["capture"]).ContainsKey("postFreshObservation"))
        {
            Pin("capture", "postFreshObservation", Catalog.CancellationPostFreshSha256);
            string postRoot = files.Resolve(String(Item(document["capture"]), "postFreshBodyRoot")); files.TrackInventory(postRoot); paths.Add("postFreshBodyRoot", postRoot);
            JsonArray index = JsonNode.Parse(File.ReadAllBytes(paths["postFreshObservation"]))!.AsArray();
            foreach (JsonObject row in index.Select(value => value!.AsObject()))
            { files.Pin(new JsonObject { ["path"] = Path.Combine(postRoot, Portable(String(row, "relativeRole"))), ["sha256"] = String(row, "sha256"), ["bytes"] = Integer(row, "bytes") }); }
            Need(Directory.EnumerateFiles(postRoot, "*", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(postRoot, path).Replace('\\', '/')).Order(StringComparer.Ordinal)
                .SequenceEqual(Cancellation.BodyRoles.Where(role => !role.EndsWith(".stryker-unchanged", StringComparison.Ordinal)).Order(StringComparer.Ordinal)), "Finite actual post-fresh body inventory differs.");
            postFresh = new JsonObject { ["files"] = index };
        }
        string parent = MaintainedConfig(files, "unit-runtime"); Need(Hash(parent) == Catalog.ParentConfigSha256, "Finite full parent configuration changed.");
        Finite.Configuration(files.ReadDocument(parent), files.ReadDocument(paths["configuration"]), Catalog.CurrentCancellation.Original.SourcePath,
            Catalog.CurrentCancellation.Original.Start, Catalog.CurrentCancellation.Original.End, Catalog.CancellationFilter);
        ValidateFiniteSources(files, document, paths, sources, Catalog.CurrentCancellation);
        // Bind every source/build/generator input, including the 146 Core files,
        // rather than accepting only the selected Healthcheck source text.
        JsonObject manifest = files.ReadDocument(paths["beforeManifest"]);
        JsonObject[] inputs = Items(manifest, "files").Select(Item).ToArray();
        Need(inputs.Count(input => String(input, "path").StartsWith(CorePrefix, StringComparison.Ordinal) && String(input, "path").EndsWith(".cs", StringComparison.Ordinal)) == 146,
            "Finite cancellation shipping Core inventory differs.");
        foreach (JsonObject input in inputs.Where(input => ShippingInput(String(input, "path"))))
        { var current = (JsonObject)input.DeepClone(); current["path"] = Path.Combine(files.Repository, Portable(String(input, "path"))); files.Pin(current); }
        return new(document, sources, paths, logs, bodyRoot, postFresh, true);
    }

    private static Adjustment? CompleteFiniteCancellation(FiniteAssertionRecord record, Partition[] originals, JsonArray observations)
    {
        Finite.Spec spec = Catalog.CurrentCancellation; Dictionary<string, string> paths = record.Paths;
        JsonObject Read(string name) => JsonNode.Parse(File.ReadAllBytes(paths[name]))!.AsObject();
        JsonObject report = Read("report"), config = Read("configuration"), native = Read("nativeProcess");
        Definition[] definitions = Finite.Definitions(report, Hash(paths["report"]), Catalog.CancellationNativeRoot, record.Sources,
            originals.Single(value => value.Group == "unit-runtime"), spec);
        Need(definitions.Length == 330 && definitions.Count(value => value.Status == "Killed") == 1
            && definitions.Count(value => value.Status == "CompileError") == 23 && definitions.Count(value => value.Status == "Ignored") == 306,
            "Finite cancellation generated/filtered/compiled source inventory differs.");
        VerifyFiniteTestSource(report, spec);
        NativeAuditResult audit = HealthcheckMutationNativeAudit.Audit(report, config, record.Logs, paths["objectModel"]);
        Need(audit.HostCount == 3 && audit.NormalHostCount == 3 && (int?)audit.Observations["discoveryHostCount"] == 1
            && (int?)audit.Observations["normalDiscoveryHostCount"] == 1 && (int?)audit.Observations["discoveredGuidCount"] == 3,
            "Exact initial/coverage/mutant/discovery normal native host cohort differs.");
        Finite.NativeWitness(audit, definitions, spec, record.Logs);
        Cancellation.Process(native, "native-process");
        string marker = File.ReadAllText(paths["marker"]).Trim(); JsonObject initial = Read("initialProcess"), fresh = Read("freshProcess"), build = Read("freshBuildProcess");
        string[] cases = Cancellation.Baselines(paths["initialTrx"], paths["freshTrx"], initial, fresh, build, marker);
        string[] reportCases = Item(report["testFiles"]).SelectMany(pair => Items(Item(pair.Value), "tests").Select(value => String(Item(value), "id") + "\t" + String(Item(value), "name"))).Order(StringComparer.Ordinal).ToArray();
        Need(cases.SequenceEqual(reportCases), "Finite cancellation native report differs from both actual three-case baselines.");
        Need(Finite.Utc(String(initial, "finishedUtc")) < Finite.Utc(String(native, "startedUtc")), "Finite initial baseline did not precede native execution.");
        JsonObject restoration = Cancellation.Capture(Read("receipt"), record.CaptureRoot, marker, native, build, Read("backupAbsence"),
            record.CurrentCapture?["files"] as JsonArray, paths.GetValueOrDefault("postFreshBodyRoot"), fresh);
        string[] traces = Directory.EnumerateFiles(paths["kernelRoot"], "actual-process.*").ToArray();
        JsonObject kernels = Cancellation.Kernels(traces, record.Logs);
        foreach (string name in new[] { "sourceAfterNative", "sourceAfterFresh" })
        {
            JsonObject check = Read(name);
            Need(String(check, "root") == Catalog.CancellationRoot + "/source" && Integer(check, "verifiedInputs") == spec.InputCount
                && (bool?)check["allHashAndLengthMatched"] == true, "Contemporaneous actual source check differs.");
            DateTimeOffset observed = Finite.Utc(String(check, "observedUtc"));
            Need(name == "sourceAfterNative" ? observed > Finite.Utc(marker) && observed < Finite.Utc(String(build, "startedUtc"))
                : observed > Finite.Utc(String(fresh, "finishedUtc")), "Actual source check phase/order differs.");
        }
        bool complete = ((JsonArray)restoration["proofDeficits"]!).Count == 0;
        observations.Add(new JsonObject
        {
            ["proofKind"] = Finite.ProofKind,
            ["originalDefinitionIdentity"] = spec.Original.Identity,
            ["originalReportSha256"] = spec.OriginalReportSha256,
            ["originalStatus"] = spec.Original.Status,
            ["finiteReportSha256"] = spec.FiniteReportSha256,
            ["validated"] = complete,
            ["appliedKind"] = complete ? "AssertionKill" : null,
            ["directAssertionValidated"] = true,
            ["directAssertionGuidCount"] = Finite.TargetGuids(spec).Length,
            ["finiteCases"] = cases.Length,
            ["finiteGeneratedDefinitions"] = definitions.Length,
            ["nativeDetails"] = audit.Observations.DeepClone(),
            ["restoration"] = restoration,
            ["kernelRoles"] = kernels,
            ["numeratorDelta"] = complete ? 1 : 0,
            ["denominatorDelta"] = 0,
            ["rawReportRewritten"] = false,
            ["globalIntegrityIssuesWaived"] = false,
            ["oldHostNormalityClaim"] = false,
            ["scope"] = "One original rawKilled definition; new finite normal hosts do not make its old aborted host normal."
        });
        return complete ? new(spec.Original.Identity, spec.OriginalReportSha256, spec.Original.Status, "AssertionKill") : null;
    }
}
