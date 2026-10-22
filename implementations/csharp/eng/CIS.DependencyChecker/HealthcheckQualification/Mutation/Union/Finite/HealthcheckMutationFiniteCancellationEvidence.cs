using System.Globalization;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using static CIS.Verification.HealthcheckMutationFiniteAssertionValidation;
using Catalog = CIS.Verification.HealthcheckMutationFiniteAssertionCatalog;

namespace CIS.Verification;

// This is the one contemporaneously captured run29 cohort, not a caller-defined
// assertion or process-acceptance policy. Old aborted hosts stay aborted.
internal static class HealthcheckMutationFiniteCancellationEvidence
{
    internal const string Schema = "cis.healthcheck.finite-cancellation-rerun/v1-development";
    private const string Language = Catalog.CancellationRoot + "/source/implementations/csharp";
    private const string Unit = "implementations/csharp/tests/CIS.Core.UnitTests/bin/Release/net10.0/";
    private const string Core = "implementations/csharp/src/CIS.Core/bin/Release/net10.0/";
    internal static readonly string[] BodyRoles = [Core + "CIS.Core.dll", Core + "CIS.Core.pdb", Core + "CIS.Core.deps.json",
        Unit + "CIS.Core.dll", Unit + "CIS.Core.pdb", Unit + "CIS.Core.UnitTests.dll", Unit + "CIS.Core.UnitTests.pdb",
        Unit + "CIS.Core.UnitTests.runtimeconfig.json", Unit + "CIS.Core.UnitTests.deps.json",
        Unit + "Microsoft.VisualStudio.TestPlatform.ObjectModel.dll", Unit + "CIS.Core.dll.stryker-unchanged"];

    internal static void Document(JsonObject value)
    {
        Fields(value, "schema", "proofKind", "osLane", "original", "finite", "sources", "logsManifest", "toolchain", "baselines", "capture");
        Need(ReadText(value, "schema") == Schema && ReadText(value, "proofKind") == ProofKind && ReadText(value, "osLane") == "ubuntu", "Unknown finite cancellation schema/kind/lane.");
        JsonObject original = Object(value, "original"); Fields(original, "reportSha256", "definitionIdentity");
        Need(ReadText(original, "reportSha256") == Catalog.CurrentCancellation.OriginalReportSha256
            && ReadText(original, "definitionIdentity") == Catalog.CurrentCancellation.Original.Identity, "Unknown finite cancellation original tuple.");
        Fields(Object(value, "finite"), "report", "configuration", "nativeSourceRoot", "nativeProcess", "kernelTraces");
        Need(ReadText(Object(value, "finite"), "nativeSourceRoot") == Catalog.CancellationNativeRoot, "Unknown finite cancellation native source root.");
        Fields(Object(value, "sources"), "beforeRoot", "beforeManifest", "afterRoot", "afterManifest");
        Fields(Object(value, "toolchain"), "strykerCore", "strykerCli", "objectModel");
        Fields(Object(value, "baselines"), "initialTrx", "initialProcess", "freshTrx", "freshProcess", "freshBuildProcess");
        JsonObject capture = Object(value, "capture");
        string[] required = ["receipt", "marker", "bodyRoot", "controllerSource", "controllerAssembly", "backupAbsence", "sourceAfterNative", "sourceAfterFresh"];
        Need(required.All(capture.ContainsKey) && capture.All(pair => required.Contains(pair.Key, StringComparer.Ordinal)
            || pair.Key is "postFreshObservation" or "postFreshBodyRoot") && capture.ContainsKey("postFreshObservation") == capture.ContainsKey("postFreshBodyRoot"), "Missing/unknown finite cancellation capture fields.");
    }

