using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using static CIS.Verification.HealthcheckMutationChildren.HealthcheckMutationChildProtocol;

namespace CIS.Verification;

// Proves child propagation only. Native Stryker results, strict kill accounting, and release gates remain separate.
internal static class HealthcheckChildMutationVerification
{
    internal static Task<int> RunAsync(string[] args)
    {
        try
        {
            Require(args.Length > 0 && args[0] is "prepare" or "audit", "Expected prepare or audit child proof mode.");
            Dictionary<string, string> options = Options(args.Skip(1).ToArray());
            if (args[0] == "prepare") { Prepare(options); }
            else { Audit(options); }
            return Task.FromResult(0);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return Task.FromResult(1);
        }
    }

    private static void Prepare(Dictionary<string, string> options)
    {
        Only(options, "language-root", "config", "stryker-core", "dotnet", "output", "phase", "source-replay-control");
        string language = Full(options, "language-root"), config = Full(options, "config"), tool = Full(options, "stryker-core");
        string dotnet = Full(options, "dotnet"), output = Empty(options);
        string phase = options.GetValueOrDefault("phase", "automatic");
        Require(phase is "automatic" or "instrumented" or "source-replay", "Unknown child proof phase.");
        Require((phase == "source-replay") == options.ContainsKey("source-replay-control"), "Source replay requires its explicit phase and control together.");
        SourceReplayControl? sourceReplay = phase == "source-replay" ? Read<SourceReplayControl>(Full(options, "source-replay-control")) : null;
        if (sourceReplay is not null) { ValidateSourceReplay(language, sourceReplay); }
        Require(!Under(output, language) && !Under(language, output), "Keep child proof output separate from maintained language inputs.");
        Require(RuntimeInformation.FrameworkDescription == ".NET 10.0.12", "Run this proof producer on the pinned target runtime 10.0.12.");
        ValidateConfiguration(config);
        Require(Path.GetFileName(tool) == "Stryker.Core.dll" && AssemblyName.GetAssemblyName(tool).Version == new Version(4, 16, 0, 0),
            "The child selector proof is pinned to Stryker.Core 4.16.0.0.");
        string selectorSource = SelectorSource(tool);
        foreach (string fragment in new[] { "public static int ActiveMutant = -2;", "public static bool CaptureCoverage;",
            "STRYKER_MUTANT_ID_CONTROL_VAR", "STRYKER_MUTANT_FILE", "public static bool IsActive(int id)",
            "ActiveMutant = int.Parse(environmentVariable);", "return id == ActiveMutant;" })
        {
            Require(selectorSource.Contains(fragment, StringComparison.Ordinal), "Pinned Stryker selector mechanics changed: " + fragment);
        }
        using JsonDocument sdk = JsonDocument.Parse(File.ReadAllText(Path.Combine(language, "global.json")));
        Require(sdk.RootElement.GetProperty("sdk").GetProperty("version").GetString() == "10.0.401"
            && sdk.RootElement.GetProperty("sdk").GetProperty("rollForward").GetString() == "disable", "The SDK source pin changed.");
        AuditLaunchSites(language);
        FilePin[] sources = SourceInputs(language);
        Require(sources.Length > 0 && new[]
        {
            "src/CIS.Core/CIS.Core.csproj",
            "tests/CIS.Core.IntegrationTests/Healthcheck/Reporting/HealthcheckHttpTransportTests.cs",
            "tests/CIS.Core.IntegrationTests/Healthcheck/Reporting/HealthcheckProbeProcessTests.cs",
            "eng/CIS.DependencyChecker/HealthcheckQualification/Mutation/Children/HealthcheckMutationChildSupport.cs",
            "eng/CIS.DependencyChecker/HealthcheckQualification/Mutation/Children/HealthcheckMutationChildObserver.cs"
        }.All(required => sources.Any(pin => pin.Path == required)), "Child proof source inventory omitted a required maintained input.");
        string coreLibrary = typeof(object).Assembly.Location;
        var session = new Session(Schema, language, output, config, Hash(config), tool, Hash(tool), Digest(selectorSource),
            dotnet, Hash(dotnet), coreLibrary, Hash(coreLibrary), sources, DateTimeOffset.UtcNow,
            CoverageAnalysisOff: true, AllowInitialUninstrumentedBaseline: phase is "automatic" or "source-replay", SourceReplay: sourceReplay,
            RequireWindowsErrorMode: OperatingSystem.IsWindows());
        Write(Path.Combine(output, "session.json"), session);
        File.WriteAllText(Path.Combine(output, "pinned-MutantControl.cs.txt"), selectorSource);
        Directory.CreateDirectory(Path.Combine(output, "launches"));
        Directory.CreateDirectory(Path.Combine(output, "stages"));
        Console.WriteLine(Path.Combine(output, "session.json"));
    }

