using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using static CIS.Verification.HealthcheckMutationUnionModel;

namespace CIS.Verification;

// A finite rerun proves one original definition. It never supplies a partition
// or replaces the complete original report's source/cohort/integrity evidence.
internal static class HealthcheckMutationFiniteAssertionValidation
{
    internal const string Schema = "cis.healthcheck.finite-assertion-rerun/v1-development";
    internal const string ProofKind = "NativeStrykerSelectedSource";
    internal sealed record Spec(Definition Original, string OriginalReportSha256, string FiniteReportSha256,
        string ManifestSha256, string InputDigest, int InputCount, string HarnessPath, string HarnessSha256,
        string TargetGuid, string TargetMethod, string SchedulingGuid, string AssertionMessage,
        string? RoleHistorySha256 = null, string[]? RoleHistoryFragments = null,
        string[]? AdditionalTargetGuids = null);
    internal sealed record CurrentBodyObservation(JsonObject Details);

    internal static void Document(JsonObject document, Spec spec)
    {
        Fields(document, "schema", "proofKind", "osLane", "original", "finite", "sources", "logsManifest", "toolchain", "baselines", "capture");
        Need(Text(document, "schema") == Schema && Text(document, "proofKind") == ProofKind && Text(document, "osLane") == "ubuntu",
            "Unsupported finite assertion schema, proof kind or OS lane.");
        JsonObject original = Object(document, "original"); Fields(original, "reportSha256", "definitionIdentity");
        Need(Text(original, "reportSha256") == spec.OriginalReportSha256 && Text(original, "definitionIdentity") == spec.Original.Identity,
            "Finite assertion belongs to another original report/definition.");
        Fields(Object(document, "finite"), "report", "configuration", "nativeSourceRoot", "nativeExit", "invocation", "processTrace");
        Fields(Object(document, "sources"), "beforeRoot", "beforeManifest", "afterRoot", "afterManifest");
        Fields(Object(document, "toolchain"), "strykerCore", "strykerCli", "objectModel");
        Fields(Object(document, "baselines"), "initialTrx", "initialExit", "freshTrx", "freshExit");
        JsonObject capture = Object(document, "capture");
        string[] required = ["backup", "backupPaths", "copyInventory", "preFreshMarker", "sourceCore", "testCore", "unitAssembly", "unitDeps", "unitRuntimeConfig"];
        Need(required.All(capture.ContainsKey) && capture.All(pair => required.Contains(pair.Key, StringComparer.Ordinal)
            || pair.Key is "roleProvenance" or "postFreshObservation"), "Missing or unsupported finite capture fields.");
    }

    internal static void Configuration(JsonObject parent, JsonObject finite, string sourcePath, int start, int end, string? filter = null)
    {
        Fields(parent, "stryker-config"); Fields(finite, "stryker-config");
        JsonObject original = Object(parent, "stryker-config"), rerun = Object(finite, "stryker-config");
        string[] delta = original.Select(pair => pair.Key).Union(rerun.Select(pair => pair.Key), StringComparer.Ordinal)
            .Where(key => !JsonNode.DeepEquals(original[key], rerun[key])).Order(StringComparer.Ordinal).ToArray();
        Need(delta.SequenceEqual(["additional-timeout", "mutate", "test-case-filter"]), "Finite configuration must change exactly range, cohort and relaxed timeout.");
        Need((int?)original["additional-timeout"] == 10000 && (int?)rerun["additional-timeout"] == 60000,
            "The reviewed finite timeout is exactly10000 to60000ms.");
        Need(Text(rerun, "mutation-level") == "Complete" && Text(rerun, "configuration") == "Release"
            && Text(rerun, "test-runner") == "vstest" && Text(rerun, "target-framework") == "net10.0"
            && (int?)rerun["concurrency"] == 2 && (bool?)rerun["disable-bail"] == true && !rerun.ContainsKey("coverage-analysis"),
            "Finite rerun must preserve Complete/Release/VSTest/defaultperTest/two-runner/no-bail settings.");
        Need(rerun["mutate"] is JsonArray { Count: 1 } ranges && (string?)ranges[0] == $"{sourcePath}{{{start}..{end}}}", "Finite UTF16 range differs.");
        Need(Text(rerun, "test-case-filter") == (filter ?? HealthcheckMutationFiniteAssertionCatalog.Filter), "Finite reviewed cohort filter differs.");
        Need(rerun["test-projects"] is JsonArray { Count: 1 } projects && (string?)projects[0] == "../../tests/CIS.Core.UnitTests/CIS.Core.UnitTests.csproj", "Finite rerun changes selected family.");
        JsonObject thresholds = Object(rerun, "thresholds");
        Need((int?)thresholds["high"] == 100 && (int?)thresholds["low"] == 90 && (int?)thresholds["break"] == 90, "Finite thresholds changed.");
    }

