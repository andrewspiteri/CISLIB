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

// Supplemental invocation glue. No product/test/checker source is compiled here.
// All process-tree and capacity enforcement delegates to the frozen tested APIs.
return args.FirstOrDefault() == "self-controls" ? Operator.SelfControls(args[1..]) : await Operator.RunAsync(args);

internal static class Operator
{
    private const long AggregateLimit = 2L * 1024 * 1024 * 1024, RetainedLimit = 256L * 1024 * 1024, ChildLimit = 128L * 1024 * 1024;
    private const string Filter = "FullyQualifiedName=CIS.Tests.IntegrationTests.Healthcheck.Reporting.HealthcheckHttpTransportTests.RealKestrelServesKnownViewsWithCachePreventionAndNoBusinessEffects|FullyQualifiedName=CIS.Tests.IntegrationTests.Healthcheck.Reporting.HealthcheckProbeProcessTests.OmittedViewAndValidUpperCliLimitsStillRequestAndEmitHealth|FullyQualifiedName=CIS.Tests.TestAssemblySetup.SchedulingMatchesRequestedMode";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly BindingFlags Static = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly StringComparison Comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private sealed record Pin(string Path, string Sha256, long Bytes);

    internal static async Task<int> RunAsync(string[] args)
    {
        var clock = Stopwatch.StartNew();
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        var external = new List<string>();
        for (int index = 0; index < args.Length; index += 2)
        {
            Need(index + 1 < args.Length, "Expected explicit --name value pairs.");
            if (args[index] == "--external-owned-root") { external.Add(Full(args[index + 1])); }
            else { Need(options.TryAdd(args[index], args[index + 1]), "Duplicate operator option."); }
        }
        string[] required = ["--candidate-root", "--task-root", "--checker", "--operator-source-root", "--output-root", "--temporary-root",
            "--acceptance-validator", "--windows-pipes-proof", "--linux-permission-proof", "--acceptance-policy", "--acceptance-source-root", "--acceptance-build-receipt", "--operator-build-receipt"];
        Need(required.All(options.ContainsKey) && options.Keys.All(key => required.Contains(key, StringComparer.Ordinal)), "Unknown or missing operator argument.");
        string candidateRoot = Full(options["--candidate-root"]), taskRoot = Full(options["--task-root"]);
        string checker = Full(options["--checker"]), sourceRoot = Full(options["--operator-source-root"]);
        string output = Full(options["--output-root"]), temporary = Full(options["--temporary-root"]);
        string validatorPath = Full(options["--acceptance-validator"]), windowsProof = Full(options["--windows-pipes-proof"]);
        string linuxProof = Full(options["--linux-permission-proof"]), policy = Full(options["--acceptance-policy"]);
        string validatorSource = Full(options["--acceptance-source-root"]), validatorBuild = Full(options["--acceptance-build-receipt"]);
        string operatorBuild = Full(options["--operator-build-receipt"]);
        Need(OperatingSystem.IsLinux() && Native.getuid() == 0 && Native.geteuid() == 0, "The explicit v4 permission-control route requires the actual Linux UID0/effectiveUID0 actor.");
        Need(AtOrUnder(taskRoot, candidateRoot) && AtOrUnder(taskRoot, output) && !AtOrUnder(candidateRoot, output), "Operator output must be a separate owned task subroot.");
        Need(!Directory.Exists(output) && !File.Exists(output), "Use fresh operator output.");
        Need(!Directory.Exists(temporary) && !File.Exists(temporary) && temporary.Length <= 65, "Use a fresh short owned temporary root (at most65characters).");
        Need(!AtOrUnder(candidateRoot, sourceRoot) && !AtOrUnder(candidateRoot, temporary), "Supplemental source/temp must remain outside the frozen candidate.");
        JsonObject candidateJson = Read(Path.Combine(candidateRoot, "candidate.json")), toolchainJson = Read(Path.Combine(candidateRoot, "toolchain.json"));
        string dotnet = (string)toolchainJson["subjectDotnet"]!, cli = toolchainJson["files"]!.AsArray().Single(p => Path.GetFileName((string)p!["path"]!) == "Stryker.CLI.dll")!["path"]!.GetValue<string>();
        string core = Path.Combine(Path.GetDirectoryName(cli)!, "Stryker.Core.dll"), helper = Path.Combine(Path.GetDirectoryName(checker)!, "CIS.Core.TestSupport.dll");
        Need((string?)toolchainJson["toolDotnet"] == dotnet && RuntimeInformation.FrameworkDescription == ".NET 10.0.12"
            && Same(Environment.ProcessPath!, dotnet), "Use the frozen shared dotnet host under subject runtime10.0.12.");
        var pins = toolchainJson["files"]!.AsArray().Select(p => new Pin((string)p!["path"]!, (string)p["sha256"]!, new FileInfo((string)p["path"]!).Length)).ToList();
        Need(pins.Any(p => Same(p.Path, checker)) && pins.Any(p => Same(p.Path, helper)), "Checker/helper paths must belong to the frozen toolchain.");
        pins.AddRange(new[] { "candidate.json", "toolchain.json" }.Select(name => Pinned(Path.Combine(candidateRoot, name))));
        pins.AddRange(new[] { "Program.cs", "ChildOperator.csproj", "README.md" }.Select(name => Pinned(Path.Combine(sourceRoot, name))));
        string assemblyPath = typeof(Operator).Assembly.Location;
        pins.AddRange(new[] { assemblyPath, Path.ChangeExtension(assemblyPath, ".deps.json"), Path.ChangeExtension(assemblyPath, ".runtimeconfig.json"), Path.ChangeExtension(assemblyPath, ".pdb"), operatorBuild }.Select(Pinned));
        ValidateBuildReceipt(operatorBuild, sourceRoot, assemblyPath);
        pins.AddRange(new[] { validatorPath, Path.ChangeExtension(validatorPath, ".deps.json"), Path.ChangeExtension(validatorPath, ".runtimeconfig.json"), Path.ChangeExtension(validatorPath, ".pdb"),
            validatorBuild, windowsProof, linuxProof, policy }.Select(Pinned));
        AssertPins();
        AssemblyLoadContext.Default.LoadFromAssemblyPath(helper);
        Assembly frozen = AssemblyLoadContext.Default.LoadFromAssemblyPath(checker);
        Type grouped = frozen.GetType("CIS.Verification.HealthcheckGroupedMutation", throwOnError: true)!;
        Type runner = AssemblyLoadContext.Default.Assemblies.Single(a => a.GetName().Name == "CIS.Core.TestSupport").GetType("CIS.TestSupport.ProcessRunner", true)!;
        MethodInfo processRun = runner.GetMethod("RunAsync", Static)!;
        Need(processRun.GetParameters().Length == 10, "Frozen ProcessRunner signature changed.");
        _ = Call(grouped, "ValidateOwnedScope", candidateRoot, taskRoot);
        object candidate = Call(grouped, "CandidateAt", candidateRoot)!;
        object toolchain = Call(grouped, "ReadToolchain", candidateRoot, candidate)!;
        string sdkMsBuild = (string)Call(grouped, "SdkMsBuildPath", toolchain)!;
        Call(grouped, "ValidatePilot", candidateRoot, candidate);
        string workspace = Path.Combine(candidateRoot, "workspace"), language = Path.Combine(workspace, "implementations/csharp");
        Call(grouped, "VerifyInputs", workspace, candidate);
        using var lease = new FileStream(Path.Combine(candidateRoot, "active.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        Assembly validator = AssemblyLoadContext.Default.LoadFromAssemblyPath(validatorPath);
        Type prerequisites = validator.GetType("CIS.Recovery.HealthcheckAcceptance.TransportPrerequisites", true)!;
        MethodInfo validatePrerequisites = prerequisites.GetMethod("Validate", BindingFlags.Public | BindingFlags.Static)!;
        Need(validatePrerequisites.GetParameters().Length == 7 && validatePrerequisites.GetParameters().All(parameter => parameter.ParameterType == typeof(string))
            && validatePrerequisites.ReturnType == typeof(JsonObject), "The explicit supplemental transport-prerequisite validator API changed.");
        JsonObject prerequisiteReceipt = (JsonObject)Invoke(validatePrerequisites, null, [checker, candidateRoot, windowsProof, linuxProof, policy, validatorSource, validatorBuild])!;
        JsonObject consumer = prerequisiteReceipt["consumer"]!.AsObject();
        pins.AddRange(consumer["sourcePins"]!.AsArray().Select(pin => Pinned(Path.Combine(validatorSource, (string)pin!["path"]!))));
        string consumerRuntime = (string)consumer["actualCoreLib"]!;
        Need(Hash(consumerRuntime) == (string?)consumer["actualCoreLibSha256"] && (string?)consumer["actualFramework"] == ".NET 10.0.12", "The admitted analysis consumer did not retain the actual executor runtime identity.");
        pins.Add(Pinned(consumerRuntime));
        string consumerHost = (string)consumer["actualDotnetHost"]!;
        Need(Same(consumerHost, dotnet) && Hash(consumerHost) == (string?)consumer["actualDotnetHostSha256"], "The admitted consumer executor host differs from the frozen private dotnet host."); pins.Add(Pinned(consumerHost));
        string priorRun = linuxProof;
        JsonObject priorContext = Read(priorRun);
        Call(grouped, "ValidateSdkMsBuildEnvironment", priorContext, toolchain);
        JsonObject buildProperties = priorContext["buildProperties"]!.AsObject();
        var expectedProperties = new Dictionary<string, string> {
            ["CisMutationTesting"] = "true", ["CisHealthcheckMutationCohort"] = "true",
            ["CisHealthcheckPilotCohort"] = "false", ["CisLinuxIdentityQualification"] = OperatingSystem.IsLinux() ? "true" : "false" };
        Need(buildProperties.Count == expectedProperties.Count && expectedProperties.All(property => (string?)buildProperties[property.Key] == property.Value),
            "The prerequisite bounded Linux permission build must declare the exact current four-property context.");
        pins.Add(Pinned(priorRun));
        // Candidate output binaries are intentionally mutated and then rebuilt.
        // The shared validator checks them before/after; immutable proof files
        // outside the workspace remain pinned during every intermediate phase.
        pins.AddRange(priorContext["files"]!.AsArray().Select(pin => (string)pin!["path"]!).Where(path => !AtOrUnder(workspace, path)).Select(Pinned));
        string canonicalCore = Path.Combine(language, "src/CIS.Core/bin/Release/net10.0/CIS.Core.dll");
        string testBin = Path.Combine(language, "tests/CIS.Core.IntegrationTests/bin/Release/net10.0");
        string[] coreCopies = [Path.Combine(testBin, "CIS.Core.dll"), Path.Combine(testBin, "healthcheck-http-host/CIS.Core.dll"), Path.Combine(testBin, "healthcheck-probe/CIS.Core.dll")];
        Need(coreCopies.All(copy => Hash(copy) == Hash(canonicalCore)), "Prerequisite restored Integration/fixture Core copies disagree.");
        Pin[] baselineBinaries = new[] { canonicalCore, Path.Combine(testBin, "CIS.Core.IntegrationTests.dll"), Path.Combine(testBin, "healthcheck-http-host/CIS.Healthcheck.HttpFixture.dll"),
            Path.Combine(testBin, "healthcheck-probe/CisHealthProbe.dll") }.Concat(coreCopies).Select(Pinned).ToArray();
        foreach (JsonNode? adapter in toolchainJson["adapters"]!.AsArray()) {
            Need(Hash(Path.Combine(testBin, (string)adapter!["path"]!)) == (string)adapter["sha256"]!, "Integration adapter differs from frozen native payload.");
        }
        string profile = "hc-child-v4-" + Guid.NewGuid().ToString("N")[..8], attempt = Guid.NewGuid().ToString("N")[..12];
        string profileRoot = Path.Combine(workspace, ".cis/local/testing/incremental", profile), nativeOutput = Path.Combine(profileRoot, "work", attempt);
        string configuration = Path.Combine(nativeOutput, "stryker-config.json"), childRoot = Path.Combine(profileRoot, "child-evidence");
        string project = Path.Combine(language, "tests/CIS.Core.IntegrationTests/CIS.Core.IntegrationTests.csproj");
        var statements = new[] {
            (Source: "Healthcheck/AspNetCore/HealthcheckApplicationBuilderExtensions.cs", Statement: "context.Response.Headers.CacheControl = \"no-store, no-cache\";"),
            (Source: "Healthcheck/Pipes/HealthProbeClient.cs", Statement: "return parsed.SelectedHealthy ? 0 : 1;") };
        string[] selectors = statements.Select(s => {
            string file = Path.Combine(language, "src/CIS.Core", s.Source), text = File.ReadAllText(file);
            Need(candidateJson["inputs"]!.AsArray().Any(p => (string?)p!["path"] == "implementations/csharp/src/CIS.Core/" + s.Source && (string?)p["sha256"] == Hash(file)), "Control source differs from candidate.");
            int start = text.IndexOf(s.Statement, StringComparison.Ordinal);
            Need(start >= 0 && start == text.LastIndexOf(s.Statement, StringComparison.Ordinal), "Reviewed control statement changed/ambiguous.");
            return s.Source + "{" + start + ".." + (start + s.Statement.Length) + "}";
        }).ToArray();
        Need(external.All(Directory.Exists), "Every explicitly named external owned root must exist.");
        var roots = new[] { taskRoot }.Concat(external).Append(temporary).Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToArray();
        roots = roots.Where(root => !roots.Any(other => !Same(root, other) && AtOrUnder(other, root))).ToArray();
        object[] volumes = roots.Append(candidateRoot).Select(path => Call(grouped, "ResolveStorageVolume", path)!).ToArray();
        long Free() => volumes.Min(v => (long)Invoke(v.GetType().GetMethod("AvailableBytes", Instance)!, v, [])!);
        long Bytes(string path) => Directory.Exists(path) ? (long)Call(grouped, "CountOwnedScopeBytes", path, path)! : 0;
        long peak = 0, peakChild = 0;
        long Aggregate() {
            long bytes = roots.Sum(Bytes), children = Bytes(childRoot);
            peakChild = Math.Max(peakChild, children);
            Need(children <= ChildLimit, "Child evidence exceeded128MiB.");
            return bytes;
        }
        Need(Aggregate() <= AggregateLimit && Free() >= AggregateLimit, "Preflight aggregate/free reserve failed.");
        Directory.CreateDirectory(output);
        var environment = new Dictionary<string, string?> {
            ["UseSharedCompilation"] = "false", ["MSBUILDDISABLENODEREUSE"] = "1", ["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0", ["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] = "1",
            ["DOTNET_ROLL_FORWARD"] = null,
            ["MSBUILD_EXE_PATH"] = sdkMsBuild,
            ["PATH"] = Path.GetDirectoryName(dotnet)! + Path.PathSeparator + (Environment.GetEnvironmentVariable("PATH") ?? ""),
            ["RestoreLockedMode"] = "true", ["TEMP"] = temporary, ["TMP"] = temporary, ["TMPDIR"] = temporary,
            ["CIS_HEALTHCHECK_MUTATION_SESSION"] = null, ["CIS_HEALTHCHECK_IDENTITY_MANIFEST"] = null };
        foreach ((string name, string value) in expectedProperties) { environment[name] = value; }
        var settings = new Dictionary<string, object> {
            ["project"] = "CIS.Core.csproj", ["test-projects"] = new[] { "../../tests/CIS.Core.IntegrationTests/CIS.Core.IntegrationTests.csproj" },
            ["test-case-filter"] = Filter, ["mutate"] = selectors, ["mutation-level"] = "Complete", ["test-runner"] = "vstest",
            ["target-framework"] = "net10.0", ["configuration"] = "Release", ["coverage-analysis"] = "off", ["disable-bail"] = true,
            ["concurrency"] = 2, ["additional-timeout"] = 10000, ["reporters"] = new[] { "json", "progress", "cleartext" },
            ["thresholds"] = new { high = 100, low = 90, @break = 0 } };
        var commandLog = new List<JsonObject>();
        var failures = new List<string>();
        var failureRecords = new List<Pin>();
        Pin? beforePrerequisiteRecord = null, afterPrerequisiteRecord = null;
        bool restored = false, cleanupHealthy = true, nativeCompleted = false;
        string temporaryCleanup = "not attempted";
        object[] volumePins = volumes.Select(v => Invoke(v.GetType().GetMethod("Observation", Instance)!, v, [])!).ToArray();
        try
        {
            Directory.CreateDirectory(temporary); Directory.CreateDirectory(nativeOutput);
            string beforePath = Path.Combine(output, "transport-prerequisites-before.json");
            Write(beforePath, prerequisiteReceipt); beforePrerequisiteRecord = Pinned(beforePath);
            Write(Path.Combine(temporary, "owner.json"), new { candidateIdentity = (string?)candidateJson["identity"], operatorOutput = output });
            Write(configuration, new Dictionary<string, object> { ["stryker-config"] = settings }); pins.Add(Pinned(configuration));
            Write(Path.Combine(output, "prepared.json"), new { schema = "cis.recovery.child-operator-prepared/v4", recordRevision = 2, candidateIdentity = (string?)candidateJson["identity"],
                args, pins, baselineBinaries, buildProperties, operatorSourceRoot = sourceRoot, operatorBuildReceiptPath = operatorBuild, operatorBuildReceiptSha256 = Hash(operatorBuild),
                priorContextValidated = "explicit validated whole Windows Pipes proof plus bounded six-tuple Linux permission proof; all four declared properties and pinned SDK MSBuild",
                transportPrerequisites = PrerequisiteSummary(prerequisiteReceipt), transportPrerequisitesRecord = beforePrerequisiteRecord,
                sdk = (string?)toolchainJson["sdk"], actualRuntime = RuntimeInformation.FrameworkDescription, actualHost = Environment.ProcessPath,
                compilerPins = pins.Where(pin => Path.GetFileName(pin.Path) is "csc.dll" or "Microsoft.CodeAnalysis.CSharp.dll" or "MSBuild.dll").ToArray(),
                environment, selectors, filter = Filter, budgetSeconds = 600, restorationReserveSeconds = 90, roots, volumePins,
                aggregateLimitBytes = AggregateLimit, freeReserveBytes = AggregateLimit, childLimitBytes = ChildLimit, retainedLimitBytes = RetainedLimit,
                frozenGuardAssembly = Pinned(checker), guardMethods = new[] { "MonitorCapacityAsync", "CountOwnedScopeBytes", "ResolveStorageVolume", "DeleteOwned", "SdkMsBuildPath", "ValidateSdkMsBuildEnvironment" },
                noUnionScopeOrScoreCredit = true });
            await Tests("baseline");
            string[] command = ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", checker, "healthcheck-mutation-run",
                "--language-root", language, "--profile-id", profile, "--dotnet", dotnet, "--stryker-cli", cli, "--stryker-core", core,
                "--child-root", childRoot, "--maximum-child-bytes", ChildLimit.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "--", "--config-file", configuration, "--output", nativeOutput, "--skip-version-check", "--log-to-file"];
            await Run("native", command, reserve: 90);
            nativeCompleted = true;
        }
        catch (Exception error) { RecordFailure("baseline-or-native", error); }
        finally
        {
            try
            {
                AssertPins(); Call(grouped, "VerifyInputs", workspace, candidate);
                await Run("restored-build", ["build", project, "-c", "Release", "--no-restore", "--no-incremental"]);
                Need(coreCopies.All(copy => Hash(copy) == Hash(canonicalCore)), "A restored test/child copy still differs from clean Core.");
                Need(baselineBinaries.All(pin => Hash(pin.Path) == pin.Sha256), "Restored shipping/test/fixture binaries differ from the verified pre-control build context.");
                await Tests("restored"); restored = true;
            }
            catch (Exception error) { RecordFailure("restoration", error); }
        }
        if (nativeCompleted && restored && failures.Count == 0)
        {
            try
            {
                string session = Path.Combine(childRoot, attempt, "session/session.json"), binding = Path.Combine(childRoot, attempt, "binding.json"), report = Path.Combine(nativeOutput, "reports/mutation-report.json");
                Type sessionType = frozen.GetType("CIS.Verification.HealthcheckMutationChildren.HealthcheckMutationChildProtocol+Session", true)!;
                object sessionValue = JsonSerializer.Deserialize(File.ReadAllBytes(session), sessionType, Json)!;
                Type childControlType = grouped.GetNestedType("ChildControl", BindingFlags.Public | BindingFlags.NonPublic)!;
                string[] sources = statements.Select(s => s.Source).ToArray();
                object inputs = candidate.GetType().GetProperty("Inputs", Instance)!.GetValue(candidate)!;
                object[] definitions = ((IEnumerable)Call(grouped, "Definitions", Read(report), sources, inputs)!).Cast<object>().ToArray();
                Type protocol = frozen.GetType("CIS.Verification.HealthcheckMutationChildren.HealthcheckMutationChildProtocol", true)!;
                object[] launches = ReadNativeLaunches(Path.Combine(childRoot, attempt, "session/launches"), protocol);
                var controls = new List<object>();
                foreach ((string kind, string body, string assembly) in new[] { ("http-host", sources[0], "CIS.Healthcheck.HttpFixture.dll"), ("probe", sources[1], "CisHealthProbe.dll") })
                {
                    object definition = SelectChildDefinition(definitions, launches, body, assembly);
                    int active = int.Parse(Text(definition, "NativeId"), System.Globalization.CultureInfo.InvariantCulture);
                    object childControl = Activator.CreateInstance(childControlType, [session, kind, active, report, Hash(report), Text(definition, "Identity"), binding, Hash(binding)])!;
                    object selections = Call(grouped, "ValidateChildBinding", childControl, sessionValue, candidate, toolchain)!;
                    Call(grouped, "ValidateChildDefinitionSelection", definition, selections, File.ReadAllText(Path.Combine(language, "src/CIS.Core", body)));
                    await Run("audit-" + kind, ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", checker, "healthcheck-mutation-children", "audit",
                        "--session", session, "--output", Path.Combine(output, "audit-" + kind), "--require-kind", kind, "--require-active", active.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
                    controls.Add(new { session, kind, activeMutant = active, nativeReport = report, nativeReportSha256 = Hash(report),
                        definitionIdentity = Text(definition, "Identity"), nativeBinding = binding, nativeBindingSha256 = Hash(binding) });
                }
                Write(Path.Combine(output, "child-controls.json"), controls);
            }
            catch (Exception error) { RecordFailure("actual-child-audit-control-admission", error); }
        }
        JsonObject? postflightPrerequisites = null;
        try { AssertPins(); Call(grouped, "VerifyInputs", workspace, candidate); _ = Call(grouped, "ReadToolchain", candidateRoot, candidate);
            postflightPrerequisites = (JsonObject)Invoke(validatePrerequisites, null, [checker, candidateRoot, windowsProof, linuxProof, policy, validatorSource, validatorBuild])!;
            Need(JsonNode.DeepEquals(prerequisiteReceipt, postflightPrerequisites), "Transport prerequisite observations changed during the child transaction.");
            string afterPath = Path.Combine(output, "transport-prerequisites-after.json");
            Write(afterPath, postflightPrerequisites); afterPrerequisiteRecord = Pinned(afterPath); }
        catch (Exception error) { RecordFailure("postflight-pins", error); }
        try {
            Need(cleanupHealthy, "Process-tree cleanup was not healthy; retain owned temp for investigation.");
            if (Directory.Exists(temporary)) { Call(grouped, "DeleteOwned", temporary, Path.GetDirectoryName(temporary)!); }
            temporaryCleanup = "owned short temp removed";
        } catch (Exception error) { temporaryCleanup = "failed/retained"; RecordFailure("owned-temp-cleanup", error); }
        long retained = 0;
        try {
            retained = checked((long)Call(grouped, "OwnedBytes", candidateRoot, true)! + Bytes(profileRoot) + Bytes(output));
            Need(retained + 2 * 262144 <= RetainedLimit, "Aggregate retained candidate/control evidence exceeds256MiB (including both terminal-record allowances).");
            Need(Aggregate() + 2 * 262144 <= AggregateLimit && Free() >= AggregateLimit + 2 * 262144, "Postflight aggregate/free reserve, including terminal records, failed.");
        } catch (Exception error) { RecordFailure("postflight-capacity", error); }
        bool complete = nativeCompleted && restored && cleanupHealthy && failures.Count == 0 && clock.Elapsed.TotalSeconds <= 600;
        string executionPath = Path.Combine(output, "execution-record.json");
        Write(executionPath, new { schema = "cis.recovery.child-operator-execution/v4", recordRevision = 2, candidateIdentity = (string?)candidateJson["identity"],
            operatorSourceRoot = sourceRoot, operatorBuildReceiptPath = operatorBuild, operatorBuildReceiptSha256 = Hash(operatorBuild),
            budgetSeconds = 600, restorationReserveSeconds = 90, nativeConcurrency = 2, coverageAnalysis = "off", selectedNativeCases = 3, selectedSourceSpans = 2,
            commands = commandLog, failureRecords, failures, nativeCompleted, restored, processCleanupHealthy = cleanupHealthy, temporaryCleanup, complete, noUnionScopeOrScoreCredit = true,
            seconds = clock.Elapsed.TotalSeconds, peakOwnedBytes = peak, peakChildBytes = peakChild, retainedBytesBeforeManifest = retained,
            aggregateLimitBytes = AggregateLimit, freeReserveBytes = AggregateLimit, childLimitBytes = ChildLimit, retainedLimitBytes = RetainedLimit });
        Pin executionRecord = Pinned(executionPath);
        string[] dynamicFiles = Directory.GetFiles(output, "*", SearchOption.AllDirectories).Concat(new[] { configuration,
            Path.Combine(nativeOutput, "reports/mutation-report.json"), Path.Combine(childRoot, attempt, "prepared.json"), Path.Combine(childRoot, attempt, "started.json"),
            Path.Combine(childRoot, attempt, "binding.json"), Path.Combine(childRoot, attempt, "session/session.json") }).Where(File.Exists).Distinct(StringComparer.Ordinal).ToArray();
        Write(Path.Combine(output, "manifest.json"), new { schema = "cis.recovery.child-operator/v4", recordRevision = 2, candidateIdentity = (string?)candidateJson["identity"], args, buildProperties,
            transportPrerequisites = PrerequisiteSummary(prerequisiteReceipt), postflightTransportPrerequisites = postflightPrerequisites is null ? null : PrerequisiteSummary(postflightPrerequisites),
            transportPrerequisitesRecord = beforePrerequisiteRecord, postflightTransportPrerequisitesRecord = afterPrerequisiteRecord, executionRecord, failureRecords,
            operatorSourceRoot = sourceRoot, operatorBuildReceiptPath = operatorBuild, operatorBuildReceiptSha256 = Hash(operatorBuild), nativeConcurrency = 2, coverageAnalysis = "off", selectedNativeCases = 3, selectedSourceSpans = 2,
            supplementalPins = pins, baselineBinaries, frozenCheckerAndSourcesMandatory = true, noUnionScopeOrScoreCredit = true, environment, selectors, filter = Filter,
            commandLog, nativeCompleted, restored, processCleanupHealthy = cleanupHealthy, temporaryCleanup, failures, complete,
            budgetSeconds = 600, restorationReserveSeconds = 90, seconds = clock.Elapsed.TotalSeconds,
            cleanupDeadlinePerCanceledCommandSeconds = 5, cleanupOverrunGrantsNoBudgetCredit = true, aggregateRoots = roots,
            peakOwnedBytes = peak, peakChildBytes = peakChild, retainedBytesBeforeManifest = retained, volumePins, actualEvidencePins = dynamicFiles.Select(Pinned).ToArray(),
            childControls = File.Exists(Path.Combine(output, "child-controls.json")) ? Pinned(Path.Combine(output, "child-controls.json")) : null });
        Console.WriteLine("Child operator complete=" + complete + "; " + clock.Elapsed.TotalSeconds.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "s; " + output);
        return complete ? 0 : 1;

        void RecordFailure(string phase, Exception error)
        {
            string detail = error.ToString(), path = Path.Combine(output, "failure-" + (failureRecords.Count + 1).ToString("D2", System.Globalization.CultureInfo.InvariantCulture) + "-" + phase + ".json");
            failures.Add(phase + ": " + Brief(error));
            try {
                Write(path, new { schema = "cis.recovery.child-operator-failure/v4", recordRevision = 2, phase, observedUtc = DateTimeOffset.UtcNow,
                    elapsedSeconds = clock.Elapsed.TotalSeconds, exceptionType = error.GetType().FullName, detail = detail[..Math.Min(detail.Length, 16384)], originalDetailCharacters = detail.Length,
                    diagnosticTruncated = detail.Length > 16384, unqualified = true });
                failureRecords.Add(Pinned(path));
            } catch (Exception persistence) {
                failures.Add("Failure-record persistence: " + Brief(persistence));
                Console.Error.WriteLine("Original failure: " + detail); Console.Error.WriteLine("Failure-record persistence: " + persistence);
            }
        }

        void AssertPins() { foreach (Pin pin in pins) { Need(File.Exists(pin.Path) && Hash(pin.Path) == pin.Sha256 && new FileInfo(pin.Path).Length == pin.Bytes, "Pinned supplemental/frozen input changed: " + pin.Path); } }
        async Task Run(string name, string[] command, int reserve = 0)
        {
            using var cancel = new CancellationTokenSource(); using var stop = new CancellationTokenSource();
            Task<Exception?>? monitor = null; object? result = null; Exception? failure = null, capacityFailure = null;
            var own = new Stopwatch(); double left = 0, startedElapsed = clock.Elapsed.TotalSeconds;
            DateTimeOffset started = DateTimeOffset.UtcNow; long phasePeak = 0; bool monitorCompleted = false;
            try {
                long initialBytes = Aggregate(); phasePeak = Math.Max(phasePeak, initialBytes); peak = Math.Max(peak, initialBytes);
                Need(initialBytes <= AggregateLimit && Free() >= AggregateLimit, "Capacity/free reserve failed before " + name);
                monitor = (Task<Exception?>)Call(grouped, "MonitorCapacityAsync", (Func<long>)Aggregate, (Func<long>)Free,
                    (Action<long>)(bytes => { phasePeak = Math.Max(phasePeak, bytes); peak = Math.Max(peak, bytes); }), cancel, stop.Token, AggregateLimit, AggregateLimit)!;
                startedElapsed = clock.Elapsed.TotalSeconds; left = 600 - startedElapsed - reserve;
                Need(left > 0, "Total600s budget exhausted before " + name);
                started = DateTimeOffset.UtcNow; own.Start();
                Task task = (Task)Invoke(processRun, null, [dotnet, command, language, TimeSpan.FromSeconds(left), environment, null, 65536, true, null, cancel.Token])!;
                await task; result = task.GetType().GetProperty("Result")!.GetValue(task);
            } catch (Exception error) {
                failure = error;
                result = error.GetType().GetProperty("Result")?.GetValue(error);
                if (error.GetType().GetProperty("CleanupCompleted")?.GetValue(error) is bool healthy) { cleanupHealthy &= healthy; }
            } finally {
                await stop.CancelAsync();
                if (monitor is not null) {
                    try { capacityFailure = await monitor; monitorCompleted = true; }
                    catch (Exception error) { capacityFailure = error; }
                }
                own.Stop();
                bool drained = result is not null && (bool)result.GetType().GetProperty("OutputDrained")!.GetValue(result)!;
                if (result is not null) {
                    cleanupHealthy &= drained;
                    File.WriteAllText(Path.Combine(output, name + ".stdout.log"), Text(result, "StandardOutput"));
                    File.WriteAllText(Path.Combine(output, name + ".stderr.log"), Text(result, "StandardError"));
                }
                try {
                    long finalBytes = Aggregate(); phasePeak = Math.Max(phasePeak, finalBytes); peak = Math.Max(peak, finalBytes);
                    Need(finalBytes <= AggregateLimit && Free() >= AggregateLimit, "Capacity/free reserve failed after " + name);
                } catch (Exception error) { capacityFailure ??= error; }
                JsonObject row = JsonSerializer.SerializeToNode(new { name, executable = dotnet, arguments = command, startedUtc = started, finishedUtc = DateTimeOffset.UtcNow,
                    startedElapsedSeconds = startedElapsed, seconds = own.Elapsed.TotalSeconds, timeoutSeconds = left, restorationReserveSeconds = reserve,
                    exitCode = result?.GetType().GetProperty("ExitCode")!.GetValue(result), outputDrained = drained,
                    stdoutTotalBytes = result?.GetType().GetProperty("StandardOutputTotalBytes")!.GetValue(result), stderrTotalBytes = result?.GetType().GetProperty("StandardErrorTotalBytes")!.GetValue(result),
                    processId = result?.GetType().GetProperty("ProcessId")!.GetValue(result), failure = failure is null ? null : Brief(failure), cleanupHealthy,
                    monitorCompleted, capacityFailure = capacityFailure is null ? null : Brief(capacityFailure), peakOwnedBytes = phasePeak, peakChildBytes = peakChild,
                    aggregateLimitBytes = AggregateLimit, freeReserveBytes = AggregateLimit, childLimitBytes = ChildLimit }, Json)!.AsObject();
                string phasePath = Path.Combine(output, "phase-" + (commandLog.Count + 1).ToString("D2", System.Globalization.CultureInfo.InvariantCulture) + "-" + name + ".json");
                Write(phasePath, row); row["record"] = JsonSerializer.SerializeToNode(Pinned(phasePath), Json); commandLog.Add(row);
            }
            if (capacityFailure is not null) { throw new InvalidDataException("Existing capacity guard stopped " + name, capacityFailure); }
            if (failure is not null) { ExceptionDispatchInfo.Capture(failure).Throw(); }
            Need(result is not null, "No actual process result for " + name);
            Need((int)result!.GetType().GetProperty("ExitCode")!.GetValue(result)! == 0 && (bool)result!.GetType().GetProperty("OutputDrained")!.GetValue(result)!, name + " failed or did not drain.");
        }
        async Task Tests(string phase) {
            string name = phase + "-CIS.Core.IntegrationTests";
            await Run(name, ["test", project, "-c", "Release", "--no-build", "--no-restore", "--filter", Filter,
                "--logger", "trx;LogFileName=" + name + ".trx", "--results-directory", output]);
            Need((int)Call(grouped, "PassingTrx", Path.Combine(output, name + ".trx"))! == 3, "Selected actual baseline/postflight must pass exactly3cases.");
        }
    }

    private static void ValidateBuildReceipt(string path, string sourceRoot, string assembly)
    {
        JsonObject receipt = Read(path);
        Need((string?)receipt["schema"] == "cis.healthcheck.child-operator-build/v4" && (string?)receipt["compilerPlatform"] == "windows-x64"
            && (string?)receipt["sdk"] == "10.0.401" && (string?)receipt["actualCompileHostRuntime"] == "10.0.12"
            && (int?)receipt["exitCode"] == 0 && (bool?)receipt["outputDrained"] == true && (bool?)receipt["privateRuntime10_0_12Observed"] == true
            && (bool?)receipt["timedOut"] == false && (bool?)receipt["capacityHold"] == false
            && receipt["arguments"] is JsonArray arguments && arguments.Any(item => ((string?)item)?.Contains("MSBuild.dll", StringComparison.Ordinal) == true)
            && arguments.Any(item => (string?)item == "-target:Build"), "The actual successful isolated Windows producer build receipt is required.");
        string[] names = ["Program.cs", "ChildOperator.csproj", "README.md"];
        JsonArray sources = receipt["sourcePins"]!.AsArray();
        Need(sources.Select(pin => (string)pin!["path"]!).Order(StringComparer.Ordinal).SequenceEqual(names.Order(StringComparer.Ordinal), StringComparer.Ordinal), "The operator source/project/data membership differs from its actual build.");
        foreach (JsonNode? pin in sources) { Need(Hash(Path.Combine(sourceRoot, (string)pin!["path"]!)) == (string?)pin["sha256"], "An operator source/project/recipe changed after compilation."); }
        foreach (string file in new[] { assembly, Path.ChangeExtension(assembly, ".deps.json"), Path.ChangeExtension(assembly, ".runtimeconfig.json"), Path.ChangeExtension(assembly, ".pdb") })
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
    private static string Text(object value, string name) => (string)value.GetType().GetProperty(name, Instance)!.GetValue(value)!;
    private static T Value<T>(object value, string name) => (T)(value.GetType().GetProperty(name, Instance)!.GetValue(value) ?? throw new InvalidDataException("Missing native property: " + name));
    private static string Brief(Exception error) { string text = error.GetType().FullName + ": " + error.Message; return text[..Math.Min(text.Length, 1024)]; }
    private static JsonObject PrerequisiteSummary(JsonObject receipt) => new()
    {
        ["schema"] = receipt["schema"]!.DeepClone(), ["commonInputsSha256"] = receipt["commonInputsSha256"]!.DeepClone(),
        ["windowsProofSha256"] = receipt["windowsProofSha256"]!.DeepClone(),
        ["linuxPermissionProof"] = new JsonObject { ["sha256"] = receipt["linuxPermissionProof"]!["sha256"]!.DeepClone() },
        ["consumer"] = new JsonObject { ["buildReceiptSha256"] = receipt["consumer"]!["buildReceiptSha256"]!.DeepClone() }
    };
    private static object[] ReadNativeLaunches(string directory, Type protocol)
    {
        Type launchType = protocol.GetNestedType("Launch", BindingFlags.Public | BindingFlags.NonPublic)!;
        var options = (JsonSerializerOptions)protocol.GetField("JsonOptions", Static)!.GetValue(null)!;
        string schema = (string)protocol.GetField("Schema", Static)!.GetRawConstantValue()!;
        return Directory.GetFiles(directory, "launch.json", SearchOption.AllDirectories).Select(path =>
        {
            object launch = JsonSerializer.Deserialize(File.ReadAllBytes(path), launchType, options) ?? throw new InvalidDataException("Missing native child launch.");
            Need(Text(launch, "Schema") == schema, "Actual frozen child launch schema differs.");
            return launch;
        }).ToArray();
    }
    private static object SelectChildDefinition(object[] definitions, object[] launches, string source, string assembly)
    {
        var activated = launches.Where(launch => Text(Value<object>(launch, "Stage"), "AssemblyFile") == assembly
            && Value<int>(launch, "ActiveMutant") >= 0 && Value<bool>(launch, "Instrumented"))
            .Select(launch => Value<int>(launch, "ActiveMutant").ToString(System.Globalization.CultureInfo.InvariantCulture)).ToHashSet(StringComparer.Ordinal);
        return definitions.Where(definition => Text(definition, "Source") == source && Text(definition, "Status") is "Killed" or "Survived"
            && activated.Contains(Text(definition, "NativeId"))).OrderBy(definition => Text(definition, "Status") == "Killed" ? 0 : 1)
            .ThenBy(definition => Text(definition, "Identity"), StringComparer.Ordinal).FirstOrDefault()
            ?? throw new InvalidDataException("No actual completed selected child activation for " + assembly);
    }
    internal static int SelfControls(string[] args)
    {
        Need(args.Length == 4 && args[0] == "--checker" && args[2] == "--output-root", "Use self-controls --checker <frozen-checker> --output-root <fresh-owned-directory>.");
        string checkerPath = Full(args[1]), output = Full(args[3]), helper = Path.Combine(Path.GetDirectoryName(checkerPath)!, "CIS.Core.TestSupport.dll");
        Need(Hash(checkerPath) == "0bbadb285a453a098fb33a6635b8f5cefe4f45368ab40509e557d267689d7fbe"
            && Hash(helper) == "a84ca81bf306eae633e42c679ea62445b3fd2cd5a1cc585640049e0059305154", "Use the exact frozen native schema/guard closure.");
        Need(!Directory.Exists(output) && !File.Exists(output), "Use fresh control output."); Directory.CreateDirectory(output);
        AssemblyLoadContext.Default.LoadFromAssemblyPath(helper);
        Assembly checker = AssemblyLoadContext.Default.LoadFromAssemblyPath(checkerPath);
        Type protocol = checker.GetType("CIS.Verification.HealthcheckMutationChildren.HealthcheckMutationChildProtocol", true)!;
        Type definitionType = checker.GetType("CIS.Verification.HealthcheckGroupedMutation", true)!.GetNestedType("Definition", BindingFlags.Public | BindingFlags.NonPublic)!;
        string schema = (string)protocol.GetField("Schema", Static)!.GetRawConstantValue()!;
        var passed = new List<string>();
        void Pass(string name, Action check) { check(); passed.Add(name); }
        void Reject(string name, Action check) { bool rejected = false; try { check(); } catch (Exception error) when (error is InvalidDataException or ArgumentException or JsonException) { rejected = true; } Need(rejected, "Control accepted invalid input: " + name); passed.Add(name); }
        void Launch(string name, int active, string assembly, bool instrumented = true)
        {
            string folder = Path.Combine(output, name); Directory.CreateDirectory(folder);
            Write(Path.Combine(folder, "launch.json"), new JsonObject { ["Schema"] = schema, ["ActiveMutant"] = active, ["Instrumented"] = instrumented,
                ["Stage"] = new JsonObject { ["AssemblyFile"] = assembly } });
        }
        object Definition(string id, string status) => JsonSerializer.Deserialize(new JsonObject { ["identity"] = "synthetic-" + id, ["nativeId"] = id,
            ["source"] = "Healthcheck/Pipes/HealthProbeClient.cs", ["status"] = status }.ToJsonString(), definitionType, Json)!;
        Launch("survived", 1, "CisHealthProbe.dll"); Launch("killed", 2, "CisHealthProbe.dll"); Launch("baseline", -1, "CisHealthProbe.dll", false);
        object[] launches = ReadNativeLaunches(output, protocol), definitions = [Definition("1", "Survived"), Definition("2", "Killed")];
        Pass("actual-PascalCase-frozen-Launch-and-Stage-records", () => Need(launches.Length == 3 && launches.Any(launch => Value<int>(launch, "ActiveMutant") == 2
            && Text(Value<object>(launch, "Stage"), "AssemblyFile") == "CisHealthProbe.dll"), "The actual native PascalCase record was not decoded."));
        Pass("prefer-actual-Killed-child-activation-over-earlier-Survived", () => Need(Text(SelectChildDefinition(definitions, launches, "Healthcheck/Pipes/HealthProbeClient.cs", "CisHealthProbe.dll"), "NativeId") == "2", "A later actual Killed activation was ignored."));
        Reject("other-child-body-cannot-substitute", () => SelectChildDefinition(definitions, launches, "Healthcheck/Pipes/HealthProbeClient.cs", "CIS.Healthcheck.HttpFixture.dll"));
        Reject("unactivated-definition-cannot-substitute", () => SelectChildDefinition([Definition("3", "Killed")], launches, "Healthcheck/Pipes/HealthProbeClient.cs", "CisHealthProbe.dll"));
        JsonObject prerequisites = new() { ["schema"] = "synthetic", ["commonInputsSha256"] = "source", ["windowsProofSha256"] = "windows",
            ["linuxPermissionProof"] = new JsonObject { ["sha256"] = "linux", ["largeRetainedObservation"] = new string('x', 140000) },
            ["consumer"] = new JsonObject { ["buildReceiptSha256"] = "actual-build", ["largeRetainedObservation"] = new string('x', 70000) } };
        Pass("full-prerequisite-record-retained-while-final-summary-stays-compact", () =>
        {
            string before = prerequisites.ToJsonString(), path = Path.Combine(output, "full-prerequisite.json"); Write(path, prerequisites);
            JsonObject summary = PrerequisiteSummary(prerequisites);
            Need(summary.ToJsonString().Length < 1024 && prerequisites.ToJsonString() == before && Read(path).ToJsonString() == before
                && (string?)summary["consumer"]?["buildReceiptSha256"] == "actual-build", "Compact receipt lost or rewrote full prerequisite evidence.");
        });
        string durableFailure = Path.Combine(output, "phase-failure.json");
        Write(durableFailure, new { failure = "synthetic guard failure", monitorCompleted = true, capacityFailure = "synthetic", complete = false });
        string failureHash = Hash(durableFailure), oversized = Path.Combine(output, "oversized-final.json");
        Reject("unchanged-256KiB-record-ceiling-rejects-before-creating-final-file", () => Write(oversized, new { padding = new string('x', 262144) }));
        Pass("prior-durable-failure-survives-later-final-serialization-rejection", () => Need(!File.Exists(oversized) && Hash(durableFailure) == failureHash
            && (bool?)Read(durableFailure)["complete"] == false, "A final serialization failure erased prior failure evidence."));
        Write(Path.Combine(output, "results.json"), new { schema = "cis.recovery.child-operator-self-controls/v4", recordRevision = 2, passed, count = passed.Count,
            syntheticAdmissionOnly = true, nativeOrProductEvidence = false, actualFramework = RuntimeInformation.FrameworkDescription, frozenCheckerSha256 = Hash(checkerPath) });
        Console.WriteLine("Child operator controls passed=" + passed.Count); return 0;
    }
    private static object? Call(Type type, string name, params object?[] args) => Invoke(type.GetMethod(name, Static) ?? throw new MissingMethodException(type.FullName, name), null, args);
    private static object? Invoke(MethodInfo method, object? target, object?[] args) {
        try { return method.Invoke(target, args); }
        catch (TargetInvocationException error) when (error.InnerException is not null) { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    private static string Full(string path) { Need(Path.IsPathFullyQualified(path), "Use absolute operator paths."); return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); }
    private static bool Same(string first, string second) => string.Equals(Full(first), Full(second), Comparison);
    private static bool AtOrUnder(string root, string path) => Same(root, path) || Full(path).StartsWith(Full(root) + Path.DirectorySeparatorChar, Comparison);
    private static JsonObject Read(string path) => JsonNode.Parse(File.ReadAllBytes(path))!.AsObject();
    private static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(file)); }
    private static Pin Pinned(string path) => new(Full(path), Hash(path), new FileInfo(path).Length);
    private static void Write(string path, object value) { byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, Json); Need(bytes.Length <= 262144, "Supplemental record exceeds256KiB."); using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read); file.Write(bytes); file.Flush(true); }
    private static void Need(bool condition, string message) { if (!condition) { throw new InvalidDataException(message); } }
    private static class Native { [DllImport("libc")] internal static extern uint getuid(); [DllImport("libc")] internal static extern uint geteuid(); }
}