    internal static void Process(JsonObject process, string name)
    {
        Need(ReadText(process, "name") == name && (int?)process["actualExitCode"] == 0 && (bool?)process["timedOut"] == false,
            "Actual finite process failed, timed out or has the wrong role.");
        Need(Utc(ReadText(process, "finishedUtc")) >= Utc(ReadText(process, "startedUtc")), "Actual finite process chronology reversed.");
        JsonObject environment = Object(process, "explicitEnvironment");
        Need(ReadText(environment, "CisMutationTesting") == "true" && ReadText(environment, "UseSharedCompilation") == "false"
            && ReadText(environment, "MSBUILDDISABLENODEREUSE") == "1" && ReadText(environment, "DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER") == "1"
            && ReadText(environment, "DOTNET_ROOT") == Catalog.TaskRoot + "/dotnet", "Finite process environment differs from the serialized native cohort.");
        string[] argv = ((JsonArray)process["argv"]!).Select(value => value!.GetValue<string>()).ToArray();
        if (name == "native-process")
        {
            Need(ReadText(Object(process, "executable"), "path") == "/usr/bin/strace" && ReadText(process, "cwd") == Catalog.CancellationNativeRoot
                && (int?)process["outerBudgetSeconds"] == 1800, "Finite native executable, cwd or budget differs.");
            string[] expected = ["-ff", "-ttt", "-s", "4096", "-e", "trace=process", "-o", Catalog.CancellationRoot + "/actual-process",
                Catalog.TaskRoot + "/dotnet/dotnet", "exec", "--fx-version", "8.0.31", "--roll-forward", "Disable",
                Catalog.TaskRoot + "/release-next-stryker-4.16.0/Stryker.CLI.dll", "--config-file", Catalog.CancellationRoot + "/unit.json",
                "--output", Catalog.CancellationRoot + "/native", "--log-to-file", "--diag"];
            Need(argv.SequenceEqual(expected), "Actual finite native argv differs.");
        }
        else
        {
            Need(ReadText(Object(process, "executable"), "path") == Catalog.TaskRoot + "/dotnet/dotnet" && ReadText(process, "cwd") == Language,
                "Finite baseline executable/cwd differs.");
            string phase = name.Split('/')[0];
            if (name.EndsWith("/test", StringComparison.Ordinal))
            {
                string[] expected = ["test", "tests/CIS.Core.UnitTests/CIS.Core.UnitTests.csproj", "-c", "Release", "--no-build", "--no-restore",
                    "--filter", Catalog.CancellationFilter, "--logger", "trx;LogFileName=selected.trx", "--results-directory", Catalog.CancellationRoot + "/" + phase + "/results",
                    "--diag", Catalog.CancellationRoot + "/" + phase + "/test.vstest.log"];
                Need(argv.SequenceEqual(expected), "Actual finite baseline cohort/argv differs.");
            }
            else
            {
                Need(name == "fresh-restored-baseline/build" && argv.SequenceEqual(new[] { "build", "tests/CIS.Core.UnitTests/CIS.Core.UnitTests.csproj", "-c", "Release", "--no-incremental",
                    "-p:RestoreLockedMode=true", "-p:Platform=AnyCPU", "-p:CisMutationTesting=true", "-p:UseSharedCompilation=false", "-m:1", "-nodeReuse:false", "--nologo" }), "Actual finite fresh-build argv differs.");
            }
        }
    }

    internal static string[] Baselines(string first, string fresh, JsonObject initialProcess, JsonObject freshProcess, JsonObject freshBuild, string marker)
    {
        Process(initialProcess, "baseline/test"); Process(freshProcess, "fresh-restored-baseline/test"); Process(freshBuild, "fresh-restored-baseline/build");
        string[] expected = TargetGuids(Catalog.CurrentCancellation).Select(guid => guid + "\t" + Catalog.CurrentCancellation.TargetMethod)
            .Append(Catalog.CurrentCancellation.SchedulingGuid + "\tCIS.Tests.TestAssemblySetup.SchedulingMatchesRequestedMode").Order(StringComparer.Ordinal).ToArray();
        Need(HealthcheckMutationBaselineValidation.ReadPassedCases(first, "unit").SequenceEqual(expected)
            && HealthcheckMutationBaselineValidation.ReadPassedCases(fresh, "unit").SequenceEqual(expected), "Finite cancellation initial/fresh exact three-case cohort differs.");
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        DateTimeOffset firstStart = Utc((string)XDocument.Load(first).Descendants(ns + "Times").Single().Attribute("start")!);
        DateTimeOffset freshStart = Utc((string)XDocument.Load(fresh).Descendants(ns + "Times").Single().Attribute("start")!);
        Need(Utc(ReadText(initialProcess, "startedUtc")) <= firstStart && firstStart < Utc(ReadText(initialProcess, "finishedUtc"))
            && Utc(marker) < Utc(ReadText(freshBuild, "startedUtc")) && Utc(ReadText(freshBuild, "finishedUtc")) < Utc(ReadText(freshProcess, "startedUtc"))
            && Utc(ReadText(freshProcess, "startedUtc")) <= freshStart && freshStart < Utc(ReadText(freshProcess, "finishedUtc")), "Finite baseline/capture/process chronology differs.");
        return expected;
    }

