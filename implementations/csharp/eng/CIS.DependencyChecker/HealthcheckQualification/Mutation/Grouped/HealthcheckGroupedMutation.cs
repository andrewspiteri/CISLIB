using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using CIS.TestSupport;
using static CIS.Verification.TypeManagerVerificationInputs;

namespace CIS.Verification;

// Prospective grouped native evidence. Historical catalogue receivers keep their original meaning.
internal static partial class HealthcheckGroupedMutation
{
    private const string LanguagePrefix = "implementations/csharp/";
    private const string CorePrefix = LanguagePrefix + "src/CIS.Core/";
    private const long OwnedLimit = 2L * 1024 * 1024 * 1024;
    private const long RetainedLimit = 256L * 1024 * 1024;
    private const long FreeReserve = 2L * 1024 * 1024 * 1024;
    internal const int NativeConcurrency = 8;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    internal sealed record Input(string Path, string Sha256);
    internal sealed record Candidate(string Schema, string Identity, string Platform, string Host, Input[] Inputs, string[] Sources, string ArchiveSha256)
    {
        public JsonObject? PreparationVolume { get; init; }
    }
    internal sealed record Group(string Name, string[] Sources, string[] Projects, string Filter, string Coverage)
    {
        public IReadOnlyDictionary<string, string> BuildProperties => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CisMutationTesting"] = "true",
            ["CisHealthcheckMutationCohort"] = "true",
            ["CisHealthcheckPilotCohort"] = Name == "pilot" ? "true" : "false",
            ["CisLinuxIdentityQualification"] = Name == "pipes" && OperatingSystem.IsLinux() ? "true" : "false"
        };
    }
    internal sealed record StorageVolume(string RequestedPath, string QueryDirectory, string MountPoint)
    {
        internal long AvailableBytes()
        {
            var volume = new DriveInfo(QueryDirectory);
            Require(volume.IsReady, "The selected storage volume is not ready: " + QueryDirectory);
            return volume.AvailableFreeSpace;
        }

        internal JsonObject Observation() => new()
        {
            ["requestedPath"] = RequestedPath,
            ["queryDirectory"] = QueryDirectory,
            ["mountPoint"] = MountPoint,
            ["availableFreeBytes"] = AvailableBytes()
        };
    }

    internal static async Task<int> RunAsync(string[] args)
    {
        Require(args.Length > 0, "Use healthcheck-grouped-mutation prepare, run or receive with named inputs.");
        return args[0] switch
        {
            "prepare" => await PrepareAsync(args[1..]),
            "run" => await RunGroupAsync(args[1..]),
            "pilot-controls" => await PilotControlsAsync(args[1..]),
            "export-platform-proof" => await ExportPlatformProofAsync(args[1..]),
            "receive" => await ReceiveAsync(args[1..]),
            _ => throw new ArgumentException("Unknown grouped mutation command.")
        };
    }

    private static async Task<int> PrepareAsync(string[] args)
    {
        var input = new TypeManagerVerificationInputs(args, ["repo-root", "output-root"], []);
        input.EmptyOutput();
        string repo = input.PathOf("repo-root"), output = input.Output, workspace = Path.Combine(output, "workspace");
        string outputRelative = Path.GetRelativePath(repo, output).Replace('\\', '/');
        Require(!new[] { "implementations/csharp", "conformance", "contracts", "compatibility/csharp", "eng", "docs" }.Any(path => outputRelative == path || outputRelative.StartsWith(path + "/", StringComparison.Ordinal)),
            "Candidate output cannot be nested inside captured source inputs.");
        Input[] inputs = Capture(repo);
        Require(inputs.Length > 0, "No candidate inputs were found.");
        long preparationBytes = inputs.Sum(pin => new FileInfo(Path.Combine(repo, pin.Path)).Length);
        Require(2 * preparationBytes + 256L * inputs.Length + 1024 * 1024 <= OwnedLimit,
            "The immutable source/workspace preparation cannot fit the 2 GiB owned pilot budget.");
        StorageVolume preparationVolume = ResolveStorageVolume(output);
        Require(preparationVolume.AvailableBytes() >= FreeReserve + 2 * preparationBytes,
            "Candidate preparation would breach the 2 GiB free-space reserve.");
        foreach (Input pin in inputs) { Copy(Path.Combine(repo, pin.Path), Path.Combine(workspace, pin.Path), pin.Sha256); }
        string[] sources = inputs.Where(pin => pin.Path.StartsWith(CorePrefix + "Healthcheck/", StringComparison.Ordinal) && pin.Path.EndsWith(".cs", StringComparison.Ordinal))
            .Select(pin => pin.Path[CorePrefix.Length..]).ToArray();
        Require(sources.Length > 0, "The current Healthcheck source inventory is empty.");
        string platform = RuntimeInformation.OSDescription + "; " + RuntimeInformation.ProcessArchitecture + "; " + RuntimeInformation.FrameworkDescription;
        string identity = CandidateIdentity(inputs, platform, Environment.MachineName);
        string archive = Path.Combine(output, "candidate.zip");
        ZipFile.CreateFromDirectory(workspace, archive, CompressionLevel.Optimal, includeBaseDirectory: false);
        var candidate = new Candidate("cis.healthcheck.grouped-candidate/v1", identity,
            platform,
            Environment.MachineName, inputs, sources, Hash(archive))
        { PreparationVolume = preparationVolume.Observation() };
        Write(Path.Combine(output, "candidate.json"), JsonSerializer.Serialize(candidate, Json));
        foreach (Group group in Groups(candidate))
        {
            WriteJson(Path.Combine(output, "profiles", group.Name + ".json"), Configuration(group));
        }
        // The archive retains dirty/new inputs without requiring a historical Git materialization.
        Require(Capture(repo).SequenceEqual(inputs), "Candidate inputs changed during preparation.");
        Console.WriteLine($"Prepared {identity}: {sources.Length} Healthcheck sources; one immutable archive and one active workspace.");
        await Task.CompletedTask;
        return 0;
    }

    internal static Input[] Capture(string repo) => SourceInventory.CaptureVerificationInputs(repo)
        .Select(pin => new Input(pin.Path, pin.Sha256)).ToArray();

    internal static Group[] Groups(Candidate candidate)
    {
        const string unit = "../../tests/CIS.Core.UnitTests/CIS.Core.UnitTests.csproj";
        const string integration = "../../tests/CIS.Core.IntegrationTests/CIS.Core.IntegrationTests.csproj";
        const string scheduling = "FullyQualifiedName=CIS.Tests.TestAssemblySetup.SchedulingMatchesRequestedMode";
        const string unitFilter = "FullyQualifiedName~CIS.Tests.UnitTests.Healthcheck.|FullyQualifiedName~CIS.Tests.UnitTests.HealthcheckValueContractTests.|" + scheduling;
        const string httpFilter = "(FullyQualifiedName~HealthcheckHttp&FullyQualifiedName!~.RealKestrelServesKnownViewsWithCachePreventionAndNoBusinessEffects&FullyQualifiedName!~.ClientCancellationOfAuthorizationDoesNotAffectLaterHealthOrBusiness)|" + scheduling;
        const string pipesFilter = "((FullyQualifiedName~HealthPipe|FullyQualifiedName~HealthcheckPipe)&FullyQualifiedName!~.WindowsPipeEndpointDescriptorsReleasePinsAfterActualSettlement)|FullyQualifiedName~HealthProbeClientTransportTests|FullyQualifiedName~HealthProbeCallerCompletionQualificationTests|FullyQualifiedName~HealthProbeCancellationCompletionQualificationTests|FullyQualifiedName~HealthReport|" + scheduling;
        string[] pilot = ["Healthcheck/Contracts/HealthDiagnostic.cs", "Healthcheck/Contracts/HealthMeasurement.cs", "Healthcheck/Contracts/Collection/IHealthCollector.cs", "Healthcheck/Execution/HealthExecutionBinder.cs"];
        Require(pilot.All(candidate.Sources.Contains), "The fixed pilot source changed; review its recipe before running.");
        return
        [
            new("pilot", pilot.Order(StringComparer.Ordinal).ToArray(), [unit], unitFilter, "perTest"),
            new("runtime", candidate.Sources.Where(path => !pilot.Contains(path, StringComparer.Ordinal) && !path.StartsWith("Healthcheck/AspNetCore/", StringComparison.Ordinal) && !path.StartsWith("Healthcheck/Pipes/", StringComparison.Ordinal)).ToArray(), [unit], unitFilter, "perTest"),
            new("http", candidate.Sources.Where(path => path.StartsWith("Healthcheck/AspNetCore/", StringComparison.Ordinal)).ToArray(), [integration], httpFilter, "perTest"),
            new("pipes", candidate.Sources.Where(path => path.StartsWith("Healthcheck/Pipes/", StringComparison.Ordinal)).ToArray(), [unit, integration], pipesFilter, "perTest")
        ];
    }

    internal static JsonObject Configuration(Group group) => new()
    {
        ["stryker-config"] = new JsonObject
        {
            ["project"] = "CIS.Core.csproj",
            ["test-projects"] = JsonSerializer.SerializeToNode(group.Projects),
            ["test-case-filter"] = group.Filter,
            ["test-runner"] = "vstest",
            ["target-framework"] = "net10.0",
            ["configuration"] = "Release",
            ["mutate"] = JsonSerializer.SerializeToNode(group.Sources),
            ["mutation-level"] = "Complete",
            ["coverage-analysis"] = group.Coverage,
            ["reporters"] = new JsonArray("json", "progress", "cleartext"),
            ["concurrency"] = NativeConcurrency,
            ["disable-bail"] = true,
            ["additional-timeout"] = 10000,
            // The 90% gate belongs to the complete disjoint union, never an individual group.
            ["thresholds"] = new JsonObject { ["high"] = 100, ["low"] = 90, ["break"] = 0 }
        }
    };

    internal static Candidate CandidateAt(string root)
    {
        Candidate candidate = JsonSerializer.Deserialize<Candidate>(File.ReadAllText(Path.Combine(root, "candidate.json")), Json)
            ?? throw new InvalidDataException("Missing candidate manifest.");
        Require(candidate.Schema == "cis.healthcheck.grouped-candidate/v1" && candidate.Identity == CandidateIdentity(candidate.Inputs, candidate.Platform, candidate.Host), "Candidate identity is invalid.");
        ValidateInventory(candidate);
        Require(candidate.ArchiveSha256 == Hash(Path.Combine(root, "candidate.zip")), "Candidate archive changed.");
        Require(candidate.Inputs.Select(pin => pin.Path).Distinct(StringComparer.Ordinal).Count() == candidate.Inputs.Length, "Duplicate candidate inputs.");
        return candidate;
    }

    private static string CandidateIdentity(Input[] inputs, string platform, string host) => Digest(JsonSerializer.SerializeToUtf8Bytes(new { inputs, platform, host }));

    internal static void ValidateInventory(Candidate candidate)
    {
        string[] sources = candidate.Inputs.Where(pin => pin.Path.StartsWith(CorePrefix + "Healthcheck/", StringComparison.Ordinal) && pin.Path.EndsWith(".cs", StringComparison.Ordinal))
            .Select(pin => pin.Path[CorePrefix.Length..]).Order(StringComparer.Ordinal).ToArray();
        Require(sources.Length > 0 && candidate.Sources.SequenceEqual(sources, StringComparer.Ordinal), "Candidate inventory omits, duplicates or changes frozen Healthcheck sources.");
    }

    private static async Task<int> RunGroupAsync(string[] args)
    {
        var clock = Stopwatch.StartNew();
        var input = new TypeManagerVerificationInputs(args, ["candidate-root", "group", "stryker-cli"], ["budget-seconds", "tool-dotnet", "owned-root"]);
        string root = input.PathOf("candidate-root");
        string ownedRoot = ValidateOwnedScope(root, input.Optional("owned-root") ?? root);
        using var lease = new FileStream(Path.Combine(root, "active.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        Candidate candidate = CandidateAt(root);
        Group group = Groups(candidate).Single(item => item.Name == input.Get("group"));
        JsonObject executionIdentity = ObserveExecutionIdentity();
        int processorCount = Environment.ProcessorCount;
        ValidateProcessorCapacity(processorCount);
        int budget = int.Parse(input.Optional("budget-seconds") ?? "600", System.Globalization.CultureInfo.InvariantCulture);
        Require(budget is > 0 and <= 3600 && (group.Name != "pilot" || budget <= 600), "Pilot ceiling is ten minutes; a full group needs a declared budget no greater than one hour.");
        if (group.Name != "pilot")
        {
            ValidatePilot(root, candidate);
            foreach (Group prior in Groups(candidate).Where(item => (item.Name is "http" or "pipes")
                && Directory.Exists(Path.Combine(root, "groups", item.Name))))
            { ValidateCompletedTransportAsyncControls(root, candidate, prior); }
        }
        string workspace = Path.Combine(root, "workspace"), language = Path.Combine(workspace, LanguagePrefix), output = Path.Combine(root, "groups", group.Name);
        Require(!Directory.Exists(output), "A group attempt already exists; retain its result and prepare a fresh candidate attempt after a relevant change.");
        VerifyInputs(workspace, candidate);
        string tool = input.PathOf("stryker-cli");
        Require(File.Exists(tool) && File.Exists(Path.Combine(Path.GetDirectoryName(tool)!, "Stryker.Core.dll")), "Restore Stryker 4.16.0 before running mutation.");
        Require(System.Reflection.AssemblyName.GetAssemblyName(tool).Version == new Version(4, 16, 0, 0), "The native CLI is not the pinned Stryker 4.16.0 assembly.");
        JsonObject toolPolicy = Read(Path.Combine(language, "eng/toolchain.json"));
        Require((string?)toolPolicy["sdk"] == "10.0.401" && (string?)toolPolicy["targetRuntime"] == "10.0.12" && (string?)toolPolicy["toolHostRuntime"] == "8.0.31", "Requalify changed toolchain pins.");
        string toolsManifest = Path.Combine(language, ".config/dotnet-tools.json");
        Require((string?)Read(toolsManifest)["tools"]?["dotnet-stryker"]?["version"] == "4.16.0", "Use pinned Stryker 4.16.0.");
        // Fail missing tools before building the expensive matrix.
        string subjectDotnet = Executable(input.Dotnet), toolDotnet = Executable(input.Optional("tool-dotnet") ?? input.Dotnet);
        ProcessResult runtimes = await ProcessRunner.RunAsync(toolDotnet, ["--list-runtimes"], language, TimeSpan.FromSeconds(20));
        Require(runtimes.ExitCode == 0 && runtimes.StandardOutput.Contains("Microsoft.NETCore.App 8.0.31 ", StringComparison.Ordinal), "The selected dotnet host is missing pinned Microsoft.NETCore.App 8.0.31; no mutation was started.");
        ProcessResult sdk = await ProcessRunner.RunAsync(input.Dotnet, ["--version"], language, TimeSpan.FromSeconds(20));
        Require(sdk.ExitCode == 0 && sdk.StandardOutput.Trim() == "10.0.401", "The mutation workspace must use SDK 10.0.401.");
        string configuration = Path.Combine(root, "profiles", group.Name + ".json");
        Require(Read(configuration).ToJsonString() == Configuration(group).ToJsonString(), "The stable native group recipe changed.");
        Directory.CreateDirectory(output);
        string temporary = Path.Combine(Path.GetTempPath(), "cgh-" + Guid.NewGuid().ToString("N")[..8]);
        Require(!Directory.Exists(temporary) && !File.Exists(temporary), "Use a fresh owned short temporary root.");
        StorageVolume ownedVolume = ResolveStorageVolume(ownedRoot), candidateVolume = ResolveStorageVolume(root), temporaryVolume = ResolveStorageVolume(temporary);
        long peak = CountOwnedScopeBytes(ownedRoot, temporary);
        Require(peak <= OwnedLimit && Math.Min(ownedVolume.AvailableBytes(), Math.Min(candidateVolume.AvailableBytes(), temporaryVolume.AvailableBytes())) >= FreeReserve,
            "The declared aggregate owned scope exceeds capacity or its actual volume free-space reserve before execution.");
        Directory.CreateDirectory(temporary);
        string temporaryOwner = Path.Combine(temporary, "owner.json");
        Write(temporaryOwner, JsonSerializer.Serialize(new { candidateIdentity = candidate.Identity, group = group.Name }, Json));
        var environment = new Dictionary<string, string?>
        {
            ["CisMutationTesting"] = "true",
            ["CisHealthcheckMutationCohort"] = "true",
            ["UseSharedCompilation"] = "false",
            ["MSBUILDDISABLENODEREUSE"] = "1",
            ["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] = "1",
            ["RestoreLockedMode"] = "true",
            ["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0",
            ["CIS_HEALTHCHECK_MUTATION_SESSION"] = null
        };
        foreach ((string name, string value) in group.BuildProperties) { environment[name] = value; }
        environment["TEMP"] = temporary; environment["TMP"] = temporary; environment["TMPDIR"] = temporary;
        var commandLog = new JsonArray();
        bool restored = false;
        bool processCleanupHealthy = true;
        ProcessResult? native = null;
        Exception? failure = null;
        JsonObject? linuxIdentityBinding = null;
        JsonObject? linuxIdentityReceipt = null;
        try
        {
            foreach (string project in group.Projects)
            {
                string path = Path.GetFullPath(Path.Combine(language, "src/CIS.Core", project));
                await Run("restore-" + Path.GetFileNameWithoutExtension(path), ["restore", path, "--locked-mode"]);
                await Run("build-" + Path.GetFileNameWithoutExtension(path), ["build", path, "-c", "Release", "--no-restore"]);
            }
            await BindToolchainAsync(root, candidate, group, language, subjectDotnet, toolDotnet, tool);
            Toolchain boundTools = ReadToolchain(root, candidate);
            environment["MSBUILD_EXE_PATH"] = SdkMsBuildPath(boundTools);
            environment["PATH"] = Path.GetDirectoryName(subjectDotnet) + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
            ValidateSdkMsBuildEnvironment(new JsonObject { ["environment"] = JsonSerializer.SerializeToNode(environment, Json) }, boundTools);
            linuxIdentityBinding = await PrepareLinuxIdentityAsync(root, candidate, group, language, output, temporary, subjectDotnet,
                environment, (name, command, cwd, executable) => Run(name, command, cwd, executable: executable));
            await Tests("baseline");
            string[] nativeCommand = ["exec", "--fx-version", "8.0.31", "--roll-forward", "Disable", tool,
                "--config-file", configuration, "--output", Path.Combine(output, "native"), "--skip-version-check"];
            if (group.Name == "pilot") { nativeCommand = [.. nativeCommand, "--log-to-file"]; }
            native = await Run("native", nativeCommand,
                Path.Combine(language, "src/CIS.Core"), requireSuccess: false, executable: toolDotnet);
            linuxIdentityReceipt = ValidateLinuxIdentityJournals(root, candidate, group, linuxIdentityBinding);
            Require(native.ExitCode == 0, "Native execution failed; no mutation result receives product kill credit.");
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception or TimeoutException or JsonException)
        {
            failure = error;
            try { linuxIdentityReceipt = ValidateLinuxIdentityJournals(root, candidate, group, linuxIdentityBinding); }
            catch (Exception identityError) when (identityError is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or JsonException)
            { failure = new AggregateException("Execution and foreign-owner infrastructure admission failed.", error, identityError); }
        }
        finally
        {
            try
            {
                VerifyInputs(workspace, candidate);
                for (int index = 0; index < group.Projects.Length; index++)
                {
                    string project = group.Projects[index];
                    string path = Path.GetFullPath(Path.Combine(language, "src/CIS.Core", project));
                    string[] command = ["build", path, "-c", "Release", "--no-restore", "--no-incremental"];
                    // Recompile the shared closure once; subsequent projects consume those freshly restored outputs.
                    if (index > 0) { command = [.. command, "-p:BuildProjectReferences=false"]; }
                    await Run("restored-build-" + Path.GetFileNameWithoutExtension(path), command);
                }
                string restoredCore = Path.Combine(language, "src/CIS.Core/bin/Release/net10.0/CIS.Core.dll");
                string restoredCoreHash = Hash(restoredCore);
                foreach (string project in group.Projects)
                {
                    string directory = Path.GetDirectoryName(Path.GetFullPath(Path.Combine(language, "src/CIS.Core", project)))!;
                    Require(Hash(Path.Combine(directory, "bin/Release/net10.0/CIS.Core.dll")) == restoredCoreHash,
                        "A restored test host still contains different or instrumented Core bytes: " + project);
                }
                await Tests("restored");
                linuxIdentityReceipt = ValidateLinuxIdentityJournals(root, candidate, group, linuxIdentityBinding);
                Require(JsonNode.DeepEquals(executionIdentity, ObserveExecutionIdentity()), "The native group's execution identity changed before restoration completed.");
                restored = true;
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception or TimeoutException or JsonException)
            {
                Exception restoration = new InvalidDataException("Restoration/postflight failed: " + error.Message, error);
                failure = failure is null ? restoration : new AggregateException("Execution and restoration/postflight failed.", failure, restoration);
            }
        }
        string report = Path.Combine(output, "native/reports/mutation-report.json");
        var record = new JsonObject
        {
            ["schema"] = "cis.healthcheck.grouped-run/v1",
            ["candidateIdentity"] = candidate.Identity,
            ["group"] = group.Name,
            ["sources"] = JsonSerializer.SerializeToNode(group.Sources),
            ["configSha256"] = Hash(configuration),
            ["strykerCliSha256"] = Hash(tool),
            ["buildProperties"] = JsonSerializer.SerializeToNode(group.BuildProperties),
            ["environment"] = JsonSerializer.SerializeToNode(environment),
            ["strykerCoreSha256"] = Hash(Path.Combine(Path.GetDirectoryName(tool)!, "Stryker.Core.dll")),
            ["platform"] = candidate.Platform,
            ["host"] = Environment.MachineName,
            ["executionIdentity"] = executionIdentity,
            ["linuxIdentity"] = linuxIdentityReceipt,
            ["commandLog"] = commandLog,
            ["nativeExitCode"] = native?.ExitCode,
            ["processorCount"] = processorCount,
            ["nativeConcurrency"] = NativeConcurrency,
            ["toolchainSha256"] = File.Exists(Path.Combine(root, "toolchain.json")) ? Hash(Path.Combine(root, "toolchain.json")) : null,
            ["temporaryRoot"] = temporary,
            ["ownedStorageScope"] = new JsonObject
            {
                ["root"] = ownedRoot,
                ["candidateIncluded"] = true,
                ["temporaryCountedSeparately"] = !AtOrUnder(ownedRoot, temporary),
                ["limitBytes"] = OwnedLimit
            },
            ["storageVolumes"] = new JsonObject { ["owned"] = ownedVolume.Observation(), ["candidate"] = candidateVolume.Observation(), ["temporary"] = temporaryVolume.Observation() },
            ["processCleanupHealthy"] = processCleanupHealthy,
            ["nativeTrxPins"] = JsonSerializer.SerializeToNode(Directory.EnumerateFiles(output, "*.trx").Order(StringComparer.Ordinal)
                .Select(path => new Input(Path.GetFileName(path), Hash(path))).ToArray(), Json),
            ["baselineProjectBuildInvocations"] = commandLog.Count(command => ((string?)command!["name"])?.StartsWith("build-", StringComparison.Ordinal) == true),
            ["restoredProjectBuildInvocations"] = commandLog.Count(command => ((string?)command!["name"])?.StartsWith("restored-build-", StringComparison.Ordinal) == true),
            ["forcedSharedClosureBuildInvocations"] = commandLog.Count(command => ((string?)command!["name"])?.StartsWith("restored-build-", StringComparison.Ordinal) == true
                && !command!["arguments"]!.AsArray().Any(argument => (string?)argument == "-p:BuildProjectReferences=false")),
            ["nativeInvocations"] = commandLog.Count(command => (string?)command!["name"] == "native"),
            ["restored"] = restored,
            ["failure"] = failure?.Message,
            ["seconds"] = clock.Elapsed.TotalSeconds,
            ["peakOwnedBytes"] = peak,
            ["report"] = File.Exists(report) ? "native/reports/mutation-report.json" : null,
            ["reportSha256"] = File.Exists(report) ? Hash(report) : null
        };
        WriteJson(Path.Combine(output, "run.json"), record);
        try
        {
            Require(File.Exists(temporaryOwner), "The owned short temporary root lost its ownership marker.");
            // A failed process-tree cleanup leaves diagnostics and an explicit hold instead of deleting live output.
            Require(processCleanupHealthy, "Temporary scratch cannot be removed while process-tree cleanup is incomplete.");
            DeleteOwned(temporary, Path.GetTempPath());
            record["temporaryCleanup"] = "owned short temp removed";
            WriteJson(Path.Combine(output, "run.json"), record);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Exception cleanup = new InvalidDataException("Owned temporary cleanup failed: " + error.Message, error);
            failure = failure is null ? cleanup : new AggregateException("Execution and owned temporary cleanup failed.", failure, cleanup);
            record["failure"] = failure.Message;
            WriteJson(Path.Combine(output, "run.json"), record);
        }
        if (failure is not null) { throw failure; }
        Require(restored && File.Exists(report), "No healthy restored native result is available.");
        // Keep unchanged native JSON and bounded diagnostics; remove only this new native scratch.
        string scratch = Path.Combine(output, "native");
        foreach (string path in Directory.EnumerateFileSystemEntries(scratch))
        {
            if (Path.GetFileName(path) == "reports") { continue; }
            DeleteOwned(path, root);
        }
        record["cleanup"] = "native scratch removed; candidate workspace retained for next group";
        record["retainedBytes"] = OwnedBytes(root, skipWorkspace: true);
        Require((long)record["retainedBytes"]! <= RetainedLimit, "Ordinary retained evidence exceeded 256 MiB.");
        WriteJson(Path.Combine(output, "run.json"), record);
        if (group.Name is "http" or "pipes")
        {
            try { record["asyncControls"] = ValidateCompletedTransportAsyncControls(root, candidate, group); }
            catch (InvalidDataException error)
            {
                record["asyncControlFailure"] = error.Message;
                WriteRetained(root, Path.Combine(output, "run.json"), record.ToJsonString(Json));
                throw;
            }
            WriteRetained(root, Path.Combine(output, "run.json"), record.ToJsonString(Json));
        }
        Console.WriteLine($"Grouped {group.Name}: native complete; restored tests healthy; {clock.Elapsed.TotalSeconds:F2}s; peak {peak} bytes.");
        return 0;

        long CurrentOwnedBytes() => CountOwnedScopeBytes(ownedRoot, temporary);

        async Task<ProcessResult> Run(string name, string[] command, string? cwd = null, bool requireSuccess = true, string? executable = null)
        {
            double left = budget - clock.Elapsed.TotalSeconds;
            // Reserve cleanup/postflight time instead of spending the whole pilot budget in native mutation.
            if (name == "native") { left -= 90; }
            Require(left > 0, "The declared group budget is exhausted; stop expansion.");
            long bytes = CurrentOwnedBytes(); peak = Math.Max(peak, bytes);
            long FreeBytes() => Math.Min(ownedVolume.AvailableBytes(), Math.Min(candidateVolume.AvailableBytes(), temporaryVolume.AvailableBytes()));
            Require(bytes <= OwnedLimit && FreeBytes() >= FreeReserve, "Owned scratch or free-space reserve budget failed.");
            Console.WriteLine("Grouped " + group.Name + ": " + name);
            var commandClock = Stopwatch.StartNew();
            using var capacity = new CancellationTokenSource();
            using var monitoring = new CancellationTokenSource();
            Task<Exception?> monitor = MonitorCapacityAsync(CurrentOwnedBytes, FreeBytes,
                bytes => peak = Math.Max(peak, bytes), capacity, monitoring.Token);
            ProcessResult result;
            Exception? processFailure = null;
            try
            {
                result = await ProcessRunner.RunAsync(executable ?? subjectDotnet, command, cwd ?? language, TimeSpan.FromSeconds(left), environment,
                    maximumCapturedBytesPerStream: 65536, allowTruncatedOutput: true, cancellationToken: capacity.Token);
            }
            catch (ProcessTimeoutException error)
            {
                processCleanupHealthy &= error.CleanupCompleted;
                result = error.Result;
                processFailure = error;
            }
            catch (ProcessOutputLimitException error)
            {
                result = error.Result;
                processFailure = error;
            }
            finally
            {
                await monitoring.CancelAsync();
                await monitor;
            }
            Write(Path.Combine(output, name + ".log"), result.StandardOutput + result.StandardError);
            commandLog.Add(new JsonObject
            {
                ["name"] = name,
                ["executable"] = executable ?? subjectDotnet,
                ["arguments"] = JsonSerializer.SerializeToNode(command),
                ["exitCode"] = result.ExitCode,
                ["outputDrained"] = result.OutputDrained,
                ["seconds"] = commandClock.Elapsed.TotalSeconds,
                ["stdoutBytes"] = result.StandardOutputTotalBytes,
                ["stderrBytes"] = result.StandardErrorTotalBytes
            });
            peak = Math.Max(peak, CurrentOwnedBytes());
            if (monitor.Result is Exception capacityFailure) { throw new InvalidDataException("Owned capacity monitor failed; expansion stopped: " + capacityFailure.Message, processFailure ?? capacityFailure); }
            if (processFailure is not null) { throw processFailure; }
            Require(peak <= OwnedLimit && result.OutputDrained, "Owned storage exceeded 2 GiB or process output cleanup failed.");
            Require(!requireSuccess || result.ExitCode == 0, name + " failed; inspect its bounded native diagnostics.");
            return result;

        }

        async Task Tests(string phase)
        {
            foreach (string project in group.Projects)
            {
                string path = Path.GetFullPath(Path.Combine(language, "src/CIS.Core", project));
                string name = phase + "-" + Path.GetFileNameWithoutExtension(path);
                await Run(name, ["test", path, "-c", "Release", "--no-build", "--no-restore", "--filter", group.Filter,
                    "--logger", "trx;LogFileName=" + name + ".trx", "--results-directory", output]);
                PassingTrx(Path.Combine(output, name + ".trx"));
            }
        }
    }

    private static void WriteRetained(string root, string path, string content)
    {
        long existing = File.Exists(path) ? new FileInfo(path).Length : 0;
        Require(OwnedBytes(root, skipWorkspace: true) - existing + Encoding.UTF8.GetByteCount(content) <= RetainedLimit,
            "Aggregate ordinary retained candidate evidence would exceed 256 MiB; stop instead of expanding diagnostics.");
        Require(ResolveStorageVolume(root).AvailableBytes() >= FreeReserve + Encoding.UTF8.GetByteCount(content), "Writing retained evidence would breach the free-space reserve.");
        Write(path, content);
    }

    internal static void ValidateProcessorCapacity(int available) => Require(available >= NativeConcurrency,
        "The declared eight-worker grouped profile requires at least eight available processors; no build or mutation was started.");

    private static bool AtOrUnder(string root, string path)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)); path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        return string.Equals(root, path, comparison) || path.StartsWith(prefix, comparison);
    }

    internal static string ValidateOwnedScope(string candidate, string selected)
    {
        string scope = Path.TrimEndingDirectorySeparator(Path.GetFullPath(selected));
        candidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        Require(Directory.Exists(scope) && Directory.Exists(candidate) && AtOrUnder(scope, candidate), "The owned storage root must be an existing directory containing this candidate.");
        for (string? path = candidate; path is not null; path = Path.GetDirectoryName(path))
        { Require((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0, "The owned storage scope cannot contain linked candidate ancestors."); }
        return scope;
    }

    internal static long CountOwnedScopeBytes(string root, string temporary) => checked(OwnedBytes(root)
        + (!AtOrUnder(root, temporary) && Directory.Exists(temporary) ? OwnedBytes(temporary) : 0));

    internal static StorageVolume ResolveStorageVolume(string path)
    {
        string requested = Path.GetFullPath(path), existing = requested;
        while (true)
        {
            try
            {
                FileAttributes attributes = File.GetAttributes(existing);
                Require((attributes & FileAttributes.Directory) != 0, "Storage query requires a directory: " + existing);
                break;
            }
            catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
            {
                existing = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(existing))
                    ?? throw new InvalidDataException("No existing ancestor is available for the storage query: " + requested, error);
            }
        }
        // Reject alias paths rather than assigning a lexical mount to a different physical volume.
        for (string? ancestor = existing; ancestor is not null; ancestor = Directory.GetParent(ancestor)?.FullName)
        { Require((File.GetAttributes(ancestor) & FileAttributes.ReparsePoint) == 0, "Storage query contains a linked directory: " + ancestor); }
        string mount = MostSpecificMount(existing, DriveInfo.GetDrives().Select(volume => volume.Name));
        var selected = new StorageVolume(requested, existing, mount);
        // On Unix DriveInfo stats the actual existing directory; on Windows it resolves its drive.
        // Query now so missing/unready/failed volumes block before native execution, without falling back to '/'.
        _ = selected.AvailableBytes();
        return selected;
    }

    internal static string MostSpecificMount(string path, IEnumerable<string> mounts)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string? selected = mounts.Select(mount => Path.TrimEndingDirectorySeparator(Path.GetFullPath(mount)))
            .Where(mount => full.Equals(mount, comparison) || full.StartsWith(
                Path.EndsInDirectorySeparator(mount) ? mount : mount + Path.DirectorySeparatorChar, comparison))
            .OrderByDescending(mount => mount.Length).FirstOrDefault();
        Require(selected is not null, "No mounted volume covers storage path: " + full);
        return selected!;
    }

    internal static async Task<Exception?> MonitorCapacityAsync(Func<long> sampleOwned, Func<long> sampleFree, Action<long> observe,
        CancellationTokenSource processCancellation, CancellationToken stop, long maximumOwned = OwnedLimit, long minimumFree = FreeReserve)
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                long bytes = sampleOwned();
                observe(bytes);
                Require(bytes <= maximumOwned && sampleFree() >= minimumFree,
                    "Owned aggregate scratch exceeded its declared budget or the free-space reserve was breached.");
                await Task.Delay(TimeSpan.FromSeconds(1), stop);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
        {
            await processCancellation.CancelAsync();
            return error;
        }
        return null;
    }

    internal static int PassingTrx(string path)
    {
        XDocument document = XDocument.Load(path);
        XElement counters = document.Descendants().Single(item => item.Name.LocalName == "Counters");
        int total = (int?)counters.Attribute("total") ?? 0;
        Require(total > 0 && (int?)counters.Attribute("executed") == total && (int?)counters.Attribute("passed") == total,
            "Native baseline/postflight is empty, failed or skipped.");
        Require(document.Descendants().Any(item => item.Name.LocalName == "StdOut" && item.Value.Contains("CIS mutation scheduling: serialized", StringComparison.Ordinal)), "Missing actual serialized scheduling control.");
        return total;
    }

    private static void VerifyInputs(string workspace, Candidate candidate)
    {
        Require(Capture(workspace).SequenceEqual(candidate.Inputs), "Candidate source/test/build/configuration inputs changed.");
    }

    private static long OwnedBytes(string root, bool skipWorkspace = false)
    {
        long bytes = 0;
        Walk(root);
        return bytes;
        void Walk(string directory)
        {
            try
            {
                Require((File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0, "Owned path contains a directory link.");
                foreach (string file in Directory.EnumerateFiles(directory))
                {
                    try { bytes = checked(bytes + new FileInfo(file).Length); }
                    catch (FileNotFoundException) { }
                }
                foreach (string child in Directory.EnumerateDirectories(directory))
                {
                    if (!skipWorkspace || child != Path.Combine(root, "workspace")) { Walk(child); }
                }
            }
            catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { } // Native build scratch may disappear between samples.
        }
    }

    private static void DeleteOwned(string path, string root)
    {
        string full = Path.GetFullPath(path), prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        Require(full.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal), "Cleanup target escaped this owned candidate.");
        Require((File.GetAttributes(full) & FileAttributes.ReparsePoint) == 0, "Cleanup target is a link.");
        if (Directory.Exists(full))
        {
            NoLinks(full);
        }
        if (Directory.Exists(full)) { Directory.Delete(full, recursive: true); }
        else { File.Delete(full); }

        static void NoLinks(string directory)
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                Require((File.GetAttributes(entry) & FileAttributes.ReparsePoint) == 0, "Owned cleanup contains a linked descendant.");
                if (Directory.Exists(entry)) { NoLinks(entry); }
            }
        }
    }
}
