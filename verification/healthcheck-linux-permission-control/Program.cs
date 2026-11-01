using System.Collections;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.TestSupport;

return await PermissionControl.RunAsync(args);

internal static class PermissionControl
{
    private const string CheckerHash = "0bbadb285a453a098fb33a6635b8f5cefe4f45368ab40509e557d267689d7fbe";
    private const string HelperHash = "a84ca81bf306eae633e42c679ea62445b3fd2cd5a1cc585640049e0059305154";
    private const long Limit = 2L * 1024 * 1024 * 1024, RetainedLimit = 256L * 1024 * 1024;
    private const string TestPrefix = "CIS.Tests.UnitTests.Healthcheck.";
    private const string FirstTest = TestPrefix + "HealthPipeInternalMutationQualificationTests.FirstInstanceProtectionRejectsAnExistingEndpointEvenWhenItHasSpareCapacity";
    private const string DuplexTest = TestPrefix + "HealthPipePublicLifecycleBoundaryTests.ActualFirstAndAdditionalEndpointsSupportAsynchronousDuplexTransport";
    private const string ClientTest = TestPrefix + "HealthPipeForeignOwnerBoundaryTests.CurrentUserOnlyRejectsAnAccessibleForeignOwnerBeforeSendingARequest";
    private const string ServerTest = TestPrefix + "HealthPipeForeignOwnerBoundaryTests.ForeignPeerRejectionPreservesTheActualOwnersAdmission";
    private const string SchedulingTest = "CIS.Tests.TestAssemblySetup.SchedulingMatchesRequestedMode";
    private static readonly string[] TestNames = [FirstTest, DuplexTest, ClientTest, ServerTest, SchedulingTest];
    private static readonly string[] Sources = ["Healthcheck/Pipes/HealthPipeEndpoint.cs", "Healthcheck/Pipes/HealthPipeServer.cs", "Healthcheck/Pipes/HealthProbeClient.cs"];
    private static readonly BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private sealed record Pin(string Path, string Sha256, long Bytes);
    private sealed record Requirement(string Obligation, string Source, string Operator, JsonObject Location, string Replacement, string DefinitionIdentity, string[] WitnessNames);