    private static void Audit(Dictionary<string, string> options)
    {
        Only(options, "session", "output", "require-kind", "require-active", "phase");
        string sessionPath = Full(options, "session"), output = Empty(options);
        Session session = Read<Session>(sessionPath);
        Require(session.Schema == Schema && session.CoverageAnalysisOff, "Unknown or coverage-selected child proof session.");
        string phase = options.GetValueOrDefault("phase", "automatic");
        Require((phase is "automatic" or "source-replay") && (phase == "source-replay") == (session.SourceReplay is not null),
            "Automatic and manual source-replay child audits cannot substitute for one another.");
        Require(!Under(session.OutputRoot, output) && !Under(output, session.OutputRoot), "Audit output must be separate from child receipts.");
        var failures = new List<string>();
        var rows = new List<object>();
        var launches = new List<Launch>();
        var verifiedStages = new HashSet<string>(StringComparer.Ordinal);
        string requiredKind = options.GetValueOrDefault("require-kind", "both");
        Require(requiredKind is "http-host" or "probe" or "both", "Unknown required child kind.");
        int? requiredActive = options.TryGetValue("require-active", out string? value)
            ? int.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture) : null;
        Require(requiredActive is null or >= 0, "A required active mutant must be nonnegative.");
        Require(session.SourceReplay is null || requiredActive is null, "Manual source replay has no Stryker active-mutant selector.");
        Check(() =>
        {
            ValidateConfiguration(session.ConfigurationPath);
            Require(Hash(session.ConfigurationPath) == session.ConfigurationSha256 && Hash(session.StrykerCorePath) == session.StrykerCoreSha256
                && Digest(SelectorSource(session.StrykerCorePath)) == session.SelectorSourceSha256, "The native tool/config selector inputs changed.");
            Require(Hash(session.DotnetPath) == session.DotnetSha256 && Hash(session.CoreLibraryPath) == session.CoreLibrarySha256,
                "The pinned native host/runtime changed.");
            FilePin[] currentSources = SourceInputs(session.LanguageRoot);
            Require(currentSources.SequenceEqual(session.SourceInputs), "The exact maintained source/structural input set changed during the child proof.");
            AuditLaunchSites(session.LanguageRoot);
            if (session.SourceReplay is { } sourceReplay) { ValidateSourceReplay(session.LanguageRoot, sourceReplay); }
        });
        string[] directories = Directory.GetDirectories(Path.Combine(session.OutputRoot, "launches"));
        Require(directories.Length is > 0 and <= 200000, "No bounded actual child launch evidence was retained.");
        foreach (string directory in directories.Order(StringComparer.Ordinal))
        {
            Check(() =>
            {
                string[] files = Directory.GetFiles(directory).Select(Path.GetFileName).Select(file => file!).Order(StringComparer.Ordinal).ToArray();
                string[] allowed = ["exit.json", "launch.json", "managed-failure.json", "parent-exit.json", "receipt.json"];
                Require(files.All(allowed.Contains) && new[] { "launch.json", "receipt.json", "parent-exit.json" }.All(files.Contains)
                    && (files.Contains("exit.json") || files.Contains("managed-failure.json")),
                    "Missing terminal/parent receipt, forced stop, timeout, or lifecycle guard failure: " + directory);
                string launchPath = Path.Combine(directory, "launch.json");
                Launch launch = Read<Launch>(launchPath);
                Receipt receipt = Read<Receipt>(Path.Combine(directory, "receipt.json"));
                ParentExitReceipt parent = Read<ParentExitReceipt>(Path.Combine(directory, "parent-exit.json"));
                ExitReceipt? exit = files.Contains("exit.json") ? Read<ExitReceipt>(Path.Combine(directory, "exit.json")) : null;
                bool managed = files.Contains("managed-failure.json");
                string terminalKind = "normal";
                Require(session.SourceReplay is null || !managed, "Manual source replay requires complete normal parent/child exits.");
                Require(launch.Schema == Schema && receipt.Schema == Schema && launch.Id == Path.GetFileName(directory)
                    && receipt.LaunchId == launch.Id && receipt.LaunchSha256 == Hash(launchPath), "Mismatched child receipt identity.");
                Require(string.Equals(Path.GetFullPath(launch.SessionPath), sessionPath, PathComparison) && launch.SessionSha256 == Hash(sessionPath),
                    "A child belongs to another or changed session.");
                Require(receipt.Failure is null && receipt.Pid > 0 && receipt.Pid != launch.ParentPid && receipt.Pid == parent.Pid
                    && parent.Schema == Schema && parent.ParentPid == launch.ParentPid && parent.LaunchId == launch.Id
                    && parent.LaunchSha256 == Hash(launchPath) && !parent.ForcedStop && !parent.TimedOut
                    && launch.CreatedUtc >= session.CreatedUtc && receipt.ObservedUtc >= launch.CreatedUtc
                    && parent.ObservedStartedUtc >= launch.CreatedUtc && parent.ObservedStartedUtc <= parent.ObservedUtc
                    && parent.StartedUtc <= parent.ExitedUtc && parent.ExitedUtc >= receipt.ObservedUtc && parent.ObservedUtc >= parent.ExitedUtc,
                    "Invalid actual child process lifecycle receipt.");
                if (session.RequireWindowsErrorMode)
                {
                    WindowsErrorModeReceipt? errorMode = receipt.WindowsErrorMode;
                    Require(errorMode is not null && errorMode.Pid == receipt.Pid
                        && errorMode.Before == errorMode.SetReturnedPrevious
                        && errorMode.Requested == (errorMode.Before | NoWindowsErrorReporting)
                        && errorMode.After == errorMode.Requested
                        && errorMode.ObservedUtc >= launch.CreatedUtc && errorMode.ObservedUtc <= receipt.ObservedUtc,
                        WindowsErrorModeError);
                }
                Require(string.Equals(parent.FileName, session.DotnetPath, PathComparison)
                    && string.Equals(parent.WorkingDirectory, launch.Stage.Directory, PathComparison)
                    && parent.Arguments.Length >= 6 && parent.Arguments.Take(5).SequenceEqual(["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable"])
                    && parent.Arguments.Skip(5).SequenceEqual(receipt.Arguments), "The actual parent launch argv/runtime differs from the child origin.");
                if (exit is not null)
                {
                    Require(exit.LaunchId == launch.Id && exit.Pid == parent.Pid && exit.ExitCode == parent.ExitCode
                        && exit.ExitedUtc >= receipt.ObservedUtc && parent.ExitedUtc >= exit.ExitedUtc,
                        "The child normal-exit receipt and actual parent exit disagree.");
                }
                if (managed)
                {
                    ManagedFailureReceipt failure = Read<ManagedFailureReceipt>(Path.Combine(directory, "managed-failure.json"));
                    bool http = launch.Stage.AssemblyFile == "CIS.Healthcheck.HttpFixture.dll" && failure.ExceptionType == typeof(ArgumentException).FullName;
                    bool probe = launch.Stage.AssemblyFile == "CisHealthProbe.dll" && failure.ExceptionType == typeof(NullReferenceException).FullName;
                    Require(http || probe, "The managed failure kind/exception is not one of the two exact Core boundaries.");
                    Require(failure.Schema == Schema && failure.LaunchId == launch.Id && failure.LaunchSha256 == Hash(launchPath)
                        && failure.OriginReceiptSha256 == Hash(Path.Combine(directory, "receipt.json")) && failure.Pid == parent.Pid
                        && failure.IsTerminating && failure.FrameIndex == 0
                        && launch.Instrumented && launch.ActiveMutant >= 0 && failure.ActiveMutant == launch.ActiveMutant
                        && parent.ExitCode != 0
                        && failure.ObservedUtc >= receipt.ObservedUtc && parent.ExitedUtc >= failure.ObservedUtc
                        && string.Equals(failure.CorePath, receipt.CorePath, PathComparison)
                        && failure.CoreSha256 == receipt.CoreSha256 && failure.CoreMvid == receipt.CoreMvid,
                        "The managed failure does not completely attribute the actual terminating Core exception.");
                    if (http) { VerifyRegistrationFrame(failure.CorePath, failure.CoreMvid, failure.MetadataToken, failure.DeclaringType, failure.MethodName); }
                    else { VerifyProbeNullFrame(failure.CorePath, failure.CoreMvid, failure.MetadataToken, failure.DeclaringType, failure.MethodName); }
                    terminalKind = probe ? "managed-core-probe-null" : "managed-core-registration";
                }
                else { Require(exit is not null, "Normal completion requires both actual parent and child exit receipts."); }
                Require(receipt.Runtime == ".NET 10.0.12" && receipt.DotnetSha256 == session.DotnetSha256
                    && receipt.CoreLibrarySha256 == session.CoreLibrarySha256, "A child used a different runtime.");
                Require(launch.ParentCoreSha256 == launch.Stage.CoreSha256 && receipt.CoreSha256 == launch.ParentCoreSha256
                    && launch.ParentCoreMvid == launch.Stage.CoreMvid && receipt.CoreMvid == launch.ParentCoreMvid
                    && receipt.ControllerType == launch.ControllerType && launch.ControllerType == launch.Stage.ControllerType
                    && receipt.Instrumented == launch.Instrumented && receipt.ActiveMutant == launch.ActiveMutant,
                    "The selected child Core/controller/selector differs from its actual parent.");
                Require(launch.Instrumented == (launch.ControllerType is not null) && launch.ActiveMutant >= -1,
                    "Unknown child instrumentation/baseline phase.");
                if (session.SourceReplay is { } control)
                {
                    Require(!launch.Instrumented && launch.ControllerType is null && launch.ActiveMutant == -1
                        && launch.ParentCoreSha256 == control.CoreSha256 && launch.ParentCoreMvid == control.CoreMvid,
                        "Manual source replay did not load its exact pinned uninstrumented Core.");
                }
                Require(Guid.TryParseExact(launch.StageOwnerId, "N", out Guid stageOwner) && stageOwner != Guid.Empty
                    && launch.StageOwnerId == stageOwner.ToString("N"), "Invalid child stage owner identity.");
                string ownerRoot = Path.Combine(session.OutputRoot, "stages", launch.ParentPid.ToString(CultureInfo.InvariantCulture), launch.StageOwnerId);
                string expectedStage = Path.Combine(ownerRoot, launch.ParentCoreSha256, Path.GetFileNameWithoutExtension(launch.Stage.AssemblyFile));
                Require(Under(ownerRoot, launch.Stage.Directory)
                    && string.Equals(Path.GetFullPath(launch.Stage.Directory), Path.GetFullPath(expectedStage), PathComparison),
                    "A child stage does not match its exact parent owner/Core/executable identity.");
                Require(string.Equals(receipt.CorePath, Path.Combine(launch.Stage.Directory, "CIS.Core.dll"), PathComparison)
                    && string.Equals(receipt.EntryAssemblyPath, Path.Combine(launch.Stage.Directory, launch.Stage.AssemblyFile), PathComparison),
                    "A child escaped its private per-test-host stage.");
                if (verifiedStages.Add(Digest(JsonSerializer.Serialize(launch.Stage, JsonOptions))))
                {
                    VerifyFiles(launch.Stage.Directory, launch.Stage.DeployedFiles);
                    VerifyFiles(launch.Stage.OriginalDirectory, launch.Stage.OriginalFiles);
                }
                Require(Hash(launch.ParentAssemblyPath) == launch.ParentAssemblySha256, "The integration test host input changed.");
                Require(receipt.EntryAssemblyName == launch.Stage.AssemblyName
                    && receipt.EntryAssemblySha256 == launch.Stage.DeployedFiles.Single(pin => pin.Path == launch.Stage.AssemblyFile).Sha256,
                    "The actual real executable identity changed.");
                foreach (FilePin pin in launch.Stage.OriginalFiles.Where(pin => pin.Path is not "CIS.Core.dll" and not "CIS.Core.pdb"))
                {
                    Require(launch.Stage.DeployedFiles.Single(item => item.Path == pin.Path).Sha256 == pin.Sha256, "A non-Core child dependency was replaced.");
                }
                launches.Add(launch);
                rows.Add(new
                {
                    launch.Id,
                    launch.ParentPid,
                    launch.StageOwnerId,
                    receipt.Pid,
                    kind = Kind(launch),
                    launch.Instrumented,
                    launch.ActiveMutant,
                    receipt.CoreSha256,
                    receipt.CoreMvid,
                    receipt.ControllerType,
                    receipt.EntryAssemblyName,
                    receipt.EntryAssemblySha256,
                    receipt.Runtime,
                    receipt.WindowsErrorMode,
                    parent.ExitCode,
                    terminalKind,
                    receipt.Arguments,
                    launch.CreatedUtc,
                    parent.ExitedUtc
                });
            });
        }
        Check(() =>
        {
            if (session.SourceReplay is { } control)
            {
                Require(launches.Count > 0 && launches.All(launch => !launch.Instrumented && launch.ControllerType is null && launch.ActiveMutant == -1
                    && launch.ParentCoreSha256 == control.CoreSha256 && launch.ParentCoreMvid == control.CoreMvid), "No complete exact-Core manual source-replay children.");
                foreach (string kind in requiredKind == "both" ? new[] { "http-host", "probe" } : [requiredKind])
                { Require(launches.Any(launch => Kind(launch) == kind), "No actual source-replay child of required kind: " + kind); }
            }
            else
            {
                Launch[] instrumented = launches.Where(launch => launch.Instrumented).ToArray();
                Require(instrumented.Length > 0, "No actual child loaded an instrumented Core.");
                DateTimeOffset firstInstrumented = instrumented.Min(launch => launch.CreatedUtc);
                Require(launches.Where(launch => !launch.Instrumented).All(launch => session.AllowInitialUninstrumentedBaseline
                    && launch.ActiveMutant == -1 && launch.CreatedUtc < firstInstrumented), "Uninstrumented/unknown child baseline after mutation instrumentation began.");
                foreach (string kind in requiredKind == "both" ? new[] { "http-host", "probe" } : [requiredKind])
                {
                    Require(instrumented.Any(launch => Kind(launch) == kind && launch.ActiveMutant >= 0
                        && (requiredActive is null || launch.ActiveMutant == requiredActive)), "No selected actual instrumented child of required kind: " + kind);
                }
            }
            Require(launches.GroupBy(launch => (launch.ParentPid, launch.StageOwnerId))
                .All(group => group.Select(launch => launch.ParentCoreSha256).Distinct(StringComparer.Ordinal).Count() == 1),
                "A reused test host changed its loaded Core during child qualification.");
        });
        Write(Path.Combine(output, "results.json"), new
        {
            schema = Schema,
            scope = session.SourceReplay is null ? "Actual real child Core/selector/runtime/dependency propagation; no native kill score or release claim."
                : "Actual manual source-replay Core/runtime/dependency/lifecycle propagation; no Stryker selector, automatic kill score or release claim.",
            status = failures.Count == 0 ? session.SourceReplay is null ? "QualifiedChildPropagation" : "SourceReplayChildPropagation" : "InvalidChildEvidence",
            exitCode = failures.Count == 0 ? 0 : 1,
            sessionPath,
            sessionSha256 = Hash(sessionPath),
            coverageAnalysis = "off; all selected native tests must run for every mutant",
            requiredKind,
            requiredActive,
            phase,
            session.RequireWindowsErrorMode,
            sourceReplayControl = session.SourceReplay,
            launches = rows.Count,
            instrumentedLaunches = launches.Count(launch => launch.Instrumented),
            selectedLaunches = launches.Count(launch => launch.ActiveMutant >= 0),
            rows,
            failures
        });
        Require(failures.Count == 0, string.Join(Environment.NewLine, failures));
        Console.WriteLine(session.SourceReplay is null ? "Qualified actual child propagation; native mutation outcomes must be assessed separately."
            : "Verified actual source-replay child propagation; native assertion outcomes remain separate.");