    internal static JsonObject Capture(JsonObject receipt, string bodyRoot, string marker, JsonObject native, JsonObject freshBuild, JsonObject absence,
        JsonArray? postFresh = null, string? postFreshRoot = null, JsonObject? freshProcess = null)
    {
        Need((int?)absence["actualCount"] == 0 && ReadText(absence, "root") == Catalog.CancellationRoot + "/source"
            && Utc(ReadText(absence, "observedUtc")) < Utc(ReadText(native, "startedUtc")), "Actual native backup absence/origin/time differs.");
        Need((int?)receipt["actualBackupCount"] == 1 && (bool?)receipt["actualNativeRestoration"] == true && (bool?)receipt["noFreshBuildYet"] == true,
            "Contemporaneous native restoration is missing.");
        DateTimeOffset captured = Utc(marker), nativeEnd = Utc(ReadText(native, "finishedUtc"));
        Need(nativeEnd < Utc(ReadText(receipt, "observedUtc")) && Utc(ReadText(receipt, "observedUtc")) <= captured
            && captured < Utc(ReadText(freshBuild, "startedUtc")), "Native/body/fresh-build chronology differs.");
        JsonObject[] rows = ((JsonArray)receipt["bodies"]!).Select(value => value!.AsObject()).ToArray();
        Need(rows.Length == BodyRoles.Length && rows.Select(row => ReadText(row, "relativeRole")).ToHashSet(StringComparer.Ordinal).SetEquals(BodyRoles), "All eleven actual Core/PDB/deps/test/runtime/backup roles are required exactly once.");
        var coreMvids = new HashSet<Guid>();
        foreach (JsonObject row in rows)
        {
            string role = ReadText(row, "relativeRole"), path = Path.Combine(bodyRoot, role);
            Need(ReadText(row, "actualOrigin") == Catalog.CancellationRoot + "/source/" + role
                && ReadText(row, "path") == Catalog.CancellationRoot + "/native-before-fresh/bodies/" + role
                && Hash(path) == ReadText(row, "sha256") && new FileInfo(path).Length == (long?)row["bytes"]
                && nativeEnd < Utc(ReadText(row, "observedUtc")) && Utc(ReadText(row, "observedUtc")) <= Utc(ReadText(receipt, "observedUtc")), "Actual retained body origin/hash/length/time differs.");
            if (row.ContainsKey("mvid"))
            {
                using FileStream stream = File.OpenRead(path); using var pe = new PEReader(stream); MetadataReader metadata = pe.GetMetadataReader();
                Need(metadata.GetGuid(metadata.GetModuleDefinition().Mvid).ToString() == ReadText(row, "mvid"), "Actual retained module MVID differs.");
            }
            if (role.EndsWith("/CIS.Core.dll", StringComparison.Ordinal) || role.EndsWith("/CIS.Core.dll.stryker-unchanged", StringComparison.Ordinal))
            { Need(Hash(path) == ReadText(receipt, "originalCoreSha256"), "Actual original backup/source/test Core differs."); coreMvids.Add(CoreIdentity(path)); }
        }
        Need(coreMvids.Count == 1, "Actual original Core role MVIDs disagree.");
        foreach ((string name, string role) in new[] { ("sourceCore", Core + "CIS.Core.dll"), ("testCore", Unit + "CIS.Core.dll") })
        {
            JsonObject pin = Object(receipt, name);
            Need(ReadText(pin, "path") == Catalog.CancellationRoot + "/source/" + role && ReadText(pin, "sha256") == Hash(Path.Combine(bodyRoot, role)), "Actual live Core restoration role differs.");
        }
        JsonObject runtime = JsonNode.Parse(File.ReadAllBytes(Path.Combine(bodyRoot, Unit + "CIS.Core.UnitTests.runtimeconfig.json")))!["runtimeOptions"]!.AsObject();
        Need(ReadText(runtime, "tfm") == "net10.0" && ReadText(runtime, "rollForward") == "Disable"
            && runtime["frameworks"] is JsonArray { Count: 2 } frameworks && frameworks.All(value => (string?)value!["version"] == "10.0.12")
            && frameworks.Select(value => (string)value!["name"]!).ToHashSet(StringComparer.Ordinal).SetEquals(["Microsoft.NETCore.App", "Microsoft.AspNetCore.App"]), "Actual native subject runtime configuration differs.");
        var deficits = new JsonArray(); JsonObject? symbolObservation = null;
        if (postFresh is null || postFreshRoot is null || freshProcess is null) { deficits.Add("Actual ten-role post-fresh binary observation is missing."); }
        else { symbolObservation = PostFresh(postFresh, postFreshRoot, rows, freshProcess, bodyRoot); }
        return new JsonObject
        {
            ["proofDeficits"] = deficits,
            ["contemporaneousBodyCount"] = rows.Length,
            ["postFreshBodyCount"] = postFresh?.Count ?? 0,
            ["backupSha256"] = ReadText(receipt, "originalCoreSha256"),
            ["coreMvid"] = coreMvids.Single().ToString(),
            ["preFreshMarkerUtc"] = captured.ToString("O"),
            ["bodyCapturePrecedesFreshBuild"] = true,
            ["sourceAndTestCoreEqualActualBackup"] = true,
            ["loadedModuleIdentityClaim"] = false,
            ["oldHostNormalityClaim"] = false,
            ["symbolObservation"] = symbolObservation
        };
    }

