using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CIS.Verification.HealthcheckMutationChildren;
using static CIS.Verification.HealthcheckMutationUnionModel;

namespace CIS.Verification;

// The shared verifier establishes native outcomes. This adapter receives the
// separate actual child propagation witnesses without inventing test GUIDs,
// assertion wording rules, native statuses or per-mutant replay requirements.
internal static class HealthcheckSharedMutationChildren
{
    private const string Core = "implementations/csharp/src/CIS.Core/";
    private const string Unit = "implementations/csharp/tests/CIS.Core.UnitTests/";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static StringComparison Comparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    internal sealed record Reader(Func<JsonObject, string> Pin, Func<string, JsonObject> Document,
        Action<string> Guard, Action<string> TrackInventory);
    internal sealed record Result(string Group, string ReportSha256, bool Verified, bool UnitOnly,
        string[] ActiveKinds, IntegrityIssue[] Issues, JsonObject Observation);

    internal static async Task<Result> ReadAsync(Reader files, string snapshot, JsonObject descriptor,
        JsonObject profile, JsonObject batch, Source source, Definition[] definitions, string report, string output)
    {
        string reportHash = Hash(report);
        try
        {
            var pinnedTrees = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            Need(source.Group is "pipes" or "aspnet", "Child receiving applies only to a child-process partition.");
            if (descriptor["childEvidence"] is null) { return Held("MissingSharedChildEvidence", "No bound child attempt was supplied."); }
            Need(descriptor["childEvidence"] is JsonArray pins && pins.Count is > 0 and <= 128, "Child evidence requires 1 through128 explicit binding pins.");
            string id = Text(profile, "id");
            Need(Regex.IsMatch(id, @"\A[A-Za-z0-9][A-Za-z0-9._-]{0,63}\z", RegexOptions.CultureInvariant), "Unsafe child profile ID.");
            string childRoot = Under(snapshot, ".cis/local/testing/incremental/" + id + "/child-evidence");
            var candidates = new List<(string Path, JsonObject Binding)>();
            var paths = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (JsonNode? node in descriptor["childEvidence"]!.AsArray())
            {
                JsonObject pin = Item(node); Fields(pin, "path", "sha256", "bytes");
                Need(Number(pin, "bytes") is > 0 and <= 262144, "A child binding exceeds256KiB.");
                string path = files.Pin(pin);
                Need(Inside(childRoot, path) && Path.GetFileName(path) == "binding.json" && paths.Add(path), "Child binding is foreign, duplicate or outside its profile root.");
                JsonObject binding = files.Document(path);
                BindingFields(binding);
                Need(Text(binding, "profileId") == id && Text(binding, "phase") == "mutation", "Child binding has a different profile/phase.");
                JsonObject native = Item(binding["native"]);
                if (Text(native, "reportSha256") == reportHash && Number(native, "reportBytes") == new FileInfo(report).Length
                    && Same(Text(Item(binding["source"]), "path"), Under(snapshot, Core + source.Path))
                    && Text(Item(binding["source"]), "sha256") == source.Sha256)
                { candidates.Add((path, binding)); }
            }
            Need(candidates.Count == 1, "Exactly one child attempt must bind this original native report.");
            var selected = candidates[0]; JsonObject value = selected.Binding;
            string attemptId = Text(value, "attemptId");
            Need(Regex.IsMatch(attemptId, @"\A[a-f0-9]{12}\z", RegexOptions.CultureInvariant), "Invalid CIS child attempt identity.");
            string attempt = Under(childRoot, attemptId);
            Need(Same(selected.Path, Under(attempt, "binding.json")), "Binding does not belong to its exact CIS attempt.");
            string language = Under(snapshot, "implementations/csharp"), working = Under(snapshot, Core.TrimEnd('/'));
            string nativeOutput = Under(snapshot, ".cis/local/testing/incremental/" + id + "/work/" + attemptId);
            Need(Same(Text(value, "languageRoot"), language) && Same(Text(value, "workingDirectory"), working)
                && Same(Text(value, "outputRoot"), nativeOutput), "Child attempt roots differ from the original native snapshot.");
            string configuration = files.Pin(Item(value["config"])), sessionPath = files.Pin(Item(value["session"]));
            string sourcePath = files.Pin(Item(value["source"]));
            Need(Same(configuration, Under(attempt, "native-config.json")) && Same(sessionPath, Under(attempt, "session/session.json"))
                && Same(sourcePath, Under(snapshot, Core + source.Path)) && Hash(sourcePath) == source.Sha256, "Child config/session/source identity differs.");
            JsonObject tools = Item(value["tools"]);
            string[] toolNames = ["dotnet", "strykerCli", "strykerCore", "wrapper", "helper", "wrapperDeps", "wrapperRuntimeConfig"];
            Fields(tools, toolNames);
            var toolPaths = toolNames.ToDictionary(name => name, name => files.Pin(Item(tools[name])), StringComparer.Ordinal);
            string[] declaredTools = Strings(profile, "toolFiles").Select(path => Full(snapshot, path)).ToArray();
            Need(toolPaths.Values.All(path => declaredTools.Any(tool => Same(tool, path))), "A child producer/tool is absent from the original profile tool inputs.");
            Need(Path.GetFileName(toolPaths["strykerCore"]) == "Stryker.Core.dll"
                && Same(Path.GetDirectoryName(toolPaths["strykerCli"])!, Path.GetDirectoryName(toolPaths["strykerCore"])!)
                && Same(toolPaths["wrapperDeps"], Path.ChangeExtension(toolPaths["wrapper"], ".deps.json"))
                && Same(toolPaths["wrapperRuntimeConfig"], Path.ChangeExtension(toolPaths["wrapper"], ".runtimeconfig.json")), "Child producer deployment is inconsistent.");
            JsonObject config = files.Document(configuration);
            ValidateRenderedConfiguration(files.Document(Under(snapshot, Text(profile, "strykerConfigFile"))), config, source.Path, Text(batch, "testFilterSha256"));
            JsonObject state = files.Document(Under(snapshot, ".cis/local/testing/incremental/" + id + "/state.json"));
            Need(state["recoveryRequired"]?.GetValue<bool>() == false && state["activeExecutionUntilUtc"] is null, "Child native state retains unfinished work.");
            JsonObject stage = Item(Item(state["batches"]).Select(pair => Item(pair.Value))
                .Single(row => Text(row, "sourceFile") == Core + source.Path)["mutation"]);
            Need(Text(stage, "reportSha256") == reportHash && Text(stage, "fingerprint") == Text(batch, "mutationFingerprint"), "Child attempt belongs to a different native stage.");
            JsonObject execution = Item(stage["execution"]), nativeBinding = Item(value["native"]);
            string[] outerCommand = Strings(execution, "command");
            JsonObject verifiedExecution = batch["executions"]!.AsArray().Select(Item).Single(row => Text(row, "role") == "mutation");
            Need(CommandHash(outerCommand) == Text(verifiedExecution, "commandSha256") && Text(execution, "status") == "exited"
                && execution["error"] is null && !execution["outputTruncated"]!.GetValue<bool>()
                && Number(execution, "exitCode") == Number(nativeBinding, "exitCode") && Number(nativeBinding, "exitCode") is 0 or 2,
                "Child wrapper/native invocation differs from the freshly verified shared execution.");
            long maximumChildBytes = ValidateCommand(outerCommand, Strings(nativeBinding, "command"), toolPaths, language, id, childRoot, nativeOutput, configuration);
            JsonObject phases = Item(value["phases"]); Fields(phases, "prepared", "started");
            string preparedPath = files.Pin(Item(phases["prepared"])), startedPath = files.Pin(Item(phases["started"]));
            Need(Same(preparedPath, Under(attempt, "prepared.json")) && Same(startedPath, Under(attempt, "started.json")), "Wrapper phases escaped their original attempt.");
            JsonObject prepared = files.Document(preparedPath), started = files.Document(startedPath);
            ValidatePhases(value, prepared, started, Item(phases["prepared"]));
            DateTimeOffset first = Time(nativeBinding, "startedUtc"), last = Time(nativeBinding, "finishedUtc");
            ValidateNativeWindow(nativeBinding, stage);
            JsonObject capture = Item(value["capture"]);
            Fields(capture, "root", "sessionRoot", "bytesBeforeBinding", "maximumBytes");
            string sessionRoot = Under(attempt, "session");
            Need(Same(Text(capture, "root"), childRoot) && Same(Text(capture, "sessionRoot"), sessionRoot)
                && Number(capture, "maximumBytes") == maximumChildBytes
                && Number(capture, "bytesBeforeBinding") is >= 0 && Number(capture, "bytesBeforeBinding") <= Number(capture, "maximumBytes"), "Child capture storage binding is invalid.");
            PinTree(attempt);
            string[] retained = SafeFiles(childRoot).Take(32769).ToArray();
            Need(retained.Length <= 32768 && retained.Sum(path => new FileInfo(path).Length) <= Number(capture, "maximumBytes"), "The retained child root exceeds its explicit native bound.");
            files.TrackInventory(childRoot);
            // The session is the existing Pascal-case v4 capture protocol. Its
            // bytes were pinned/reparsed before the maintained typed reader runs.
            JsonObject sessionDocument = files.Document(sessionPath);
            Fields(sessionDocument, "Schema", "LanguageRoot", "OutputRoot", "ConfigurationPath", "ConfigurationSha256", "StrykerCorePath", "StrykerCoreSha256",
                "SelectorSourceSha256", "DotnetPath", "DotnetSha256", "CoreLibraryPath", "CoreLibrarySha256", "SourceInputs", "CreatedUtc", "CoverageAnalysisOff",
                "AllowInitialUninstrumentedBaseline", "SourceReplay", "RequireWindowsErrorMode", "BoundIntentVersion");
            var session = HealthcheckMutationChildProtocol.Read<HealthcheckMutationChildProtocol.Session>(sessionPath);
            Need(session.Schema == HealthcheckMutationChildProtocol.Schema && session.SourceReplay is null && session.CoverageAnalysisOff
                && session.BoundIntentVersion == HealthcheckMutationChildCapture.Version
                && session.RequireWindowsErrorMode == OperatingSystem.IsWindows()
                && Same(session.LanguageRoot, language) && Same(session.OutputRoot, sessionRoot)
                && Same(session.ConfigurationPath, configuration) && session.ConfigurationSha256 == Hash(configuration)
                && Same(session.StrykerCorePath, toolPaths["strykerCore"]) && session.StrykerCoreSha256 == Hash(toolPaths["strykerCore"])
                && Same(session.DotnetPath, toolPaths["dotnet"]) && session.DotnetSha256 == Hash(toolPaths["dotnet"]), "Session is not this exact automatic native attempt.");
            Need(session.CreatedUtc <= Time(prepared, "createdUtc") && Time(prepared, "createdUtc") <= first
                && session.SourceInputs.Length is > 0 and <= 16384, "Child session source closure/chronology is invalid.");
            foreach (var pin in session.SourceInputs)
            {
                string path = Under(language, pin.Path); PinFile(path, pin.Sha256);
            }
            PinFile(session.CoreLibraryPath, session.CoreLibrarySha256);
            HealthcheckChildMutationVerification.ValidateCurrentSession(session);
            string launchesRoot = Under(sessionRoot, "launches");
            string[] directories = Directory.GetDirectories(launchesRoot);
            Need(directories.Length <= 8192, "Child launch inventory exceeds its bounded receiving scope.");
            HealthcheckMutationChildCapture.AuditResult? captureAudit = null;
            bool unitOnly = AllNativeKillersAreUnit(snapshot, Strings(profile, "testFiles"), files.Document(report), definitions,
                path => { files.Guard(Path.GetDirectoryName(path)!); PinFile(path, Hash(path)); return File.ReadAllText(path); });
            if (IsEmptyUnitOnlyCapture(session, directories.Length, unitOnly))
            {
                return new(source.Group, reportHash, true, true, [], [], Observation("EmptyUnitOnlyCapture", null));
            }
            Need(directories.Length > 0, "An empty capture cannot attest integration/native child execution.");
            captureAudit = HealthcheckMutationChildCapture.Audit(session, sessionPath, directories);
            Need(captureAudit.Version == HealthcheckMutationChildCapture.Version && captureAudit.Failures.Length == 0
                && !captureAudit.HistoricalLegacyCaptureClaim, "The actual bound-intent child capture is incomplete or failed.");
            var launches = directories.Select(directory =>
            {
                string path = Under(directory, "launch.json");
                files.Document(path);
                var launch = HealthcheckMutationChildProtocol.Read<HealthcheckMutationChildProtocol.Launch>(path);
                Need(launch.CreatedUtc >= first && launch.CreatedUtc <= last, "A child launch is outside its original native attempt.");
                Need(launch.ActiveMutant < 0 || definitions.Any(definition => definition.Id == launch.ActiveMutant.ToString(CultureInfo.InvariantCulture)),
                    "A child active selector is absent from this exact native source report.");
                Need(Inside(language, launch.Stage.OriginalDirectory) && Inside(sessionRoot, launch.Stage.Directory)
                    && Inside(language, launch.ParentAssemblyPath), "Child stage/test origin escaped its native snapshot.");
                PinTree(launch.Stage.OriginalDirectory);
                PinFile(launch.ParentAssemblyPath, launch.ParentAssemblySha256);
                return launch;
            }).ToArray();
            string[] activeKinds = launches.Where(launch => launch.Instrumented && launch.ActiveMutant >= 0)
                .Select(launch => Kind(launch.Stage.AssemblyFile)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            Need(activeKinds.Length > 0, "No actual child selected an active native mutant.");
            string required = activeKinds.Length == 2 ? "both" : activeKinds[0];
            string auditOutput = Path.Combine(output, "child-audits", source.Group + "-" + reportHash[..16]);
            int exit = await HealthcheckChildMutationVerification.RunAsync(["audit", "--session", sessionPath, "--output", auditOutput, "--require-kind", required]);
            Need(exit == 0, "The maintained child auditor rejected actual child origin/body/selector/runtime/terminal evidence.");
            string auditReport = Path.Combine(auditOutput, "results.json");
            return new(source.Group, reportHash, true, false, activeKinds, [], Observation("ChildPropagationVerified", auditReport));

            JsonObject Observation(string status, string? audit) => new()
            {
                ["group"] = source.Group,
                ["sourcePath"] = source.Path,
                ["reportSha256"] = reportHash,
                ["bindingSha256"] = Hash(selected.Path),
                ["sessionSha256"] = Hash(sessionPath),
                ["status"] = status,
                ["nativeKillPolicy"] = "cis-native-completed/v1",
                ["captureAuditPassed"] = captureAudit is not null,
                ["fullChildAuditPassed"] = audit is not null,
                ["auditReport"] = audit,
                ["auditReportSha256"] = audit is null ? null : Hash(audit),
                ["captureIntents"] = captureAudit?.IntentCount ?? 0,
                ["captureLaunches"] = captureAudit?.LaunchDirectoryCount ?? 0
            };
            void PinFile(string path, string sha)
            {
                files.Pin(new JsonObject { ["path"] = path, ["sha256"] = sha, ["bytes"] = new FileInfo(path).Length });
            }
            void PinTree(string root)
            {
                root = Path.GetFullPath(root);
                files.Guard(root);
                // Many launches share the same original fixture directory.
                // Pin it once; PinnedInputs still rechecks every unique file and
                // its registered inventory when the complete receipt closes.
                if (!pinnedTrees.Add(root)) { return; }
                files.TrackInventory(root);
                long bytes = 0; int count = 0;
                foreach (string path in SafeFiles(root))
                {
                    long length = new FileInfo(path).Length;
                    Need(++count <= 16384 && length <= 134217728 && (bytes += length) <= 1073741824, "Child evidence tree exceeds receiving bounds.");
                    PinFile(path, Hash(path));
                    if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) { files.Document(path); }
                }
            }
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or IOException or UnauthorizedAccessException or JsonException or ArgumentException or FormatException)
        { return Held("InvalidSharedChildEvidence", error.Message); }

        Result Held(string code, string detail) => new(source.Group, reportHash, false, false, [], [new(code, detail, [])],
            new JsonObject { ["group"] = source.Group, ["sourcePath"] = source.Path, ["reportSha256"] = reportHash, ["status"] = code, ["detail"] = detail });
    }