        void Check(Action action)
        {
            try { action(); }
            catch (Exception error) { failures.Add(error.Message); }
        }
    }

    private static string Kind(Launch launch) => launch.Stage.AssemblyFile switch
    {
        "CIS.Healthcheck.HttpFixture.dll" => "http-host",
        "CisHealthProbe.dll" => "probe",
        _ => throw new InvalidOperationException("Unknown real child executable.")
    };

    private static void ValidateSourceReplay(string language, SourceReplayControl control)
    {
        Require(control.Kind is "mutant" or "historical-baseline" or "canonical-baseline", "Unknown manual source-replay control kind.");
        Require(new[] { control.ReportSha256, control.OriginalSourceSha256, control.CanonicalSourceSha256, control.CurrentSourceSha256, control.CoreSha256 }
            .All(hash => hash.Length == 64 && hash.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f')),
            "Manual source-replay pins must be exact lower-case SHA-256 values.");
        Require(control.SourcePath == "src/CIS.Core/Healthcheck/AspNetCore/HealthcheckApplicationBuilderExtensions.cs",
            "The manual source-replay phase is bounded to the reviewed ASP.NET source.");
        Require(new FileInfo(control.ReportPath).Length <= 32 * 1024 * 1024 && Hash(control.ReportPath) == control.ReportSha256,
            "The immutable manual source-replay report changed.");
        using JsonDocument report = JsonDocument.Parse(File.ReadAllText(control.ReportPath));
        JsonProperty[] files = report.RootElement.GetProperty("files").EnumerateObject()
            .Where(file => file.Name.Replace('\\', '/').EndsWith("/" + control.SourcePath, StringComparison.Ordinal)).ToArray();
        Require(files.Length == 1, "The manual source-replay report source is ambiguous or missing.");
        string original = files[0].Value.GetProperty("source").GetString() ?? throw new InvalidOperationException("Missing original replay source.");
        Require(Digest(original) == control.OriginalSourceSha256 && Hash(Input(language, control.SourcePath)) == control.CurrentSourceSha256,
            "The exact manual original/current source pin changed.");
        if (control.Kind == "mutant")
        {
            JsonElement[] definitions = files[0].Value.GetProperty("mutants").EnumerateArray()
                .Where(row => row.GetProperty("id").GetString() == control.OriginalId).ToArray();
            Require(definitions.Length == 1 && (definitions[0].GetProperty("status").GetString() is "CompileError" or "Ignored"),
                "The manual source replacement is not an exact original compiler/filter omission.");
            JsonElement row = definitions[0], location = row.GetProperty("location");
            int start = Offset(location.GetProperty("start")), end = Offset(location.GetProperty("end"));
            Require(end > start && control.Offset == start && original[start..end] == control.OriginalText
                && row.GetProperty("replacement").GetString() == control.Replacement && control.OriginalText != control.Replacement
                && Digest(original[..start] + control.Replacement + original[end..]) == control.CurrentSourceSha256,
                "The manual single-replacement source does not reproduce its exact native definition.");
        }
        else
        {
            Require(control.OriginalId.Length == 0 && control.Offset == 0 && control.OriginalText.Length == 0 && control.Replacement.Length == 0
                && control.CurrentSourceSha256 == (control.Kind == "historical-baseline" ? control.OriginalSourceSha256 : control.CanonicalSourceSha256),
                "The explicit manual baseline source identity changed.");
        }
        Require(File.Exists(control.CorePath) && (File.GetAttributes(control.CorePath) & FileAttributes.ReparsePoint) == 0
            && Path.GetFileName(control.CorePath) == "CIS.Core.dll" && Hash(control.CorePath) == control.CoreSha256
            && control.CoreMvid != Guid.Empty && Mvid(control.CorePath) == control.CoreMvid, "The actually built manual Core bytes/MVID changed.");

        int Offset(JsonElement point)
        {
            int line = point.GetProperty("line").GetInt32(), column = point.GetProperty("column").GetInt32(), offset = 0;
            Require(line > 0 && column > 0, "Manual native source locations are one-based.");
            for (int current = 1; current < line; current++) { int next = original.IndexOf('\n', offset); Require(next >= 0, "Manual source line exceeds its source."); offset = next + 1; }
            int end = original.IndexOf('\n', offset); if (end < 0) { end = original.Length; }
            Require(column - 1 <= end - offset, "Manual source column exceeds its line.");
            return offset + column - 1;
        }
    }

    private static void ValidateConfiguration(string path)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement config = document.RootElement.GetProperty("stryker-config");
        Require(config.GetProperty("coverage-analysis").GetString() == "off" && config.GetProperty("test-runner").GetString() == "vstest"
            && config.GetProperty("target-framework").GetString() == "net10.0" && config.GetProperty("configuration").GetString() == "Release",
            "Child mutation proof requires explicit coverage-analysis off and the pinned Release/net10.0 VSTest lane.");
        Require(config.GetProperty("test-projects").EnumerateArray().Any(project => project.GetString()?.EndsWith("CIS.Core.IntegrationTests.csproj", StringComparison.Ordinal) == true),
            "The real integration tests must be selected in the native Stryker configuration.");
    }

    private static void AuditLaunchSites(string language)
    {
        string reporting = Path.GetFullPath(Path.Combine(language, "tests/CIS.Core.IntegrationTests/Healthcheck/Reporting"));
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["HealthcheckHttpTransportTests.cs"] = "Process.Start(TransportTestSupport.StartInfo(\"healthcheck-http-host\", \"CIS.Healthcheck.HttpFixture\", [mode]))",
            ["HealthcheckPipeTransportTests.cs"] = "Process.Start(TransportTestSupport.StartInfo(\"healthcheck-http-host\", \"CIS.Healthcheck.HttpFixture\", [\"pipe-pin-release\"]))",
            ["HealthcheckProbeProcessTests.cs"] = "Process.Start(TransportTestSupport.StartInfo(\"healthcheck-probe\", \"CisHealthProbe\", arguments))"
        };
        foreach (string path in Directory.EnumerateFiles(Path.Combine(language, "tests"), "*.cs", SearchOption.AllDirectories)
            .Where(path => path.Contains("Healthcheck", StringComparison.Ordinal) && !Ignored(Path.GetRelativePath(language, path))))
        {
            string source = File.ReadAllText(path);
            if (source.Contains("Process.Start(", StringComparison.Ordinal))
            {
                Require(string.Equals(Path.GetDirectoryName(path), reporting, PathComparison)
                    && expected.TryGetValue(Path.GetFileName(path), out string? call) && source.Split("Process.Start(", StringSplitOptions.None).Length == 2
                    && source.Contains(call, StringComparison.Ordinal), "Unreviewed Healthcheck child process launch site: " + path);
                foreach (string hook in new[] { "ObserveStarted(child)", "MarkForcedStop(child,", "MarkTimeout(child,", "CompleteLifecycle(child)" })
                {
                    bool actualAlias = source.Contains("using MutationChildren = CIS.Verification.HealthcheckMutationChildren.HealthcheckMutationChildSupport;", StringComparison.Ordinal);
                    Require(source.Contains("HealthcheckMutationChildSupport." + hook, StringComparison.Ordinal)
                        || actualAlias && source.Contains("MutationChildren." + hook, StringComparison.Ordinal),
                        "Missing reviewed actual parent lifecycle hook: " + path + " " + hook);
                }
                if (Path.GetFileName(path) == "HealthcheckHttpTransportTests.cs")
                {
                    Require(!source.Contains("host.Client", StringComparison.Ordinal)
                        && source.Contains("ObserveHttpAsync(() => client.GetAsync(path))", StringComparison.Ordinal)
                        && source.Contains("ObserveHttpAsync(() => client.SendAsync(request, cancellationToken))", StringComparison.Ordinal)
                        && source.Contains("catch (OperationCanceledException error) when (error.InnerException is TimeoutException)", StringComparison.Ordinal)
                        && source.Contains("MutationChildren.MarkTimeout(child, \"http-request\")", StringComparison.Ordinal),
                        "The reviewed HTTP request timeout observer is missing or bypassed.");
                }
            }
            if (source.Contains("new ProcessStartInfo(", StringComparison.Ordinal))
            {
                Require(Path.GetFileName(path) == "TransportTestSupport.cs" && string.Equals(Path.GetDirectoryName(path), reporting, PathComparison)
                    && source.Contains("HealthcheckMutationChildSupport.Prepare(path, start)", StringComparison.Ordinal),
                    "A Healthcheck child launcher bypasses exact child staging: " + path);
            }
        }
        foreach (string name in expected.Keys) { Require(File.Exists(Path.Combine(reporting, name)), "A reviewed real child launcher is missing."); }
    }

    private static FilePin[] SourceInputs(string language) => Directory.EnumerateFiles(language, "*", SearchOption.AllDirectories)
        .Where(path => !Ignored(Path.GetRelativePath(language, path)) && (Path.GetExtension(path) is ".cs" or ".csproj" or ".props" or ".targets"
            || Path.GetFileName(path) is "global.json" or "Directory.Packages.props" or "packages.lock.json" or "toolchain.json"))
        .Select(path => new FilePin(Path.GetRelativePath(language, path).Replace('\\', '/'), Hash(path)))
        .OrderBy(pin => pin.Path, StringComparer.Ordinal).ToArray();

    private static bool Ignored(string path) => path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        .Any(part => part is "bin" or "obj" or ".artifacts" or ".git" or "StrykerOutput" or "stryker-output");

    private static string SelectorSource(string tool)
    {
        using FileStream stream = File.OpenRead(tool);
        using var pe = new PEReader(stream);
        MetadataReader metadata = pe.GetMetadataReader();
        ManifestResource resource = metadata.ManifestResources.Select(metadata.GetManifestResource)
            .Single(item => metadata.GetString(item.Name) == "Stryker.Core.InjectedHelpers.MutantControl.cs");
        byte[] bytes = pe.GetSectionData(pe.PEHeaders.CorHeader!.ResourcesDirectory.RelativeVirtualAddress).GetContent().ToArray();
        int offset = checked((int)resource.Offset), length = BitConverter.ToInt32(bytes, offset);
        Require(length is > 0 and < 1024 * 1024, "Unexpected pinned injected controller resource size.");
        return Encoding.UTF8.GetString(bytes, offset + 4, length);
    }

    private static Dictionary<string, string> Options(string[] args)
    {
        Require(args.Length % 2 == 0, "Expected --name value arguments.");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 0; index < args.Length; index += 2)
        {
            Require(args[index].StartsWith("--", StringComparison.Ordinal) && result.TryAdd(args[index][2..], args[index + 1]), "Unknown or duplicate child proof option.");
        }
        return result;
    }

    private static void Only(Dictionary<string, string> options, params string[] allowed) => Require(options.Keys.All(allowed.Contains), "Unknown child proof argument.");
    private static string Full(Dictionary<string, string> options, string name) => Path.GetFullPath(options.TryGetValue(name, out string? value)
        ? value : throw new InvalidOperationException("Missing --" + name));
    private static string Empty(Dictionary<string, string> options)
    {
        string output = Full(options, "output");
        Require(!Directory.Exists(output) || !Directory.EnumerateFileSystemEntries(output).Any(), "Use an empty child proof output directory.");
        Directory.CreateDirectory(output);
        return output;
    }
}
