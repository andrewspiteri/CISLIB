using System.Globalization;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using static CIS.Verification.HealthcheckMutationUnionModel;

namespace CIS.Verification;

internal static partial class HealthcheckMutationUnionVerification
{
    private static async Task<(Partition Partition, JsonObject Observation)> ReadPartitionAsync(
        PinnedInputs files, JsonObject descriptor, Source[] sources, string lane, string output)
    {
        string group = String(descriptor, "group"), nativeRoot = String(descriptor, "nativeSourceRoot");
        Need(lane == "ubuntu" ? nativeRoot.StartsWith('/') : nativeRoot.Length > 2 && char.IsAsciiLetter(nativeRoot[0]) && nativeRoot[1] == ':',
            "Native source root does not belong to the selected OS lane.");
        string reportPath = files.Pin(Item(descriptor["report"]));
        string reportHash = String(Item(descriptor["report"]), "sha256");
        JsonObject report = files.ReadDocument(reportPath);
        JsonObject config = ValidateConfiguration(files, descriptor);
        string core = files.Pin(Item(descriptor["strykerCore"]));
        Need(Hash(core) == "bf02f78d3725e1243edb7a32d88dc1a02c88f1f9f74b60b18730636073f234d7",
            "The selected native Stryker implementation must be the pinned 4.16.0 tool.");
        string exitPath = files.Pin(Item(descriptor["nativeExit"]));
        Need(int.TryParse(File.ReadAllText(exitPath).Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int exit)
            && exit is 0 or 2, "Native mutation process did not close with a report/threshold outcome.");
        string objectModel = files.Pin(Item(descriptor["objectModel"]));
        string[] logs = ReadLogs(files, descriptor);
        Definition[] definitions = ParseReport(report, group, reportHash, nativeRoot, sources);
        HealthcheckMutationNativeKernelEvidence? kernel = ReadKernelInputs(files, descriptor, lane);
        NativeAuditResult audit = HealthcheckMutationNativeAudit.Audit(report, config, logs, objectModel, reportHash, definitions, sources, kernel);
        var issues = audit.Issues.ToList();
        VerifyRestoration(files, descriptor, sources, group, report);
        if (group is "pipes" or "aspnet")
        {
            // Re-run the maintained auditor against actual immutable receipts and
            // stage bodies. A captured `qualified:true` field is not an audit.
            string session = files.Pin(Item(descriptor["childSession"]));
            string childOutput = Path.Combine(output, group + "-child-audit");
            int childExit = await HealthcheckChildMutationVerification.RunAsync(["audit", "--session", session,
                "--output", childOutput, "--require-kind", group == "pipes" ? "probe" : "http-host"]);
            if (childExit != 0)
            { issues.Add(new IntegrityIssue("child-propagation", "The maintained audit rejected actual child origin/selector/runtime/lifecycle evidence; inspect retained child results.", [])); }
        }
        var observation = new JsonObject
        {
            ["group"] = group,
            ["nativeExit"] = exit,
            ["hostCount"] = audit.HostCount,
            ["normalHostCount"] = audit.NormalHostCount,
            ["completedBeforeAbortKilledIds"] = new JsonArray(audit.CompletedBeforeAbortKilledIds.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray()),
            ["details"] = audit.Observations.DeepClone()
        };
        // QualifiedKilledIds explicitly deduplicates normal direct, reviewed semantic and
        // reviewed completed-before-abort categories. Every native/child issue remains.
        return (new Partition(group, reportHash, sources.Where(s => s.Group == group).ToArray(), definitions,
            audit.QualifiedKilledIds, issues.ToArray()), observation);
    }