    internal static Definition[] Definitions(JsonObject report, string reportHash, string nativeRoot, Source[] current, Partition original, Spec spec)
    {
        Need(reportHash == spec.FiniteReportSha256 && original.Group == "unit-runtime" && original.ReportSha256 == spec.OriginalReportSha256,
            "Finite/original report binding differs.");
        Definition[] originals = original.Definitions.Where(value => value.Identity == spec.Original.Identity).ToArray();
        Need(originals is [Definition target] && target.Id == spec.Original.Id && target.Status == spec.Original.Status
            && target.KilledBy.SequenceEqual(spec.Original.KilledBy), "Exact original definition/status/killedBy tuple is absent or altered.");
        Need(current.Count(value => value.Path == spec.Original.SourcePath && value.Sha256 == spec.Original.SourceSha256) == 1, "Current target source differs.");
        // The complete parser keeps its complete-scope rule. This deliberately
        // named finite adapter selects one original source, while preserving the
        // other groups for source validation and rejecting non-placeholder text
        // or definitions in every unselected file.
        Source[] finiteSources = current.Where(value => value.Group != "unit-runtime" || value.Path == spec.Original.SourcePath).ToArray();
        Definition[] definitions = ParseReport(report, "unit-runtime", reportHash, nativeRoot, finiteSources);
        Definition[] originalFile = original.Definitions.Where(value => value.SourcePath == spec.Original.SourcePath).ToArray();
        Need(definitions.Select(value => value.Identity).ToHashSet(StringComparer.Ordinal).SetEquals(originalFile.Select(value => value.Identity)),
            "Finite generated same-file definitions differ from the original source inventory.");
        Definition[] active = definitions.Where(value => value.Status is "Killed" or "Timeout" or "Survived" or "NoCoverage" or "Pending" or "RuntimeError").ToArray();
        Need(active is [Definition result] && result.Identity == spec.Original.Identity && result.Status == "Killed"
            && result.KilledBy.ToHashSet(StringComparer.Ordinal).SetEquals(TargetGuids(spec))
            && result.KilledBy.Length == TargetGuids(spec).Length, "Finite rerun must contain only the exact assertion-killed target and reviewed GUIDs as testable definition.");
        return definitions;
    }

    internal static void NativeWitness(NativeAuditResult audit, Definition[] finite, Spec spec, string[] logPaths)
    {
        Definition target = finite.Single(value => value.Identity == spec.Original.Identity);
        Need(audit.Issues.Length == 0 && audit.AssertionKilledIds.SequenceEqual([target.Id]) && audit.ReviewedSemanticKilledIds.Length == 0
            && audit.HostCount > 0 && audit.HostCount == audit.NormalHostCount, "Finite native protocol must be wholly normal and directly assertion-backed.");
        foreach (string targetGuid in TargetGuids(spec))
        {
            JsonObject[] witnesses = ((JsonArray)audit.Observations["witnessSamples"]!).Select(value => value!.AsObject())
                .Where(value => Text(value, "mutantId") == target.Id && Text(value, "guid") == targetGuid && Text(value, "method") == spec.TargetMethod).ToArray();
            Need(witnesses is [JsonObject witness] && (int?)witness["actualOsExitCode"] == 0,
                "Exact finite failed GUID/method/normal-parent witness is missing or ambiguous.");
            string path = Text(witnesses[0], "hostPath");
            Need(logPaths.Contains(path, StringComparer.Ordinal), "Finite assertion references a host outside its pinned native logs.");
            var failures = new List<JsonObject>();
            foreach (string line in File.ReadLines(path))
            {
                const string marker = "TestRequestHandler.SendData: sending data from testhost: ";
                int offset = line.IndexOf(marker, StringComparison.Ordinal); if (offset < 0) { continue; }
                JsonObject message = JsonNode.Parse(line[(offset + marker.Length)..])!.AsObject();
                JsonArray? results = (string?)message["MessageType"] switch
                {
                    "TestExecution.StatsChange" => message["Payload"]?["NewTestResults"] as JsonArray,
                    "TestExecution.Completed" => message["Payload"]?["LastRunTests"]?["NewTestResults"] as JsonArray,
                    _ => null
                };
                if (results is null) { continue; }
                failures.AddRange(results.Select(value => value!.AsObject()).Where(value => (string?)value["TestCase"]?["Id"] == targetGuid));
            }
            Need(failures is [JsonObject failure] && (int?)failure["Outcome"] == (int?)audit.Observations["outcomeMetadata"]?["failed"], "Finite target result is missing, duplicated or not Failed.");
            JsonObject actual = failures[0];
            Need(Text(Object(actual, "TestCase"), "FullyQualifiedName") == spec.TargetMethod
                && Text(actual, "ErrorMessage").Replace("\r\n", "\n", StringComparison.Ordinal) == spec.AssertionMessage,
                "Finite assertion is not the exact reviewed Retired-versus-Reserved failure.");
            string stack = Text(actual, "ErrorStackTrace"), file = Text(Object(actual, "TestCase"), "CodeFilePath").Replace('\\', '/');
            string[] frames = stack.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(value => value.Trim()).ToArray();
            Need(frames.Length >= 2 && frames[0].StartsWith("at " + spec.TargetMethod + "(", StringComparison.Ordinal)
                && frames[0].Contains(" in " + file + ":line ", StringComparison.Ordinal)
                && file.EndsWith("/" + spec.HarnessPath, StringComparison.Ordinal), "Finite failure origin differs from the actual receiving method/source.");
            string[] forbidden = ["TimeoutException", "CanceledException", "TestCleanup", "ClassCleanup", "AssemblyCleanup", "TestInitialize", "ClassInitialize", "AssemblyInitialize", ".Dispose(", ".DisposeAsync("];
            Need(!forbidden.Any(value => stack.Contains(value, StringComparison.Ordinal) || spec.AssertionMessage.Contains(value, StringComparison.Ordinal)), "Finite failure is a deadline/cleanup/initialization failure.");
        }
    }