    internal static JsonObject PostFresh(JsonArray index, string root, JsonObject[] originals, JsonObject freshProcess, string? originalRoot = null)
    {
        string[] expected = BodyRoles.Where(role => !role.EndsWith(".stryker-unchanged", StringComparison.Ordinal)).ToArray();
        JsonObject[] rows = index.Select(value => value!.AsObject()).ToArray();
        Need(rows.Length == 10 && rows.Select(row => ReadText(row, "relativeRole")).ToHashSet(StringComparer.Ordinal).SetEquals(expected), "All ten post-fresh native body roles are required exactly once.");
        bool normalizedTestSymbols = false;
        foreach (JsonObject row in rows)
        {
            string role = ReadText(row, "relativeRole"), path = Path.Combine(root, role); JsonObject original = originals.Single(value => ReadText(value, "relativeRole") == role);
            Need(ReadText(row, "actualOrigin") == Catalog.CancellationRoot + "/source/" + role && ReadText(row, "path") == Catalog.CancellationRoot + "/fresh-after/bodies/" + role
                && Utc(ReadText(row, "observedUtc")) > Utc(ReadText(freshProcess, "finishedUtc"))
                && Hash(path) == ReadText(row, "sha256") && new FileInfo(path).Length == (long?)row["bytes"], "Actual post-fresh body origin/time/hash/length differs.");
            bool unchanged = ReadText(row, "sha256") == ReadText(original, "sha256") && (long?)row["bytes"] == (long?)original["bytes"];
            if (!unchanged)
            {
                Need(!normalizedTestSymbols && AdmitsSymbolRole(role, ReadText(original, "sha256"), ReadText(row, "sha256")), "Unreviewed post-fresh native role changed.");
                normalizedTestSymbols = true;
            }
            Need(JsonNode.DeepEquals(row["mvid"], original["mvid"]), "Post-fresh role MVID differs from the original role.");
            if (row.ContainsKey("mvid"))
            {
                using FileStream stream = File.OpenRead(path); using var pe = new PEReader(stream); MetadataReader metadata = pe.GetMetadataReader();
                Need(metadata.GetGuid(metadata.GetModuleDefinition().Mvid).ToString() == ReadText(row, "mvid"), "Actual post-fresh module MVID differs.");
            }
        }
        if (!normalizedTestSymbols) { return new JsonObject { ["allTenCorrespondingPostFreshRolesEqual"] = true, ["loadedSymbolClaim"] = false }; }
        Need(originalRoot is not null, "Exact symbol normalization requires its actual before-fresh bodies.");
        return SymbolNormalization(originalRoot, root);
    }