    private static void VerifyRestoration(PinnedInputs files, JsonObject descriptor, Source[] sources, string group, JsonObject report)
    {
        JsonObject restoration = files.ReadDocument(files.Pin(Item(descriptor["restorationManifest"])));
        Need(String(restoration, "schema") == "cis.healthcheck.mutation-restoration/v1-development", "Unknown restoration evidence schema.");
        string backup = files.Pin(Item(restoration["actualBackup"]));
        Need(Path.GetFileName(backup).EndsWith(".stryker-unchanged", StringComparison.Ordinal), "Actual retained Stryker original backup is required.");
        string backupHash = Hash(backup);
        CoreMvid(backup);
        string inventoryPath = files.Pin(Item(restoration["nativeCaptureInventory"]));
        var inventory = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in File.ReadLines(inventoryPath))
        {
            Need(line.Length > 66 && line[64..66] == "  " && line[..64].All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f'), "Malformed original native capture inventory.");
            Need(inventory.TryAdd(line[66..].Replace('\\', '/'), line[..64]), "Duplicate original native capture origin.");
        }
        string backupOrigin = String(restoration, "actualBackupOrigin").Replace('\\', '/');
        Need(backupOrigin.EndsWith("/CIS.Core.dll.stryker-unchanged", StringComparison.Ordinal)
            && inventory.GetValueOrDefault(backupOrigin) == backupHash, "Actual retained backup is absent from the original native capture inventory.");
        string marker = files.Pin(Item(restoration["preFreshObservation"]));
        Need(inventory.Values.Contains(Hash(marker), StringComparer.Ordinal), "The original pre-fresh observation is absent from the native capture inventory.");
        string markerText = File.ReadAllText(marker).Trim();
        // Accept the original native UTC marker, or the original richer JSON
        // observation. Do not replace it with a newly asserted approval flag.
        string observedText = markerText.StartsWith('{') ? String(files.ReadDocument(marker), "observedBeforeFreshUtc") : markerText;
        Need(HealthcheckMutationUtcObservation.TryRead(observedText, out HealthcheckMutationUtcObservation.Observation observed),
            "Original pre-fresh capture observation is missing.");
        JsonObject[] pairs = Items(restoration, "binaries").Select(Item).ToArray();
        string[] required = group switch { "unit-runtime" => ["unit"], "pipes" => ["unit", "integration"], _ => ["integration"] };
        Need(pairs.Length >= 2 && pairs.Select(p => String(p, "role")).Distinct(StringComparer.Ordinal).Count() == pairs.Length,
            "Original and restored source/test Core bodies are required.");
        Need(pairs.Any(p => String(p, "role") == "source-Core") && required.All(family => pairs.Any(p => String(p, "role") == family + "-Core")), "Missing original/restored Core roles.");
        var originalPaths = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var restoredPaths = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        string nativeRoot = String(descriptor, "nativeSourceRoot").Replace('\\', '/').TrimEnd('/');
        Need(nativeRoot.EndsWith("/src/CIS.Core", StringComparison.Ordinal), "Unknown native Core project origin.");
        string languageRoot = nativeRoot[..^"src/CIS.Core".Length];
        foreach (JsonObject pair in pairs)
        {
            JsonObject original = Item(pair["original"]), restored = Item(pair["restored"]);
            string originalPath = files.Pin(original), restoredPath = files.Pin(restored);
            Need(originalPaths.Add(originalPath) && restoredPaths.Add(restoredPath), "One physical file cannot stand for multiple actual body roles.");
            Need(String(original, "sha256") == String(restored, "sha256"), "An actual original body was not restored.");
            if (String(pair, "role").EndsWith("-Core", StringComparison.Ordinal))
            {
                Need(String(original, "sha256") == backupHash && CoreMvid(originalPath) == CoreMvid(restoredPath), "Source/test Core differs from its actual retained Stryker backup.");
                string origin = String(pair, "role") switch
                {
                    "source-Core" => nativeRoot + "/bin/Release/net10.0/CIS.Core.dll",
                    "unit-Core" => languageRoot + "tests/CIS.Core.UnitTests/bin/Release/net10.0/CIS.Core.dll",
                    "integration-Core" => languageRoot + "tests/CIS.Core.IntegrationTests/bin/Release/net10.0/CIS.Core.dll",
                    _ => throw new InvalidDataException("Unknown native Core body role.")
                };
                Need(inventory.GetValueOrDefault(origin) == backupHash, "Native source/test Core origin is absent from the actual capture inventory.");
            }
        }
        JsonObject[] restoredSources = Items(restoration, "sources").Select(Item).ToArray();
        Source[] selected = sources.Where(s => s.Group == group).ToArray();
        Need(restoredSources.Length == selected.Length && restoredSources.Select(p => String(p, "sourcePath"))
            .ToHashSet(StringComparer.Ordinal).SetEquals(selected.Select(s => s.Path)), "Restored shipping source scope differs.");
        foreach (JsonObject restored in restoredSources)
        {
            JsonObject pin = Item(restored["file"]); files.Pin(pin);
            Need(selected.Single(s => s.Path == String(restored, "sourcePath")).Sha256 == String(pin, "sha256"), "Restored physical source differs.");
        }
        JsonObject[] baselines = Items(restoration, "baselines").Select(Item).ToArray();
        Need(baselines.Length == required.Length && baselines.Select(b => String(b, "family")).ToHashSet(StringComparer.Ordinal).SetEquals(required), "Missing native initial/fresh baseline family.");
        var cohort = new List<string>();
        foreach (JsonObject baseline in baselines)
        {
            string initialPath = files.Pin(Item(baseline["initial"])), freshPath = files.Pin(Item(baseline["fresh"]));
            string family = String(baseline, "family");
            string[] initial = HealthcheckMutationBaselineValidation.ReadPassedCases(initialPath, family),
                fresh = HealthcheckMutationBaselineValidation.ReadPassedCases(freshPath, family);
            foreach (string phase in new[] { "initialExit", "freshExit" })
            { Need(File.ReadAllText(files.Pin(Item(baseline[phase]))).Trim() == "0", "Original/fresh native baseline process did not exit 0."); }
            Need(initial.SequenceEqual(fresh), "Initial and restored fresh native test identities differ.");
            XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
            string? started = (string?)XDocument.Load(freshPath).Descendants(ns + "Times").Single().Attribute("start");
            Need(DateTimeOffset.TryParse(started, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset freshStarted)
                && observed.IsBefore(freshStarted), "Actual original-body observation was not before the fresh native run.");
            cohort.AddRange(initial);
        }
        string[] declared = Item(report["testFiles"]).SelectMany(file => Items(Item(file.Value), "tests").Select(node =>
        {
            JsonObject test = Item(node);
            Need(Guid.TryParse(String(test, "id"), out Guid id), "Invalid report test GUID.");
            return id.ToString() + "\t" + String(test, "name");
        })).Order(StringComparer.Ordinal).ToArray();
        Need(declared.Length > 0 && declared.Distinct(StringComparer.Ordinal).Count() == declared.Length
            && cohort.Order(StringComparer.Ordinal).SequenceEqual(declared), "Initial/fresh selected family GUID/method cohort differs from the original native report.");
    }

    private static Guid CoreMvid(string path)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            Need(pe.HasMetadata, "A Core body is not an actual managed assembly.");
            MetadataReader metadata = pe.GetMetadataReader();
            Need(metadata.IsAssembly && metadata.GetString(metadata.GetAssemblyDefinition().Name) == "CIS.Core", "A body is not the actual CIS.Core assembly.");
            Guid mvid = metadata.GetGuid(metadata.GetModuleDefinition().Mvid);
            Need(mvid != Guid.Empty, "Core body has no module identity.");
            return mvid;
        }
        catch (BadImageFormatException error) { throw new InvalidDataException("A Core body is not a valid managed assembly.", error); }
    }
}