    internal static string[] Baselines(string initial, string fresh, string initialExit, string freshExit, string marker, Spec spec)
    {
        string[] first = HealthcheckMutationBaselineValidation.ReadPassedCases(initial, "unit"), restored = HealthcheckMutationBaselineValidation.ReadPassedCases(fresh, "unit");
        string[] expected = TargetGuids(spec).Select(guid => guid + "\t" + spec.TargetMethod)
            .Append(spec.SchedulingGuid + "\tCIS.Tests.TestAssemblySetup.SchedulingMatchesRequestedMode").Order(StringComparer.Ordinal).ToArray();
        Need(first.SequenceEqual(expected) && restored.SequenceEqual(expected), "Finite initial/fresh exact reviewed cohort differs.");
        Need(File.ReadAllText(initialExit).Trim() == "0" && File.ReadAllText(freshExit).Trim() == "0", "Finite baseline process did not pass.");
        DateTimeOffset captured = Utc(File.ReadAllText(marker).Trim());
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        string? started = (string?)XDocument.Load(fresh).Descendants(ns + "Times").Single().Attribute("start");
        Need(started is not null && captured < Utc(started), "Finite pre-fresh body observation is not before the fresh native baseline.");
        return expected;
    }

    internal static Guid CoreIdentity(string path)
    {
        using FileStream stream = File.OpenRead(path); using var pe = new PEReader(stream);
        Need(pe.HasMetadata, "Finite Core body lacks managed metadata."); MetadataReader metadata = pe.GetMetadataReader();
        Need(metadata.IsAssembly && metadata.GetString(metadata.GetAssemblyDefinition().Name) == "CIS.Core", "Finite body is not CIS.Core.");
        Guid mvid = metadata.GetGuid(metadata.GetModuleDefinition().Mvid); Need(mvid != Guid.Empty, "Finite Core body lacks MVID."); return mvid;
    }