    // Admission only: this tuple merely selects the deeper physical metadata
    // checks. It can never qualify a role or host by itself.
    internal static bool AdmitsSymbolRole(string role, string before, string after) => role == Unit + "CIS.Core.pdb"
        && before == Catalog.CancellationStaleTestPdbSha256 && after == Catalog.CancellationCleanCorePdbSha256;

    private static JsonObject SymbolNormalization(string before, string after)
    {
        void Exact(string root, string role, string hash) => Need(Hash(Path.Combine(root, role)) == hash, "Exact symbol normalization physical role differs.");
        foreach (string root in new[] { before, after })
        {
            foreach (string role in new[] { Core + "CIS.Core.dll", Unit + "CIS.Core.dll" })
            {
                Exact(root, role, "044f36503902f60657e7b89017a1ccc1c9d3c6a3483e44d5093eff55f46de275");
                Need(CoreIdentity(Path.Combine(root, role)) == new Guid("467b1a15-595b-4962-9896-cbc86ffd2537"), "Exact restored Core MVID differs.");
            }
            Exact(root, Core + "CIS.Core.pdb", Catalog.CancellationCleanCorePdbSha256);
            Exact(root, Unit + "CIS.Core.UnitTests.dll", "2dffeff40ac4a1e9cb24d98f88ccc9a1c15f246cb6329b9ca9dce44e5a3871c1");
            Exact(root, Unit + "CIS.Core.UnitTests.pdb", "2f2d737183c2d5c41e142a2111eb022a23054b4ec4239adf82b8a407a5a7fb0c");
        }
        Exact(before, Unit + "CIS.Core.pdb", Catalog.CancellationStaleTestPdbSha256);
        Exact(after, Unit + "CIS.Core.pdb", Catalog.CancellationCleanCorePdbSha256);
        var pairs = new JsonArray();
        foreach ((string phase, string root, string assembly, string symbols) in new[]
        {
            ("before-source-Core", before, Core + "CIS.Core.dll", Core + "CIS.Core.pdb"),
            ("after-source-Core", after, Core + "CIS.Core.dll", Core + "CIS.Core.pdb"),
            ("after-test-Core", after, Unit + "CIS.Core.dll", Unit + "CIS.Core.pdb"),
            ("before-Unit", before, Unit + "CIS.Core.UnitTests.dll", Unit + "CIS.Core.UnitTests.pdb"),
            ("after-Unit", after, Unit + "CIS.Core.UnitTests.dll", Unit + "CIS.Core.UnitTests.pdb")
        })
        {
            HealthcheckMutationPortablePdbValidation.Binding binding = HealthcheckMutationPortablePdbValidation.Read(Path.Combine(root, assembly), Path.Combine(root, symbols));
            Need(binding.Matches, "Required actual receiving/Core symbol pair does not match both content ID and checksum.");
            JsonObject details = binding.Details(); details["role"] = phase; details["assemblySha256"] = Hash(Path.Combine(root, assembly)); details["pdbSha256"] = Hash(Path.Combine(root, symbols)); pairs.Add(details);
        }
        HealthcheckMutationPortablePdbValidation.Binding stale = HealthcheckMutationPortablePdbValidation.Read(Path.Combine(before, Unit + "CIS.Core.dll"), Path.Combine(before, Unit + "CIS.Core.pdb"));
        Need(!stale.ContentIdMatches && !stale.ChecksumMatches, "The reviewed stale old symbol identity/checksum discrepancy is absent.");
        return new JsonObject
        {
            ["staleBeforeTestCoreSymbolsUntrusted"] = true,
            ["staleBeforeSha256"] = Catalog.CancellationStaleTestPdbSha256,
            ["cleanSourceAndAfterTestSha256"] = Catalog.CancellationCleanCorePdbSha256,
            ["unchangedOrdinaryPostFreshRoleCount"] = 9,
            ["allTenCorrespondingPostFreshRolesEqual"] = false,
            ["allElevenBodiesEqualClaim"] = false,
            ["loadedSymbolClaim"] = false,
            ["validatedActualSymbolPairs"] = pairs,
            ["preservedUntrustedBeforeTestSymbols"] = stale.Details(),
            ["scope"] = "Exact run29 direct Unit receiving assertions only; no Core-origin semantic failure or historical loaded-symbol claim."
        };
    }