    internal static bool IsEmptyUnitOnlyCapture(HealthcheckMutationChildProtocol.Session session, int directories, bool onlyUnitKillers)
    {
        if (!onlyUnitKillers || directories != 0 || session.Schema != HealthcheckMutationChildProtocol.Schema
            || session.SourceReplay is not null || session.BoundIntentVersion != HealthcheckMutationChildCapture.Version) { return false; }
        // The original Capture.Audit deliberately requires at least one intent.
        // This is a separate physical empty-session inspection after current
        // source/selector/launch-site validation; it creates no AuditResult and
        // cannot satisfy the aggregate active-child requirement.
        foreach (string name in new[] { "intents", "capture-phases", "launches", "stages" })
        {
            string root = Under(session.OutputRoot, name);
            Need(Directory.Exists(root) && !File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint), "Missing or linked prepared child capture root: " + name);
            if (Directory.EnumerateFileSystemEntries(root).Any()) { return false; }
        }
        return true;
    }

    internal static void ValidateNativeWindow(JsonObject native, JsonObject stage)
    {
        JsonObject execution = Item(stage["execution"]);
        long duration = Number(execution, "durationMilliseconds");
        DateTimeOffset first = Time(native, "startedUtc"), last = Time(native, "finishedUtc"), finished = Time(stage, "finishedUtc");
        // A compatible forced Unit refresh may postdate this retained mutation.
        // Bind the actual native stage instead; allow bounded report assessment
        // I/O after process exit, rather than comparing the latest Unit baseline.
        Need(duration is >= 0 and <= 3600000 && first <= last
            && first >= finished.AddMilliseconds(-duration).AddSeconds(-30) && last <= finished.AddSeconds(1)
            && (last - first).TotalMilliseconds <= duration + 1000, "Child native chronology differs from its canonical stage.");
    }

    internal static bool AllNativeKillersAreUnit(string snapshot, string[] testFiles, JsonObject report, Definition[] definitions, Func<string, string> read)
    {
        Definition[] killed = definitions.Where(definition => definition.Status == "Killed").ToArray();
        if (killed.Length == 0) { return true; } // No credit is created by this neutral empty population.
        if (killed.Any(definition => definition.KilledBy.Length == 0) || report["testFiles"] is not JsonObject nativeFiles) { return false; }
        var origins = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in nativeFiles)
        {
            string[] matches = testFiles.Where(path => NativePath(file.Key, path, snapshot)).ToArray();
            if (matches.Length != 1) { continue; }
            string path = matches[0]; JsonObject value = Item(file.Value);
            if (!path.StartsWith(Unit, StringComparison.Ordinal) || Text(value, "source") != read(Under(snapshot, path))) { continue; }
            JsonNode tests = value["tests"] ?? throw new InvalidDataException("Missing native test identities.");
            IEnumerable<JsonObject> rows = tests is JsonArray array ? array.Select(Item) : tests.AsObject().Select(row => Item(row.Value));
            foreach (JsonObject row in rows)
            {
                string id = Text(row, "id"); Need(Guid.TryParse(id, out _), "Malformed native test GUID.");
                if (!origins.TryGetValue(id, out var values)) { origins.Add(id, values = []); }
                values.Add(path);
            }
        }
        // Count every raw occurrence, including foreign/integration duplicates.
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in nativeFiles)
        {
            JsonNode tests = Item(file.Value)["tests"] ?? throw new InvalidDataException("Missing native test identities.");
            IEnumerable<JsonObject> rows = tests is JsonArray array ? array.Select(Item) : tests.AsObject().Select(row => Item(row.Value));
            foreach (JsonObject row in rows) { string id = Text(row, "id"); counts[id] = counts.GetValueOrDefault(id) + 1; }
        }
        return killed.SelectMany(definition => definition.KilledBy).All(id => origins.TryGetValue(id, out var paths)
            && paths.Count == 1 && counts.GetValueOrDefault(id) == 1);
    }

    internal static HealthcheckSharedMutationModel.Batch[] ApplyAggregateHolds(HealthcheckSharedMutationModel.Batch[] batches, Result[] evidence)
        => batches.Select(batch =>
        {
            if (batch.Source.Group is not ("pipes" or "aspnet")) { return batch; }
            string kind = batch.Source.Group == "pipes" ? "probe" : "http-host";
            bool present = evidence.Any(row => row.Verified && !row.UnitOnly && row.Group == batch.Source.Group
                && row.ActiveKinds.Contains(kind, StringComparer.Ordinal)
                && batches.Any(origin => origin.Source.Group == row.Group && origin.ReportSha256 == row.ReportSha256));
            return present ? batch : batch with
            {
                VerifiedKilledIds = [],
                VerifiedTimeoutKilledIds = [],
                Issues = [.. batch.Issues, new("MissingSharedActiveChild", "The complete partition requires at least one actual selected " + kind + " child with verified origin/body/terminal evidence.", [])]
            };
        }).ToArray();

    internal static void ValidateRenderedConfiguration(JsonObject template, JsonObject actual, string relativeSource, string filterHash)
    {
        var expected = (JsonObject)template.DeepClone(); JsonObject config = Item(expected["stryker-config"]);
        config["mutate"] = new JsonArray(relativeSource);
        string filter = Text(Item(actual["stryker-config"]), "test-case-filter");
        Need(CommandHash(filter) == filterHash, "Child native test filter differs from the verified batch.");
        config["test-case-filter"] = filter;
        Need(JsonNode.DeepEquals(expected, actual), "Retained child configuration is not the exact rendered full-file native configuration.");
    }

    internal static void ValidatePhases(JsonObject binding, JsonObject prepared, JsonObject started, JsonObject preparedPin)
    {
        Fields(prepared, "schema", "profileId", "attemptId", "phase", "languageRoot", "outputRoot", "workingDirectory", "config", "session", "source", "tools", "command", "createdUtc");
        Fields(started, "schema", "prepared", "processId", "startedUtc", "command");
        Need(Text(prepared, "schema") == "cis.healthcheck.shared-child-attempt-prepared/v1"
            && Text(started, "schema") == "cis.healthcheck.shared-child-attempt-started/v1", "Unknown native child attempt phase schema.");
        foreach (string name in new[] { "profileId", "attemptId", "phase", "languageRoot", "outputRoot", "workingDirectory", "config", "session", "source", "tools" })
        {
            Need(JsonNode.DeepEquals(prepared[name], binding[name]), "Child prepared/final binding changed: " + name);
        }

        JsonObject native = Item(binding["native"]);
        Need(JsonNode.DeepEquals(started["prepared"], preparedPin) && Number(started, "processId") > 0
            && Number(started, "processId") == Number(native, "processId") && Time(started, "startedUtc") == Time(native, "startedUtc")
            && Strings(prepared, "command").SequenceEqual(Strings(native, "command"), StringComparer.Ordinal)
            && Strings(started, "command").SequenceEqual(Strings(native, "command"), StringComparer.Ordinal), "Actual child native process/command differs across its durable phases.");
    }

    internal static long ValidateCommand(string[] outer, string[] native, Dictionary<string, string> tools,
        string language, string profile, string childRoot, string output, string retainedConfig)
    {
        Need(outer.Length is > 11 and <= 512 && Same(outer[0], tools["dotnet"])
            && outer[1] == "exec" && outer[2] == "--fx-version" && outer[3] == "10.0.12"
            && outer[4] == "--roll-forward" && outer[5] == "Disable" && Same(outer[6], tools["wrapper"])
            && outer[7] == "healthcheck-mutation-run", "Native execution did not enter the pinned runtime/Healthcheck wrapper.");
        int separator = Array.IndexOf(outer, "--");
        Need(separator > 8 && (separator - 8) % 2 == 0, "Malformed wrapper/native command boundary.");
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 8; index < separator; index += 2)
        {
            Need(options.TryAdd(outer[index], outer[index + 1]), "Repeated wrapper option.");
        }

        string[] required = ["--language-root", "--profile-id", "--dotnet", "--stryker-cli", "--stryker-core", "--child-root"];
        Need(required.All(options.ContainsKey) && options.Keys.All(key => required.Contains(key, StringComparer.Ordinal) || key == "--maximum-child-bytes")
            && Same(options["--language-root"], language) && options["--profile-id"] == profile && Same(options["--child-root"], childRoot)
            && Same(options["--dotnet"], tools["dotnet"]) && Same(options["--stryker-cli"], tools["strykerCli"]) && Same(options["--stryker-core"], tools["strykerCore"]), "Wrapper tool/profile/root inputs differ.");
        string[] args = outer[(separator + 1)..];
        Need(args.Length is > 0 and <= 256, "Missing native Stryker arguments.");
        int configIndex = Array.IndexOf(args, "--config-file"), outputIndex = Array.IndexOf(args, "--output");
        Need(configIndex >= 0 && configIndex + 1 < args.Length && args.Count(value => value == "--config-file") == 1
            && outputIndex >= 0 && outputIndex + 1 < args.Length && args.Count(value => value == "--output") == 1
            && Same(args[configIndex + 1], Under(output, "stryker-config.json")) && Same(args[outputIndex + 1], output), "Native original CIS output/config selector differs.");
        args[configIndex + 1] = retainedConfig;
        Need(native.SequenceEqual(new[] { tools["dotnet"], tools["strykerCli"] }.Concat(args), StringComparer.Ordinal), "The inner native command changes more than the retained configuration path.");
        long maximum = 268435456;
        if (options.TryGetValue("--maximum-child-bytes", out string? configured))
        {
            Need(long.TryParse(configured, NumberStyles.None, CultureInfo.InvariantCulture, out maximum)
                && maximum is > 0 and <= 1073741824 && configured == maximum.ToString(CultureInfo.InvariantCulture), "The actual wrapper child byte limit is invalid.");
        }
        return maximum;
    }

    private static void BindingFields(JsonObject value)
    {
        Fields(value, "schema", "profileId", "attemptId", "phase", "languageRoot", "outputRoot", "workingDirectory", "config", "session", "source", "tools", "phases", "native", "capture");
        Need(Text(value, "schema") == "cis.healthcheck.shared-child-attempt/v1", "Unknown child binding schema.");
        Fields(Item(value["native"]), "command", "processId", "exitCode", "startedUtc", "finishedUtc", "reportSha256", "reportBytes");
    }
    private static IEnumerable<string> SafeFiles(string root, int depth = 0)
    {
        Need(depth <= 32, "Child evidence directory nesting exceeds its bound.");
        Need(Directory.Exists(root), "Missing child evidence directory.");
        Need(!File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint), "Linked child evidence directory.");
        foreach (string path in Directory.EnumerateFileSystemEntries(root))
        {
            Need(!File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint), "Linked child evidence entry.");
            if (Directory.Exists(path))
            {
                foreach (string nested in SafeFiles(path, depth + 1))
                {
                    yield return nested;
                }
            }
            else
            {
                yield return path;
            }
        }
    }
    private static string Kind(string assembly) => assembly switch { "CisHealthProbe.dll" => "probe", "CIS.Healthcheck.HttpFixture.dll" => "http-host", _ => throw new InvalidDataException("Unknown actual child executable.") };
    private static string Full(string root, string path) => Path.IsPathFullyQualified(path) ? Path.GetFullPath(path) : Under(root, path);
    private static bool NativePath(string native, string relative, string snapshot) => native.Replace('\\', '/').EndsWith("/" + relative, StringComparison.Ordinal)
        || Same(Path.GetFullPath(native.Replace('\\', Path.DirectorySeparatorChar), Under(snapshot, Core.TrimEnd('/'))), Under(snapshot, relative));
    private static string Under(string root, string relative)
    {
        Need(!Path.IsPathFullyQualified(relative) && !relative.Contains('\\') && !relative.Contains(':') && relative.Split('/').All(part => part.Length > 0 && part is not ("." or "..")), "Unsafe child evidence relative path.");
        string path = Path.GetFullPath(Path.Combine(root, relative)); Need(Inside(root, path), "Child evidence escaped its root."); return path;
    }
    private static bool Inside(string root, string path) => Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, Comparison);
    private static bool Same(string first, string second) => string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), Comparison);
    private static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(stream)); }
    internal static string CommandHash<T>(T value) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, Json)));
    private static string Text(JsonObject value, string name) => value[name]?.GetValue<string>() ?? throw new InvalidDataException("Missing child field: " + name);
    private static long Number(JsonObject value, string name) => value[name]?.GetValue<long>() ?? throw new InvalidDataException("Missing child number: " + name);
    private static DateTimeOffset Time(JsonObject value, string name) => DateTimeOffset.Parse(Text(value, name), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    private static string[] Strings(JsonObject value, string name) => value[name]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();
    private static JsonObject Item(JsonNode? node) => node as JsonObject ?? throw new InvalidDataException("Expected child object.");
    private static void Fields(JsonObject value, params string[] names) => Need(value.Count == names.Length && names.All(value.ContainsKey), "Unexpected or missing child binding fields.");
    private static void Need(bool condition, string detail)
    {
        if (!condition)
        {
            throw new InvalidDataException(detail);
        }
    }
}