    internal static JsonObject Capture(string backup, string sourceCore, string testCore, string backupPaths, string nativeRoot, Spec spec,
        string? roleHistory, CurrentBodyObservation? postFreshObservation)
    {
        Guid identity = CoreIdentity(backup); string hash = Hash(backup);
        Need(Hash(sourceCore) == hash && Hash(testCore) == hash && CoreIdentity(sourceCore) == identity && CoreIdentity(testCore) == identity,
            "Post-native source/test Core does not match its actual Stryker backup.");
        Need(new[] { backup, sourceCore, testCore }.Select(Path.GetFullPath).Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).Count() == 3,
            "Finite body roles require three separate retained files.");
        string expectedBackup = nativeRoot[..^"src/CIS.Core".Length] + "tests/CIS.Core.UnitTests/bin/Release/net10.0/CIS.Core.dll.stryker-unchanged";
        Need(File.ReadAllLines(backupPaths).Where(value => value.Length > 0).SequenceEqual([expectedBackup]), "Retained native backup origin differs.");
        var deficits = new JsonArray();
        if (spec.RoleHistorySha256 is null || roleHistory is null)
        { deficits.Add("The reviewed original capture-role/order history binding is not available."); }
        else
        {
            Need(Hash(roleHistory) == spec.RoleHistorySha256 && spec.RoleHistoryFragments is { Length: > 0 }, "Recovered history differs from the reviewed role provenance.");
            string text = File.ReadAllText(roleHistory); int next = 0;
            foreach (string fragment in spec.RoleHistoryFragments!)
            { int found = text.IndexOf(fragment, next, StringComparison.Ordinal); Need(found >= next, "Capture history role/order differs."); next = found + fragment.Length; }
        }
        if (postFreshObservation is null) { deficits.Add("A separately timestamped post-fresh native role observation has not been supplied."); }
        return new JsonObject
        {
            ["scope"] = "Actual post-native/pre-fresh retained restoration bodies; historical fresh loaded-module identity is not claimed.",
            ["backupSha256"] = hash,
            ["coreMvid"] = identity.ToString(),
            ["sourceAndTestCoreEqualActualBackup"] = true,
            ["historicalFreshBinaryIdentityVerified"] = false,
            ["proofDeficits"] = deficits,
            ["roleProvenanceValidated"] = spec.RoleHistorySha256 is not null && roleHistory is not null,
            ["postFreshObservationSupplied"] = postFreshObservation is not null
        };
    }

    internal static CurrentBodyObservation CurrentCapture(JsonObject capture, string localRoot, string nativeRoot,
        IReadOnlyDictionary<string, string> historical, Spec spec)
    {
        Fields(capture, "schema", "observedUtc", "completedUtc", "phase", "historicalLoadedModuleIdentityClaim", "recoveredHistoryClaim", "files");
        Need(Text(capture, "schema") == "cis.healthcheck.finite-current-native-origin-capture/v1-development"
            && (bool?)capture["historicalLoadedModuleIdentityClaim"] == false, "Unsupported finite current observation schema/history claim.");
        DateTimeOffset observed = Utc(Text(capture, "observedUtc")), completed = Utc(Text(capture, "completedUtc"));
        Need(completed >= observed, "Current capture chronology is reversed.");
        Need(nativeRoot == HealthcheckMutationFiniteAssertionCatalog.NativeRoot, "Finite capture belongs to another native workspace.");
        string language = nativeRoot[..^"src/CIS.Core".Length], unit = language + "tests/CIS.Core.UnitTests/bin/Release/net10.0/";
        string task = HealthcheckMutationFiniteAssertionCatalog.TaskRoot;
        var origins = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["sourceCore"] = nativeRoot + "/bin/Release/net10.0/CIS.Core.dll",
            ["testCore"] = unit + "CIS.Core.dll",
            ["actualBackup"] = unit + "CIS.Core.dll.stryker-unchanged",
            ["unitAssembly"] = unit + "CIS.Core.UnitTests.dll",
            ["unitDeps"] = unit + "CIS.Core.UnitTests.deps.json",
            ["unitRuntimeConfig"] = unit + "CIS.Core.UnitTests.runtimeconfig.json",
            ["strykerCore"] = task + "/release-next-stryker-4.16.0/Stryker.Core.dll",
            ["strykerCli"] = task + "/release-next-stryker-4.16.0/Stryker.CLI.dll",
            ["objectModel"] = task + "/release-next-stryker-4.16.0/Microsoft.VisualStudio.TestPlatform.ObjectModel.dll",
            ["recoveredHistory"] = task + "/finite-unit-history-27/entry989.txt",
            ["historyObservation"] = task + "/finite-unit-history-27/observed-now.txt"
        };
        var historyNames = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["sourceCore"] = "source-CIS.Core.dll",
            ["testCore"] = "test-CIS.Core.dll",
            ["actualBackup"] = "CIS.Core.dll.stryker-unchanged",
            ["unitAssembly"] = "CIS.Core.UnitTests.dll",
            ["unitDeps"] = "CIS.Core.UnitTests.deps.json",
            ["unitRuntimeConfig"] = "CIS.Core.UnitTests.runtimeconfig.json"
        };
        Need(capture["files"] is JsonArray { Count: 11 }, "All eleven actual current native role observations are required.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonObject row in ((JsonArray)capture["files"]!).Select(value => value!.AsObject()))
        {
            Fields(row, "role", "nativeOrigin", "sourceModifiedUtc", "observedUtc", "capturedCopy", "bytes", "sha256", "historicalRetainedCopy", "historicalSha256", "equalsHistoricalCopy", "mvid");
            string role = Text(row, "role"), origin = Text(row, "nativeOrigin");
            Need(seen.Add(role) && origins.TryGetValue(role, out string? expectedOrigin) && origin == expectedOrigin
                && Utc(Text(row, "observedUtc")) == observed && Utc(Text(row, "sourceModifiedUtc")) <= observed, "Current capture role/origin/time differs.");
            string name = role + "-" + origin[(origin.LastIndexOf('/') + 1)..];
            Need(Text(row, "capturedCopy") == task + "/finite-native-capture-27/" + name, "Current captured-copy origin differs.");
            string path = Path.Combine(localRoot, name);
            Need(Hash(path) == Text(row, "sha256") && new FileInfo(path).Length == (long?)row["bytes"], "Current captured body differs from actual record.");
            if (historyNames.TryGetValue(role, out string? historicalName))
            {
                Need(historical.TryGetValue(role, out string? oldPath) && Text(row, "historicalRetainedCopy") == task + "/server-finite-unit-22b-originals/" + historicalName
                    && Text(row, "historicalSha256") == Hash(oldPath) && Hash(path) == Hash(oldPath) && (bool?)row["equalsHistoricalCopy"] == true,
                    "Current actual origin differs from its historical pre-fresh capture.");
            }
            else { Need(row["historicalRetainedCopy"] is null && row["historicalSha256"] is null && row["equalsHistoricalCopy"] is null, "Tool/history observation invents historical copy equality."); }
            if (path.EndsWith(".dll", StringComparison.Ordinal) || path.EndsWith(".stryker-unchanged", StringComparison.Ordinal))
            {
                using FileStream stream = File.OpenRead(path); using var pe = new PEReader(stream);
                Need(pe.HasMetadata && pe.GetMetadataReader().GetGuid(pe.GetMetadataReader().GetModuleDefinition().Mvid).ToString() == Text(row, "mvid"), "Current managed body MVID differs.");
            }
            else { Need(row["mvid"] is null, "Non-managed capture invented a module identity."); }
        }
        Need(seen.SetEquals(origins.Keys), "Current capture role set is incomplete.");
        string recovered = Path.Combine(localRoot, "recoveredHistory-entry989.txt"), observation = Path.Combine(localRoot, "historyObservation-observed-now.txt");
        Need(Hash(recovered) == spec.RoleHistorySha256 && Utc(File.ReadAllText(observation).Trim()) <= observed, "Recovered session history identity/time differs.");
        Need(Hash(Path.Combine(localRoot, "strykerCore-Stryker.Core.dll")) == HealthcheckMutationFiniteAssertionCatalog.StrykerCoreSha256
            && Hash(Path.Combine(localRoot, "strykerCli-Stryker.CLI.dll")) == HealthcheckMutationFiniteAssertionCatalog.StrykerCliSha256
            && Hash(Path.Combine(localRoot, "objectModel-Microsoft.VisualStudio.TestPlatform.ObjectModel.dll")) == HealthcheckMutationFiniteAssertionCatalog.ObjectModelSha256,
            "Actual frozen native tool/protocol fingerprint differs.");
        return new(new JsonObject
        {
            ["observedUtc"] = observed.ToString("O"),
            ["completedUtc"] = completed.ToString("O"),
            ["actualRoleCount"] = seen.Count,
            ["sixCurrentSubjectRolesEqualHistoricalCopies"] = true,
            ["historicalLoadedModuleIdentityClaim"] = false,
            ["historyRecoveredNow"] = true,
            ["historyWasContemporaneouslyPersisted"] = false
        });
    }

    internal static DateTimeOffset Utc(string text)
    {
        Need(DateTimeOffset.TryParseExact(text, ["O", "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", "yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:sszzz"], CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out DateTimeOffset result), "Invalid finite evidence UTC observation."); return result;
    }
    internal static string Hash(string path) { using FileStream stream = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(stream)); }
    internal static string[] TargetGuids(Spec spec) => [spec.TargetGuid, .. spec.AdditionalTargetGuids ?? []];
    internal static string Text(JsonObject value, string name) => value[name] is JsonValue node && node.TryGetValue(out string? text) && text is not null ? text : throw new InvalidDataException("Missing finite assertion string: " + name);
    internal static JsonObject Object(JsonObject value, string name) => value[name] as JsonObject ?? throw new InvalidDataException("Missing finite assertion object: " + name);
    internal static void Fields(JsonObject value, params string[] names) => Need(value.Count == names.Length && value.Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal).SetEquals(names), "Unknown/missing finite assertion fields; approval flags are not evidence.");
    internal static void Need([DoesNotReturnIf(false)] bool condition, string message) { if (!condition) { throw new InvalidDataException(message); } }
}