    internal static string InventoryFingerprint(string root, IEnumerable<string> paths)
    {
        string text = string.Join('\n', paths.Select(path => Path.GetRelativePath(root, path).Replace('\\', '/') + "\t" + Hash(path) + "\t" + new FileInfo(path).Length.ToString(CultureInfo.InvariantCulture)).Order(StringComparer.Ordinal)) + "\n";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    internal static JsonObject Kernels(string[] tracePaths, string[] logPaths)
    {
        var traces = tracePaths.ToDictionary(path => int.Parse(Path.GetFileName(path)["actual-process.".Length..], CultureInfo.InvariantCulture));
        var edges = new List<(int Creator, int Child, bool Thread)>();
        foreach ((int creator, string path) in traces)
        {
            string? pending = null;
            foreach (string line in File.ReadLines(path))
            {
                if (Regex.IsMatch(line, @"\b(?:clone3?|fork|vfork)\(.*<unfinished \.\.\.>")) { Need(pending is null, "Overlapping kernel creation records."); pending = line; continue; }
                Match match = Regex.Match(line, @"(?:\bclone3?\(|\bfork\(|\bvfork\(|<\.\.\. (?:clone3?|fork|vfork) resumed>).*\s=\s(?<pid>\d+)\s*$");
                if (!match.Success) { if (line.Contains(" resumed>", StringComparison.Ordinal)) { pending = null; } continue; }
                string raw = pending is null ? line : pending + "\n" + line; pending = null;
                edges.Add((creator, int.Parse(match.Groups["pid"].Value, CultureInfo.InvariantCulture), raw.Contains("CLONE_THREAD", StringComparison.Ordinal)));
            }
        }
        var hosts = new JsonArray();
        foreach (string hostPath in logPaths.Where(path => Path.GetFileName(path).Contains("-log.host.", StringComparison.Ordinal)))
        {
            string[] host = File.ReadAllLines(hostPath); int pid = int.Parse(Regex.Match(host[0], @"^TpTrace \w+: 0 : (?<pid>\d+),").Groups["pid"].Value, CultureInfo.InvariantCulture);
            int[] parents = host.SelectMany(line => Regex.Matches(line, @"(?:\[--parentprocessid, (?<arg>\d+)\]|Monitoring parent process with id: '(?<monitor>\d+)')"))
                .Select(match => int.Parse(match.Groups["arg"].Success ? match.Groups["arg"].Value : match.Groups["monitor"].Value, CultureInfo.InvariantCulture)).Distinct().ToArray();
            Need(parents.Length == 1 && traces.ContainsKey(pid) && traces.ContainsKey(parents[0]), "Kernel host/declared-monitor records are missing/ambiguous.");
            int monitor = parents[0];
            string[] kernel = File.ReadAllLines(traces[pid]), monitorTrace = File.ReadAllLines(traces[monitor]);
            Need(kernel[^1].EndsWith("+++ exited with 0 +++", StringComparison.Ordinal) && kernel.Any(line => line.Contains("exit_group(0)", StringComparison.Ordinal)), "Actual testhost did not exit zero naturally.");
            Need(monitorTrace[^1].EndsWith(monitor == 961515 ? "+++ exited with 1 +++" : "+++ exited with 0 +++", StringComparison.Ordinal)
                && Epoch(monitorTrace[^1]) > Epoch(kernel[^1]), "Actual monitor-console natural exit/ordering differs.");
            string exec = kernel.Single(line => line.Contains(" execve(", StringComparison.Ordinal));
            Need(exec.Contains("\"--parentprocessid\", \"" + monitor + "\"", StringComparison.Ordinal) && exec.Contains("testhost.dll", StringComparison.Ordinal) && exec.EndsWith(" = 0", StringComparison.Ordinal), "Kernel exec/declared parent differs.");
            var incoming = edges.Where(edge => edge.Child == pid && !edge.Thread).ToArray(); Need(incoming.Length == 1, "Kernel process creator is missing/ambiguous.");
            int creator = incoming[0].Creator; var visited = new HashSet<int>();
            while (edges.Any(edge => edge.Child == creator && edge.Thread))
            { Need(visited.Add(creator), "Cyclic kernel thread ancestry."); var parent = edges.Where(edge => edge.Child == creator && edge.Thread).ToArray(); Need(parent.Length == 1, "Ambiguous kernel thread ancestry."); creator = parent[0].Creator; }
            string controller = Path.Combine(Path.GetDirectoryName(hostPath)!, Path.GetFileName(hostPath).Split(".host.", StringSplitOptions.None)[0] + ".txt");
            Need(logPaths.Contains(controller, StringComparer.Ordinal), "Unpinned actual host controller.");
            if (creator != monitor)
            {
                Need(creator == 961017 && monitor == 961515 && traces.ContainsKey(creator), "Unknown custom launcher/monitor pair.");
                string[] client = File.ReadAllLines(traces[creator]);
                Need(client[0].Contains("\"--fx-version\", \"8.0.31\"", StringComparison.Ordinal)
                    && client[0].Contains(Catalog.TaskRoot + "/release-next-stryker-4.16.0/Stryker.CLI.dll", StringComparison.Ordinal)
                    && client[^1].EndsWith("+++ exited with 0 +++", StringComparison.Ordinal), "Actual custom-launch Stryker origin/exit differs.");
                int callbacks = File.ReadLines(controller).Count(line =>
                {
                    const string marker = "(TestExecution.CustomTestHostLaunchCallback) -> "; int index = line.IndexOf(marker, StringComparison.Ordinal); if (index < 0) { return false; }
                    JsonObject payload = JsonNode.Parse(line[(index + marker.Length)..])!["Payload"]!.AsObject(); return (int?)payload["HostProcessId"] == pid && payload["ErrorMessage"] is null;
                });
                Need(callbacks == 1, "Actual custom host launch callback is absent/ambiguous.");
            }
            Need(File.ReadLines(controller).Count(line => line.Contains("Testhost processId: " + pid + " exited with exitcode: 0 error: ''", StringComparison.Ordinal)) == 1, "Actual parent host-exit callback differs.");
            Need(host.Any(line => line.Contains("Runtime location: " + Catalog.TaskRoot + "/dotnet/shared/Microsoft.NETCore.App/10.0.12", StringComparison.Ordinal)), "Actual subject runtime location differs.");
            hosts.Add(new JsonObject
            {
                ["hostPid"] = pid,
                ["declaredMonitorPid"] = monitor,
                ["kernelCreatorLeaderPid"] = creator,
                ["hostOsExit"] = 0,
                ["monitorConsoleExit"] = monitor == 961515 ? 1 : 0,
                ["customLauncherSeparateFromMonitor"] = creator != monitor
            });
        }
        Need(hosts.Count == 4 && hosts.Select(value => (int)value!["hostPid"]!).ToHashSet().SetEquals([961562, 961585, 961773, 961797]), "Exact four actual finite discovery/execution kernel host chains are required.");
        return new JsonObject { ["actualKernelHostChains"] = hosts, ["scope"] = "Native testhost OS exits and distinct launcher/monitor roles; no old-host or loaded-CoreLib claim." };
    }

    private static string ReadText(JsonObject value, string name) => HealthcheckMutationFiniteAssertionValidation.Text(value, name);
    private static double Epoch(string line) => double.Parse(line.Split(' ')[0], CultureInfo.InvariantCulture);
}