    internal static async Task<int> RunAsync(string[] args)
    {
        var clock = Stopwatch.StartNew();
        var options = new Dictionary<string, string>(StringComparer.Ordinal); var external = new List<string>();
        for (int index = 0; index < args.Length; index += 2)
        {
            Need(index + 1 < args.Length, "Use explicit --name value pairs.");
            if (args[index] == "--external-owned-root") { external.Add(Full(args[index + 1])); }
            else { Need(options.TryAdd(args[index], args[index + 1]), "Duplicate control option."); }
        }
        string[] required = ["--candidate-root", "--task-root", "--checker", "--operator-source-root", "--output-root", "--temporary-root", "--operator-build-receipt"];
        Need(required.All(options.ContainsKey) && options.Keys.All(required.Contains), "Missing or unknown control option.");
        string root = Full(options["--candidate-root"]), taskRoot = Full(options["--task-root"]), output = Full(options["--output-root"]);
        string sourceRoot = Full(options["--operator-source-root"]), checker = Full(options["--checker"]), temporary = Full(options["--temporary-root"]);
        string buildReceiptPath = Full(options["--operator-build-receipt"]);
        Need(OperatingSystem.IsLinux() && Native.getuid() == 0 && Native.geteuid() == 0 && RuntimeInformation.FrameworkDescription == ".NET 10.0.12",
            "The actual Linux UID0/effectiveUID0/private subject runtime10.0.12 are required.");
        Need(Under(taskRoot, root) && output == Path.Combine(root, "groups/pipes") && !Directory.Exists(output) && !File.Exists(output), "Use a fresh supplemental pipes output inside the separately prepared candidate.");
        Need(!Under(root, sourceRoot) && !Under(root, temporary) && !Directory.Exists(temporary) && !File.Exists(temporary) && temporary.Length <= 65,
            "Use a separate pinned operator source and fresh short owned temporary directory.");
        string helper = Path.Combine(Path.GetDirectoryName(checker)!, "CIS.Core.TestSupport.dll");
        Need(Hash(checker) == CheckerHash && Hash(helper) == HelperHash && Hash(typeof(ProcessRunner).Assembly.Location) == HelperHash,
            "The original frozen guard and the relocated executed helper must retain their exact qualified bytes.");
        Assembly frozen = AssemblyLoadContext.Default.LoadFromAssemblyPath(checker);
        Type grouped = frozen.GetType("CIS.Verification.HealthcheckGroupedMutation", true)!;
        object candidate = Call(grouped, "CandidateAt", root)!;
        object pipes = ((IEnumerable)Call(grouped, "Groups", candidate)!).Cast<object>().Single(value => Text(value, "Name") == "pipes");
        object tools = Call(grouped, "ReadToolchain", root, candidate)!;
        string workspace = Path.Combine(root, "workspace"), language = Path.Combine(workspace, "implementations/csharp");
        JsonObject candidateJson = Read(Path.Combine(root, "candidate.json")), toolchainJson = Read(Path.Combine(root, "toolchain.json"));
        string actualPlatform = RuntimeInformation.OSDescription + "; " + RuntimeInformation.ProcessArchitecture + "; " + RuntimeInformation.FrameworkDescription;
        Need(actualPlatform == (string?)candidateJson["platform"] && Environment.MachineName == (string?)candidateJson["host"], "The actual operator platform/host differs from its frozen native candidate.");
        string dotnet = (string)toolchainJson["subjectDotnet"]!, cli = toolchainJson["files"]!.AsArray().Single(pin => Path.GetFileName((string)pin!["path"]!) == "Stryker.CLI.dll")!["path"]!.GetValue<string>();
        Need((string?)toolchainJson["toolDotnet"] == dotnet && Same(Environment.ProcessPath!, dotnet), "Use the frozen shared private dotnet host.");
        Call(grouped, "ValidateOwnedScope", root, taskRoot); Call(grouped, "ValidatePilot", root, candidate); Call(grouped, "VerifyInputs", workspace, candidate);
        string commonInputs = (string)Call(grouped, "CommonInputs", candidate)!;
        string sdkMsBuild = (string)Call(grouped, "SdkMsBuildPath", tools)!;
        using var lease = new FileStream(Path.Combine(root, "active.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var pins = toolchainJson["files"]!.AsArray().Select(pin => Pinned((string)pin!["path"]!)).ToList();
        pins.AddRange(new[] { "candidate.json", "candidate.zip", "toolchain.json" }.Select(name => Pinned(Path.Combine(root, name))));
        pins.AddRange(new[] { "Program.cs", "LinuxPermissionControl.csproj", "requirements.json", "README.md" }.Select(name => Pinned(Path.Combine(sourceRoot, name))));
        string assembly = typeof(PermissionControl).Assembly.Location;
        pins.AddRange(new[] { assembly, Path.ChangeExtension(assembly, ".deps.json"), Path.ChangeExtension(assembly, ".runtimeconfig.json"), Path.ChangeExtension(assembly, ".pdb"), typeof(ProcessRunner).Assembly.Location, buildReceiptPath }.Select(Pinned));
        string requirementsPath = Path.Combine(sourceRoot, "requirements.json"), copiedRequirements = Path.Combine(AppContext.BaseDirectory, "requirements.json");
        Need(Hash(requirementsPath) == Hash(copiedRequirements), "The deployed control recipe differs from its compiled source inputs."); pins.Add(Pinned(copiedRequirements));
        ValidateBuildReceipt(buildReceiptPath, sourceRoot, assembly);
        AssertPins();
        Need(external.All(Directory.Exists), "Every declared external owned root must exist.");
        string[] roots = new[] { taskRoot }.Concat(external).Append(temporary).Distinct(StringComparer.Ordinal).ToArray();
        roots = roots.Where(path => !roots.Any(other => path != other && Under(other, path))).ToArray();
        Need(roots.Length == 27, "Retain the actual27-root owned scope; nest the new short TMP under an existing counted root.");
        object[] volumes = roots.Select(path => Call(grouped, "ResolveStorageVolume", path)!).ToArray();
        long peak = 0;
        long Bytes(string path) => Directory.Exists(path) ? (long)Call(grouped, "CountOwnedScopeBytes", path, path)! : 0;
        long Aggregate() => roots.Sum(Bytes);
        long Free() => volumes.Min(volume => (long)Invoke(volume.GetType().GetMethod("AvailableBytes", Instance)!, volume, [])!);
        Capacity();
        string home = Path.Combine(root, "dotnet-home");
        Need(!Directory.Exists(home) && !File.Exists(home), "Use a fresh task-owned root CLI home for this one control transaction.");
        var environment = new Dictionary<string, string?> {
            ["DOTNET_ROOT"] = Path.GetDirectoryName(dotnet), ["DOTNET_ROOT_X64"] = Path.GetDirectoryName(dotnet), ["DOTNET_HOST_PATH"] = dotnet,
            ["DOTNET_CLI_HOME"] = home, ["DOTNET_MULTILEVEL_LOOKUP"] = "0", ["DOTNET_ROLL_FORWARD"] = null,
            ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1", ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1", ["DOTNET_GENERATE_ASPNET_CERTIFICATE"] = "false",
            ["UseSharedCompilation"] = "false", ["MSBUILDDISABLENODEREUSE"] = "1", ["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0", ["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] = "1",
            ["MSBUILD_EXE_PATH"] = sdkMsBuild, ["MSBuildSDKsPath"] = null, ["PATH"] = Path.GetDirectoryName(dotnet) + ":" + (Environment.GetEnvironmentVariable("PATH") ?? ""),
            ["TEMP"] = temporary, ["TMP"] = temporary, ["TMPDIR"] = temporary, ["RestoreLockedMode"] = "true",
            ["CIS_HEALTHCHECK_MUTATION_SESSION"] = null, ["CIS_HEALTHCHECK_IDENTITY_MANIFEST"] = null,
            ["CisMutationTesting"] = "true", ["CisHealthcheckMutationCohort"] = "true", ["CisHealthcheckPilotCohort"] = "false", ["CisLinuxIdentityQualification"] = "true" };
        JsonObject buildProperties = new() { ["CisMutationTesting"] = "true", ["CisHealthcheckMutationCohort"] = "true", ["CisHealthcheckPilotCohort"] = "false", ["CisLinuxIdentityQualification"] = "true" };
        Call(grouped, "ValidateSdkMsBuildEnvironment", new JsonObject { ["environment"] = JsonSerializer.SerializeToNode(environment, Json) }, tools);
        string unit = Path.Combine(language, "tests/CIS.Core.UnitTests/CIS.Core.UnitTests.csproj");
        string integration = Path.Combine(language, "tests/CIS.Core.IntegrationTests/CIS.Core.IntegrationTests.csproj");
        string[] projects = [unit, integration];
        string filter = string.Join('|', TestNames.Select(name => "FullyQualifiedName=" + name));
        (string[] selectors, Requirement[] requirements) = Selection(language, requirementsPath);
        string config = Path.Combine(output, "stryker-config.json"), nativeOutput = Path.Combine(output, "native");
        var commands = new List<object>(); var failures = new List<string>(); var baselinePins = new List<Pin>();
        JsonObject? binding = null, journals = null; ProcessResult? native = null;
        bool restored = false, cleanupHealthy = true, identityValidated = false; long retained = 0; JsonObject[] claims = [];
        Directory.CreateDirectory(output);
        try
        {
            Directory.CreateDirectory(home); Directory.CreateDirectory(temporary);
            Write(Path.Combine(temporary, "owner.json"), new { candidateIdentity = (string?)candidateJson["identity"], group = "pipes", supplementalControl = output });
            Write(config, new Dictionary<string, object> { ["stryker-config"] = new Dictionary<string, object> {
                ["project"] = "CIS.Core.csproj", ["test-projects"] = new[] { "../../tests/CIS.Core.UnitTests/CIS.Core.UnitTests.csproj" }, ["test-case-filter"] = filter,
                ["mutate"] = selectors, ["mutation-level"] = "Complete", ["test-runner"] = "vstest", ["target-framework"] = "net10.0", ["configuration"] = "Release",
                ["coverage-analysis"] = "perTest", ["disable-bail"] = true, ["concurrency"] = 2, ["additional-timeout"] = 10000,
                ["reporters"] = new[] { "json", "progress", "cleartext" }, ["thresholds"] = new { high = 100, low = 90, @break = 0 } } });
            pins.Add(Pinned(config));
            foreach (string project in projects)
            {
                await Run("restore-" + Path.GetFileNameWithoutExtension(project), ["restore", project, "--locked-mode", "-m:1", "-nr:false"]);
                await Run("build-" + Path.GetFileNameWithoutExtension(project), ["build", project, "-c", "Release", "--no-restore", "-m:1", "-nr:false"]);
            }
            // Bind the actual host/SDK/runtime and both selected adapter closures.
            // The executed helper is deliberately relocated, and is pinned separately.
            await ObserveTools(); VerifyAdapters();
            binding = (JsonObject?)await Await(Call(grouped, "PrepareLinuxIdentityAsync", root, candidate, pipes, language, output, temporary, dotnet,
                environment, (Func<string, string[], string, string, Task<ProcessResult>>)IdentityRun)!);
            Need(binding is not null, "The actual root/nobody identity context was not prepared.");
            baselinePins.AddRange(BinaryPaths().Select(Pinned)); VerifyCoreCopies();
            await Tests("baseline"); journals = (JsonObject?)Call(grouped, "ValidateLinuxIdentityJournals", root, candidate, pipes, binding); identityValidated = true;
            identityValidated = false;
            native = await Run("native", ["exec", "--fx-version", "8.0.31", "--roll-forward", "Disable", cli,
                "--config-file", config, "--output", nativeOutput, "--skip-version-check"], reserve: 90, nativeCeiling: true, cwd: Path.Combine(language, "src/CIS.Core"));
            journals = (JsonObject?)Call(grouped, "ValidateLinuxIdentityJournals", root, candidate, pipes, binding); identityValidated = true;
        }
        catch (Exception error) { failures.Add(error.ToString()); }
        finally
        {
            try
            {
                identityValidated = false;
                Call(grouped, "VerifyInputs", workspace, candidate); AssertPins();
                for (int index = 0; index < projects.Length; index++)
                {
                    string[] command = ["build", projects[index], "-c", "Release", "--no-restore", "--no-incremental", "-m:1", "-nr:false"];
                    if (index > 0) { command = [.. command, "-p:BuildProjectReferences=false"]; }
                    await Run("restored-build-" + Path.GetFileNameWithoutExtension(projects[index]), command, reserve: 0);
                }
                VerifyCoreCopies(); VerifyAdapters();
                Need(baselinePins.Count > 0 && baselinePins.All(pin => Hash(pin.Path) == pin.Sha256), "Restored actual product/test/child bytes differ from their clean prepared baseline.");
                await Tests("restored"); journals = (JsonObject?)Call(grouped, "ValidateLinuxIdentityJournals", root, candidate, pipes, binding); identityValidated = true;
                restored = true;
            }
            catch (Exception error) { failures.Add("Restoration/postflight: " + error); }
        }
        try
        {
            Need(native?.ExitCode == 0 && native.OutputDrained && restored && failures.Count == 0, "All native, restoration and all-attempt journal prerequisites must pass before claims.");
            string reportPath = Path.Combine(nativeOutput, "reports/mutation-report.json");
            JsonObject report = Read(reportPath); object inputs = candidate.GetType().GetProperty("Inputs", Instance)!.GetValue(candidate)!;
            object definitions = Call(grouped, "Definitions", report, Sources, inputs)!;
            _ = Call(grouped, "ValidateLinuxIdentityControl", definitions, report, (Func<string, string>)ReadSource);
            _ = Call(grouped, "ValidateLinuxServerRecoveryControl", definitions, report, (Func<string, string>)ReadSource);
            string NormalizeTestPath(string path) => (string)Call(grouped, "LanguageReportPath", path)!;
            foreach ((string path, JsonNode? testFile) in report["testFiles"]!.AsObject())
            { Need((string?)testFile!["source"] == ReadSource("implementations/csharp/" + NormalizeTestPath(path)), "Actual native test-file source differs from the captured candidate."); }
            claims = Claims(((IEnumerable)definitions).Cast<object>().ToArray(), report, requirements, ReadSource, NormalizeTestPath,
                TrxCases(Path.Combine(output, "baseline.trx")), TrxCases(Path.Combine(output, "restored.trx")));
        }
        catch (Exception error) { failures.Add("Exact six native control claims: " + error); }
        try { AssertPins(); Call(grouped, "VerifyInputs", workspace, candidate); _ = Call(grouped, "ReadToolchain", root, candidate); }
        catch (Exception error) { failures.Add("Final inputs: " + error); }
        try
        {
            Need(cleanupHealthy && identityValidated && journals is not null, "An unhealthy process/journal cleanup must retain its marked short TMP.");
            Call(grouped, "DeleteOwned", temporary, Path.GetDirectoryName(temporary)!);
        }
        catch (Exception error) { failures.Add("Owned TMP cleanup: " + error); }
        try { retained = (long)Call(grouped, "OwnedBytes", root, true)!; Need(retained + 262144 <= RetainedLimit, "Retained supplemental evidence exceeds256MiB including manifest allowance."); Capacity(); }
        catch (Exception error) { failures.Add("Retained/aggregate capacity: " + error); }
        bool complete = native?.ExitCode == 0 && restored && cleanupHealthy && claims.Length == 6 && failures.Count == 0 && clock.Elapsed.TotalSeconds <= 600;
        Write(Path.Combine(output, "run-metadata.json"), new { schema = "cis.healthcheck.linux-permission-control-run/v1", complete, commands, failures,
            nativeExitCode = native?.ExitCode, nativeOutputDrained = native?.OutputDrained, restored, cleanupHealthy, temporaryRemoved = !Directory.Exists(temporary),
            budgetSeconds = 600, restorationReserveSeconds = 90, nativeCeilingSeconds = 450, seconds = clock.Elapsed.TotalSeconds, peakOwnedBytes = peak, roots, retainedBytes = retained });
        string[] retainedFiles = pins.Select(pin => pin.Path).Concat(Directory.GetFiles(output, "*", SearchOption.AllDirectories))
            .Concat(baselinePins.Select(pin => pin.Path)).Distinct(StringComparer.Ordinal).ToArray();
        Write(Path.Combine(output, "manifest.json"), new { schema = "cis.healthcheck.linux-permission-native-proof/v1", complete,
            candidateIdentity = (string?)candidateJson["identity"], candidateRoot = root, configurationPath = config, reportPath = Path.Combine(nativeOutput, "reports/mutation-report.json"),
            baselineTrxPath = Path.Combine(output, "baseline.trx"), restoredTrxPath = Path.Combine(output, "restored.trx"), sources = Sources, commandLog = commands,
            originalCommonInputs = commonInputs, platform = actualPlatform, host = Environment.MachineName,
            actualExecutionIdentity = new { operatingSystem = "linux", uid = Native.getuid(), effectiveUid = Native.geteuid(), framework = RuntimeInformation.FrameworkDescription, host = Environment.MachineName, processPath = Environment.ProcessPath },
            buildProperties, environment, selectors, filter, nativeExitCode = native?.ExitCode, outputDrained = native?.OutputDrained, nativeOutputDrained = native?.OutputDrained, restored, cleanupHealthy, failures,
            budgetSeconds = 600, restorationReserveSeconds = 90, nativeCeilingSeconds = 450, seconds = clock.Elapsed.TotalSeconds,
            checker = Pinned(checker), originalHelper = Pinned(helper), executedRelocatedHelper = Pinned(typeof(ProcessRunner).Assembly.Location),
            operatorBuildReceiptPath = buildReceiptPath, operatorBuildReceiptSha256 = Hash(buildReceiptPath),
            requirementsPath, requirementsSha256 = Hash(requirementsPath),
            files = retainedFiles.Select(Pinned).ToArray(), claims, linuxIdentity = journals,
            noModuleMutationPopulationOrScoreCredit = true, frozenNativeCandidateSourceCount = candidateJson["inputs"]!.AsArray().Count });
        Console.WriteLine("Linux permission control complete=" + complete + "; " + clock.Elapsed.TotalSeconds.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "s; " + output);
        return complete ? 0 : 1;

        string ReadSource(string relative) => File.ReadAllText(Path.Combine(workspace, relative));
        void AssertPins() { foreach (Pin pin in pins) { Need(File.Exists(pin.Path) && Hash(pin.Path) == pin.Sha256 && new FileInfo(pin.Path).Length == pin.Bytes, "Pinned supplemental/native input changed: " + pin.Path); } }
        void Capacity() { long size = Aggregate(); peak = Math.Max(peak, size); Need(size <= Limit && Free() >= Limit, "Actual all27-root capacity/free reserve failed."); }
        async Task<ProcessResult> IdentityRun(string name, string[] command, string cwd, string executable) => await Run(name, command, reserve: 90, executable: executable, cwd: cwd);
        async Task<ProcessResult> Run(string name, string[] command, int reserve = 90, bool nativeCeiling = false, string? executable = null, string? cwd = null)
        {
            double left = 600 - clock.Elapsed.TotalSeconds - reserve; if (nativeCeiling) { left = Math.Min(left, 450); }
            Need(left > 0, "The fixed total control deadline is exhausted before " + name); Capacity();
            using var cancel = new CancellationTokenSource(); using var stop = new CancellationTokenSource();
            Task<Exception?> monitoring = (Task<Exception?>)Call(grouped, "MonitorCapacityAsync", (Func<long>)Aggregate, (Func<long>)Free,
                (Action<long>)(bytes => peak = Math.Max(peak, bytes)), cancel, stop.Token, Limit, Limit)!;
            ProcessResult? result = null; Exception? failure = null; var own = Stopwatch.StartNew();
            try { result = await ProcessRunner.RunAsync(executable ?? dotnet, command, cwd ?? language, TimeSpan.FromSeconds(left), environment, maximumCapturedBytesPerStream: 65536, cancellationToken: cancel.Token); }
            catch (ProcessTimeoutException error) { result = error.Result; cleanupHealthy &= error.CleanupCompleted; failure = error; }
            catch (ProcessOutputLimitException error) { result = error.Result; failure = error; }
            catch (Exception error) { failure = error; }
            finally { await stop.CancelAsync(); await monitoring; }
            if (result is not null)
            {
                File.WriteAllText(Path.Combine(output, name + ".stdout.log"), result.StandardOutput); File.WriteAllText(Path.Combine(output, name + ".stderr.log"), result.StandardError);
                cleanupHealthy &= result.OutputDrained;
                commands.Add(new { name, executable = executable ?? dotnet, arguments = command, cwd = cwd ?? language, seconds = own.Elapsed.TotalSeconds,
                    timeoutSeconds = left, exitCode = result.ExitCode, outputDrained = result.OutputDrained, result.ProcessId, result.StandardOutputTotalBytes, result.StandardErrorTotalBytes });
            }
            if (monitoring.Result is Exception capacity) { throw new InvalidDataException("The existing capacity monitor stopped " + name, capacity); }
            if (failure is not null) { ExceptionDispatchInfo.Capture(failure).Throw(); }
            Need(result is not null && result.ExitCode == 0 && result.OutputDrained, name + " failed or did not drain."); return result!;
        }
        async Task Tests(string phase)
        {
            await Run(phase, ["test", unit, "-c", "Release", "--no-build", "--no-restore", "-m:1", "--filter", filter, "--logger", "trx;LogFileName=" + phase + ".trx", "--results-directory", output], reserve: phase == "restored" ? 0 : 90);
            Need((int)Call(grouped, "PassingTrx", Path.Combine(output, phase + ".trx"))! == 5, "Exactly the five selected Unit native cases must pass without skip.");
            VerifyTrx(Path.Combine(output, phase + ".trx"), TestNames, Path.Combine(Path.GetDirectoryName(unit)!, "bin/Release/net10.0/CIS.Core.UnitTests.dll"));
        }
        IEnumerable<string> BinaryPaths()
        {
            foreach (string directory in new[] { "src/CIS.Core", "tests/CIS.Core.UnitTests", "tests/CIS.Core.IntegrationTests", "tests/fixtures/HealthcheckHttpHost", "tools/CisHealthProbe" })
            {
                string bin = Path.Combine(language, directory, "bin/Release/net10.0");
                foreach (string file in Directory.GetFiles(bin).Where(path => Path.GetExtension(path) is ".dll" or ".pdb" or ".json")) { yield return file; }
            }
            foreach (string directory in new[] { "healthcheck-http-host", "healthcheck-probe" })
            { foreach (string file in Directory.GetFiles(Path.Combine(language, "tests/CIS.Core.IntegrationTests/bin/Release/net10.0", directory))) { yield return file; } }
        }
        void VerifyCoreCopies()
        {
            string hash = Hash(Path.Combine(language, "src/CIS.Core/bin/Release/net10.0/CIS.Core.dll"));
            foreach (string directory in new[] { "tests/CIS.Core.UnitTests", "tests/CIS.Core.IntegrationTests", "tests/fixtures/HealthcheckHttpHost", "tools/CisHealthProbe" })
            { Need(Hash(Path.Combine(language, directory, "bin/Release/net10.0/CIS.Core.dll")) == hash, "An actual restored test/helper copy differs from clean Core."); }
            foreach (string name in new[] { "healthcheck-http-host", "healthcheck-probe" })
            { Need(Hash(Path.Combine(language, "tests/CIS.Core.IntegrationTests/bin/Release/net10.0", name, "CIS.Core.dll")) == hash, "An Integration child copy remains different/instrumented."); }
        }
        void VerifyAdapters()
        {
            foreach (string project in projects)
            { foreach (JsonNode? pin in toolchainJson["adapters"]!.AsArray()) { Need(Hash(Path.Combine(Path.GetDirectoryName(project)!, "bin/Release/net10.0", (string)pin!["path"]!)) == (string?)pin["sha256"], "Actual Unit/Integration adapter bytes differ from the frozen pilot table."); } }
        }
        async Task ObserveTools()
        {
            ProcessResult sdks = await Run("observe-sdks", ["--list-sdks"]);
            ProcessResult runtimes = await Run("observe-runtimes", ["--list-runtimes"]);
            string[] coreRuntimes = runtimes.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(line => line.StartsWith("Microsoft.NETCore.App ", StringComparison.Ordinal)).ToArray();
            Need(coreRuntimes.Length == 2 && coreRuntimes.All(line => line.StartsWith("Microsoft.NETCore.App 10.0.12 ", StringComparison.Ordinal)
                    || line.StartsWith("Microsoft.NETCore.App 8.0.31 ", StringComparison.Ordinal))
                && sdks.StandardOutput.Contains("10.0.401 [" + Path.Combine(Path.GetDirectoryName(dotnet)!, "sdk") + "]", StringComparison.Ordinal)
                && runtimes.StandardOutput.Contains("Microsoft.NETCore.App 10.0.12 [" + Path.Combine(Path.GetDirectoryName(dotnet)!, "shared/Microsoft.NETCore.App") + "]", StringComparison.Ordinal)
                && runtimes.StandardOutput.Contains("Microsoft.NETCore.App 8.0.31 [" + Path.Combine(Path.GetDirectoryName(dotnet)!, "shared/Microsoft.NETCore.App") + "]", StringComparison.Ordinal)
                && runtimes.StandardOutput.Contains("Microsoft.AspNetCore.App 10.0.12 [" + Path.Combine(Path.GetDirectoryName(dotnet)!, "shared/Microsoft.AspNetCore.App") + "]", StringComparison.Ordinal),
                "The actual shared private SDK/tool/subject installations differ from the pinned toolchain.");
            _ = Call(grouped, "ReadToolchain", root, candidate);
        }
    }

    private static (string[], Requirement[]) Selection(string language, string requirementsPath)
    {
        string endpoint = File.ReadAllText(Path.Combine(language, "src/CIS.Core", Sources[0]));
        string server = File.ReadAllText(Path.Combine(language, "src/CIS.Core", Sources[1]));
        string client = File.ReadAllText(Path.Combine(language, "src/CIS.Core", Sources[2]));
        const string mask = "PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly", branch = "(first ? PipeOptions.FirstPipeInstance : PipeOptions.None)";
        string whole = mask + " | " + branch;
        int e = Unique(endpoint, whole), p = Unique(client, mask), c = Unique(server, "catch (UnauthorizedAccessException)");
        int s = server.IndexOf("continue;", c, StringComparison.Ordinal); Need(s > c && s < server.IndexOf('}', c), "The actual foreign-owner catch/continue changed.");
        string[] selectors = [Sources[0] + "{" + e + ".." + (e + whole.Length) + "}", Sources[1] + "{" + s + ".." + (s + 9) + "}", Sources[2] + "{" + p + ".." + (p + mask.Length) + "}"];
        JsonObject recipe = Read(requirementsPath);
        Need((string?)recipe["schema"] == "cis.healthcheck.linux-permission-requirements/v1" && recipe["obligations"] is JsonArray { Count: 6 }, "The pinned six-control recipe is malformed.");
        var selected = new Dictionary<string, (string Text, int Start, int Length)>(StringComparer.Ordinal) {
            [Sources[0]] = (endpoint, e, whole.Length), [Sources[1]] = (server, s, 9), [Sources[2]] = (client, p, mask.Length) };
        var result = new List<Requirement>();
        foreach (JsonNode? item in recipe["obligations"]!.AsArray())
        {
            string source = (string)item!["source"]!; Need(selected.TryGetValue(source, out var scope), "A recipe obligation escaped the three source selectors.");
            Need(Hash(Path.Combine(language, "src/CIS.Core", source)) == (string?)item["sourceSha256"], "A recipe source changed.");
            int relative = (int)item["relativeStart"]!, length = (int)item["length"]!;
            Need(relative >= 0 && length > 0 && relative + length <= scope.Length && scope.Text.Substring(scope.Start + relative, length) == (string?)item["original"], "A recipe span escaped its actual selector or changed source bytes.");
            string[] witnesses = item["witnessNames"]!.Deserialize<string[]>(Json)!;
            Need(witnesses.Length > 0 && witnesses.All(TestNames.Contains), "A recipe witness escaped the five physical native cases.");
            result.Add(new((string)item["obligation"]!, source, (string)item["operator"]!, Location(scope.Text, scope.Start + relative, length), (string)item["replacement"]!, (string)item["definitionIdentity"]!, witnesses));
        }
        Need(result.Select(item => item.Obligation).Distinct(StringComparer.Ordinal).Count() == 6, "The recipe duplicates a control obligation.");
        return (selectors, result.ToArray());
    }

    private static JsonObject[] Claims(object[] definitions, JsonObject report, Requirement[] requirements, Func<string, string> readSource,
        Func<string, string> normalizeTestPath, Dictionary<string, string> baselineCases, Dictionary<string, string> restoredCases)
    {
        Need(!definitions.Any(definition => Text(definition, "Status") is "RuntimeError" or "Pending"), "The bounded native population contains an incomplete/runtime-error outcome.");
        Need(definitions.Count(definition => Text(definition, "Status") is "Killed" or "Survived" or "Timeout" or "NoCoverage") == 6, "The selected three source spans must have exactly six eligible native definitions.");
        var claims = new List<JsonObject>();
        foreach (Requirement requirement in requirements)
        {
            object[] matches = definitions.Where(definition => Text(definition, "Source") == requirement.Source && Text(definition, "Operator") == requirement.Operator
                && Text(definition, "Replacement") == requirement.Replacement && JsonNode.DeepEquals(JsonNode.Parse(Text(definition, "Location")), requirement.Location)).ToArray();
            Need(matches.Length == 1 && Text(matches[0], "Identity") == requirement.DefinitionIdentity && Text(matches[0], "Status") == "Killed", "A required exact Unix tuple was not genuinely native Killed: " + requirement.Obligation);
            object definition = matches[0];
            JsonNode file = report["files"]!.AsObject().Single(entry => entry.Key.Replace('\\', '/').EndsWith("/" + requirement.Source, StringComparison.Ordinal) || entry.Key == requirement.Source).Value!;
            JsonNode raw = file["mutants"]!.AsArray().Single(item => (string?)item!["id"] == Text(definition, "NativeId"))!;
            string[] killed = raw["killedBy"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();
            Need(killed.Length > 0 && killed.Distinct(StringComparer.Ordinal).Count() == killed.Length, "The actual native killer mapping is empty/ambiguous.");
            var witnesses = report["testFiles"]!.AsObject().SelectMany(entry => entry.Value!["tests"]!.AsArray().Select(test => (File: entry, Test: test!)))
                .Where(item => killed.Contains((string?)item.Test["id"], StringComparer.Ordinal) && requirement.WitnessNames.Contains((string?)item.Test["name"], StringComparer.Ordinal)).ToArray();
            Need(witnesses.Length > 0, "The native definition lacks its actual physical ownership witness: " + requirement.Obligation);
            foreach (var witness in witnesses)
            {
                string relative = normalizeTestPath(witness.File.Key), id = (string)witness.Test["id"]!, name = (string)witness.Test["name"]!;
                Need((string?)witness.File.Value!["source"] == readSource("implementations/csharp/" + relative)
                    && report["testFiles"]!.AsObject().SelectMany(entry => entry.Value!["tests"]!.AsArray()).Count(test => (string?)test!["id"] == id) == 1
                    && baselineCases.TryGetValue(id, out string? before) && before == name && restoredCases.TryGetValue(id, out string? after) && after == name,
                    "A native witness differs from current source, is ambiguous, or lacks the same UUID/FQName in both passing native TRXs.");
            }
            var chosen = witnesses[0]; string witnessSource = normalizeTestPath(chosen.File.Key);
            claims.Add(new JsonObject { ["obligation"] = requirement.Obligation, ["requirement"] = "HC-C08/" + requirement.Obligation, ["definitionIdentity"] = Text(definition, "Identity"), ["source"] = requirement.Source,
                ["sourceSha256"] = Text(definition, "SourceSha256"), ["operator"] = requirement.Operator, ["location"] = requirement.Location.DeepClone(), ["replacement"] = requirement.Replacement,
                ["status"] = "Killed", ["nativeId"] = Text(definition, "NativeId"), ["witnessSource"] = witnessSource, ["witnessName"] = (string?)chosen.Test["name"],
                ["killedBy"] = new JsonArray(witnesses.Select(item => item.Test["id"]!.DeepClone()).ToArray()),
                ["witnesses"] = new JsonArray(witnesses.Select(item => (JsonNode)new JsonObject { ["id"] = (string?)item.Test["id"], ["source"] = normalizeTestPath(item.File.Key), ["name"] = (string?)item.Test["name"] }).ToArray()) });
        }
        return claims.ToArray();
    }

    private static void VerifyTrx(string path, string[] names, string assembly)
    {
        var document = System.Xml.Linq.XDocument.Load(path); var ns = document.Root!.Name.Namespace;
        var definitions = document.Descendants(ns + "UnitTest").ToArray();
        Need(definitions.Length == names.Length && definitions.Select(test => (string?)test.Attribute("id")).Distinct(StringComparer.Ordinal).Count() == names.Length, "Native TRX case identity population differs from the exact five-case recipe.");
        foreach (string name in names)
        {
            var matches = definitions.Where(test => { var method = test.Element(ns + "TestMethod")!; return (string?)method.Attribute("className") + "." + (string?)method.Attribute("name") == name; }).ToArray();
            Need(matches.Length == 1 && Same((string)matches[0].Element(ns + "TestMethod")!.Attribute("codeBase")!, assembly), "The actual native TRX FQName/assembly source differs from the selected Unit case.");
        }
    }
    private static Dictionary<string, string> TrxCases(string path)
    {
        var document = System.Xml.Linq.XDocument.Load(path); var ns = document.Root!.Name.Namespace;
        return document.Descendants(ns + "UnitTest").ToDictionary(test => (string)test.Attribute("id")!, test => {
            var method = test.Element(ns + "TestMethod")!; return (string?)method.Attribute("className") + "." + (string?)method.Attribute("name"); }, StringComparer.Ordinal);
    }
    private static void ValidateBuildReceipt(string path, string sourceRoot, string assembly)
    {
        JsonObject receipt = Read(path);
        Need((string?)receipt["schema"] == "cis.healthcheck.linux-permission-operator-build/v1" && (string?)receipt["compilerPlatform"] == "windows-x64"
            && (string?)receipt["sdk"] == "10.0.401" && (string?)receipt["actualCompileHostRuntime"] == "10.0.12"
            && (int?)receipt["exitCode"] == 0 && (bool?)receipt["outputDrained"] == true && (bool?)receipt["privateRuntime10_0_12Observed"] == true
            && (bool?)receipt["timedOut"] == false && (bool?)receipt["capacityHold"] == false
            && receipt["arguments"] is JsonArray arguments && arguments.Any(item => ((string?)item)?.Contains("MSBuild.dll", StringComparison.Ordinal) == true)
            && arguments.Any(item => (string?)item == "-target:Build"), "The actual successful isolated Windows producer build receipt is required.");
        string[] names = ["Program.cs", "LinuxPermissionControl.csproj", "requirements.json", "README.md"];
        JsonArray sources = receipt["sourcePins"]!.AsArray();
        Need(sources.Select(pin => (string)pin!["path"]!).Order(StringComparer.Ordinal).SequenceEqual(names.Order(StringComparer.Ordinal), StringComparer.Ordinal), "The operator source/project/data membership differs from its actual build.");
        foreach (JsonNode? pin in sources) { Need(Hash(Path.Combine(sourceRoot, (string)pin!["path"]!)) == (string?)pin["sha256"], "An operator source/project/recipe changed after compilation."); }
        foreach (string file in new[] { assembly, Path.ChangeExtension(assembly, ".deps.json"), Path.ChangeExtension(assembly, ".runtimeconfig.json"), Path.ChangeExtension(assembly, ".pdb"), typeof(ProcessRunner).Assembly.Location, Path.Combine(AppContext.BaseDirectory, "requirements.json") })
        { Need(receipt["portableOutputPins"]!.AsArray().Count(pin => (string?)pin!["name"] == Path.GetFileName(file) && (string?)pin["sha256"] == Hash(file)) == 1, "The deployed operator/helper/data bytes differ from the actual producer build."); }
        foreach (string name in new[] { "MSBuild.dll", "csc.dll", "Microsoft.CodeAnalysis.CSharp.dll" })
        { Need(receipt["compilerPins"]!.AsArray().Any(pin => ((string)pin!["path"]!).Replace('\\', '/').EndsWith("/" + name, StringComparison.Ordinal)), "The observed producer compiler closure omits " + name); }
        Need(receipt["runtimePins"] is JsonArray { Count: > 0 } && (string?)receipt["foreignToolPinSemantics"] == "Observed actual Windows producer bytes; not live-rehashed from Linux", "The actual foreign producer runtime provenance must remain explicit.");
        using var stream = File.OpenRead(Path.ChangeExtension(assembly, ".pdb"));
        using MetadataReaderProvider provider = MetadataReaderProvider.FromPortablePdbStream(stream);
        MetadataReader reader = provider.GetMetadataReader();
        using var image = new PEReader(File.OpenRead(assembly));
        var codeViews = image.ReadDebugDirectory().Where(entry => entry.Type == DebugDirectoryEntryType.CodeView).ToArray();
        Need(codeViews.Length == 1 && reader.DebugMetadataHeader is not null && reader.DebugMetadataHeader.Id.Length == 20, "The actual operator DLL/PDB debug identities are absent or ambiguous.");
        CodeViewDebugDirectoryData codeView = image.ReadCodeViewDebugDirectoryData(codeViews[0]);
        byte[] pdbId = reader.DebugMetadataHeader!.Id.ToArray();
        Need(codeView.Age == 1 && codeView.Guid == new Guid(pdbId.AsSpan(0, 16)) && codeViews[0].Stamp == BinaryPrimitives.ReadUInt32LittleEndian(pdbId.AsSpan(16)), "The actual portable PDB is not the one named by the compiled operator DLL.");
        var documents = reader.Documents.Select(handle => reader.GetDocument(handle)).Where(document => reader.GetString(document.Name).Replace('\\', '/').EndsWith("/Program.cs", StringComparison.Ordinal)).ToArray();
        Need(documents.Length == 1 && reader.GetGuid(documents[0].HashAlgorithm) == new Guid("8829d00f-11b8-4213-878b-770e8597ac16")
            && Convert.ToHexStringLower(reader.GetBlobBytes(documents[0].Hash)) == Hash(Path.Combine(sourceRoot, "Program.cs")), "The actual portable PDB does not bind this operator source document.");
    }
    private static JsonObject Location(string text, int start, int length)
    {
        JsonObject Point(int offset) => new() { ["line"] = 1 + text.AsSpan(0, offset).Count('\n'), ["column"] = offset - text.LastIndexOf('\n', Math.Max(0, offset - 1)) };
        return new() { ["start"] = Point(start), ["end"] = Point(start + length) };
    }
    private static int Unique(string text, string value) { int index = text.IndexOf(value, StringComparison.Ordinal); Need(index >= 0 && text.LastIndexOf(value, StringComparison.Ordinal) == index, "A reviewed actual source expression changed or is ambiguous."); return index; }
    private static string Text(object value, string name) => (string)value.GetType().GetProperty(name, Instance)!.GetValue(value)!;
    private static async Task<object?> Await(object value) { Task task = (Task)value; await task; return task.GetType().GetProperty("Result")?.GetValue(task); }
    private static object? Call(Type type, string name, params object?[] args) => Invoke(type.GetMethods(Static).Single(method => method.Name == name && method.GetParameters().Length == args.Length), null, args);
    private static object? Invoke(MethodInfo method, object? target, object?[] args) { try { return method.Invoke(target, args); } catch (TargetInvocationException error) when (error.InnerException is not null) { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; } }
    private static string Full(string path) { Need(Path.IsPathFullyQualified(path), "Use absolute owned paths."); return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); }
    private static bool Under(string root, string path) => root == path || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    private static bool Same(string left, string right) => Full(left) == Full(right);
    private static JsonObject Read(string path) => JsonNode.Parse(File.ReadAllBytes(path))!.AsObject();
    private static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(stream)); }
    private static Pin Pinned(string path) => new(Full(path), Hash(path), new FileInfo(path).Length);
    private static void Write(string path, object value) { byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, Json); Need(bytes.Length <= 262144, "Supplemental receipt exceeds256KiB."); using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read); file.Write(bytes); file.Flush(true); }
    private static void Need(bool value, string message) { if (!value) { throw new InvalidDataException(message); } }
    private static class Native { [DllImport("libc")] internal static extern uint getuid(); [DllImport("libc")] internal static extern uint geteuid(); }
}
