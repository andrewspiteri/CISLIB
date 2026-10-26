using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Stryker.Abstractions;
using Stryker.Abstractions.Options;
using Stryker.Configuration.Options;
using Stryker.Core;
using Stryker.Core.Infrastructure;
using Stryker.Core.MutationTest;
using Stryker.Core.ProjectComponents;
using Stryker.Core.ProjectComponents.Csharp;
using Stryker.Core.ProjectComponents.TestProjects;
using Stryker.Utilities.Logging;

namespace CIS.Verification.HealthcheckMutationInventory;

// Native generation/compilation/filter inventory only. No mutant is executed or credited.
internal static class Program
{
    private const string Suppression = "Inventory only: pending mutant execution intentionally suppressed after native generation, compilation and filtering.";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static async Task<int> Main(string[] args)
    {
        Require(args.Length == 5, "Expected language-root, actual config, empty output, pinned Stryker tool directory, launcher Program.cs.");
        string tools = Path.GetFullPath(args[3]);
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            string path = Path.Combine(tools, name.Name + ".dll");
            return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
        };
        return await Run(args);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<int> Run(string[] args)
    {
        string language = Path.GetFullPath(args[0]), configPath = Path.GetFullPath(args[1]);
        string output = Path.GetFullPath(args[2]), tools = Path.GetFullPath(args[3]), launcherSource = Path.GetFullPath(args[4]);
        Require(Same(Directory.GetCurrentDirectory(), Path.Combine(language, "src", "CIS.Core")), "Launch inventory from the frozen Core project directory so SDK selection uses its real global.json.");
        using JsonDocument sdk = JsonDocument.Parse(File.ReadAllText(Path.Combine(language, "global.json")));
        Require(sdk.RootElement.GetProperty("sdk").GetProperty("version").GetString() == "10.0.401"
            && sdk.RootElement.GetProperty("sdk").GetProperty("rollForward").GetString() == "disable", "The frozen SDK pin changed.");
        Require(!Directory.Exists(output) || !Directory.EnumerateFileSystemEntries(output).Any(), "Inventory output must be empty.");
        Require(!Within(language, output) && !Within(output, language), "Keep inventory artifacts separate from source.");
        Require(File.Exists(launcherSource) && Path.GetFileName(launcherSource) == "Program.cs", "Pin the maintained launcher source.");
        Directory.CreateDirectory(output); Directory.CreateDirectory(Path.Combine(output, "native"));
        using JsonDocument configuration = JsonDocument.Parse(File.ReadAllText(configPath));
        JsonElement config = configuration.RootElement.GetProperty("stryker-config");
        string[] selected = config.GetProperty("mutate").EnumerateArray().Select(item => item.GetString()!.Replace('\\', '/')).ToArray();
        Require(selected.Length is > 0 and <= 64 && selected.Distinct(StringComparer.Ordinal).Count() == selected.Length
            && selected.All(path => path.StartsWith("Healthcheck/", StringComparison.Ordinal) && path.EndsWith(".cs", StringComparison.Ordinal)
                && !Path.IsPathRooted(path) && !path.Contains("..", StringComparison.Ordinal) && path.IndexOfAny(['*', '?', '{', '}', '!']) < 0),
            "This bounded inventory requires explicit unique Healthcheck source paths, not globs or exclusions.");
        Require(config.GetProperty("project").GetString() == "CIS.Core.csproj" && config.GetProperty("coverage-analysis").GetString() == "off"
            && config.GetProperty("test-runner").GetString() == "vstest" && config.GetProperty("configuration").GetString() == "Release"
            && config.GetProperty("target-framework").GetString() == "net10.0" && config.GetProperty("mutation-level").GetString() == "Complete"
            && config.GetProperty("concurrency").GetInt32() == 2 && config.GetProperty("disable-bail").GetBoolean()
            && !config.TryGetProperty("ignore-mutations", out _) && !config.TryGetProperty("ignore-methods", out _), "Unexpected native inventory configuration.");
        string sessionPath = Environment.GetEnvironmentVariable("CIS_HEALTHCHECK_MUTATION_SESSION")
            ?? throw new InvalidOperationException("Prepare an exact child/source session before native inventory.");
        using JsonDocument sessionDocument = JsonDocument.Parse(File.ReadAllText(sessionPath));
        JsonElement session = sessionDocument.RootElement;
        Require(Same(session.GetProperty("LanguageRoot").GetString()!, language) && Same(session.GetProperty("ConfigurationPath").GetString()!, configPath)
            && session.GetProperty("ConfigurationSha256").GetString() == Hash(configPath) && session.GetProperty("CoverageAnalysisOff").GetBoolean(),
            "Native inventory config/source session differs.");
        Require(RuntimeInformation.FrameworkDescription == ".NET 10.0.12" && Environment.GetEnvironmentVariable("CisMutationTesting") == "true",
            "Use the pinned actual runtime and serialized mutation scheduling.");
        Require(typeof(StrykerRunner).Assembly.GetName().Version == new Version(4, 16, 0, 0)
            && Hash(typeof(StrykerRunner).Assembly.Location) == session.GetProperty("StrykerCoreSha256").GetString()
            && Hash(Path.Combine(tools, "Stryker.Core.dll")) == Hash(typeof(StrykerRunner).Assembly.Location), "The real pinned Stryker.Core changed.");
        string process = Environment.ProcessPath ?? throw new InvalidOperationException("Missing actual runtime host.");
        Require(Same(process, session.GetProperty("DotnetPath").GetString()!) && Hash(process) == session.GetProperty("DotnetSha256").GetString()
            && Hash(typeof(object).Assembly.Location) == session.GetProperty("CoreLibrarySha256").GetString(), "The actual host/CoreLib pins differ.");
        Pin[] sourcePins = session.GetProperty("SourceInputs").EnumerateArray()
            .Select(item => new Pin(Path.GetFullPath(Path.Combine(language, item.GetProperty("Path").GetString()!)), item.GetProperty("Sha256").GetString()!)).ToArray();
        Pin[] toolsPins = Directory.GetFiles(tools, "*.dll").Order(StringComparer.Ordinal).Select(path => new Pin(path, Hash(path))).ToArray();
        var immutable = sourcePins.Concat(toolsPins).Concat(new[]
        {
            new Pin(configPath, Hash(configPath)), new Pin(sessionPath, Hash(sessionPath)), new Pin(launcherSource, Hash(launcherSource)),
            new Pin(Assembly.GetExecutingAssembly().Location, Hash(Assembly.GetExecutingAssembly().Location)), new Pin(process, Hash(process)),
            new Pin(typeof(object).Assembly.Location, Hash(typeof(object).Assembly.Location))
        }).ToArray();
        foreach (Pin pin in immutable) { Require(Hash(pin.Path) == pin.Sha256, "Inventory input differs before execution: " + pin.Path); }
        var inputs = new StrykerInputs();
        inputs.BasePathInput.SuppliedInput = Path.Combine(language, "src", "CIS.Core");
        inputs.OutputPathInput.SuppliedInput = Path.Combine(output, "native");
        inputs.SourceProjectNameInput.SuppliedInput = "CIS.Core.csproj";
        inputs.TestProjectsInput.SuppliedInput = config.GetProperty("test-projects").EnumerateArray().Select(item => item.GetString()!).ToArray();
        inputs.TestCaseFilterInput.SuppliedInput = config.GetProperty("test-case-filter").GetString()!;
        inputs.ConfigurationInput.SuppliedInput = "Release"; inputs.TargetFrameworkInput.SuppliedInput = "net10.0";
        inputs.MutationLevelInput.SuppliedInput = "Complete"; inputs.CoverageAnalysisInput.SuppliedInput = "off";
        inputs.TestRunnerInput.SuppliedInput = "vstest"; inputs.ConcurrencyInput.SuppliedInput = 2; inputs.DisableBailInput.SuppliedInput = true;
        inputs.AdditionalTimeoutInput.SuppliedInput = config.GetProperty("additional-timeout").GetInt32();
        inputs.MutateInput.SuppliedInput = selected;
        inputs.ReportersInput.SuppliedInput = config.GetProperty("reporters").EnumerateArray().Select(item => item.GetString()!).ToArray();
        JsonElement thresholds = config.GetProperty("thresholds");
        inputs.ThresholdHighInput.SuppliedInput = thresholds.GetProperty("high").GetInt32();
        inputs.ThresholdLowInput.SuppliedInput = thresholds.GetProperty("low").GetInt32();
        inputs.ThresholdBreakInput.SuppliedInput = thresholds.GetProperty("break").GetInt32();
        Require(inputs.ThresholdLowInput.SuppliedInput == 90 && inputs.ThresholdBreakInput.SuppliedInput == 90, "Keep the actual90% thresholds.");
        using var log = new NativeLog(Path.Combine(output, "native.log"));
        var services = new ServiceCollection();
        services.AddStrykerCore(); services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Debug).AddProvider(log));
        InventoryProcess? adapter = null;
        services.AddSingleton<IMutationProcess>(provider => adapter = new InventoryProcess(
            ActivatorUtilities.CreateInstance<CsharpMutationProcess>(provider), language, output, selected));
        using ServiceProvider provider = services.BuildServiceProvider();
        ApplicationLogging.LoggerFactory = provider.GetRequiredService<ILoggerFactory>();
        var failures = new List<string>(); bool completed = false, restored = false;
        try
        {
            _ = await provider.GetRequiredService<IStrykerRunner>().RunMutationTestAsync(inputs);
            Require(adapter is not null && adapter.Captured, "Native compilation/filter inventory was not captured.");
            adapter!.VerifySuppressed();
            Require(log.MutantTestRuns == 0, "A native mutant test session ran during inventory-only execution.");
            completed = true;
        }
        catch (Exception error) { failures.Add(error.ToString()); Console.Error.WriteLine(error); }
        finally
        {
            try { Require(adapter is not null, "The native mutation process was not initialized."); adapter!.CleanupAndVerify(); restored = adapter.ExactRestorationVerified; }
            catch (Exception error) { failures.Add(error.ToString()); }
            try
            {
                foreach (Pin pin in immutable) { Require(Hash(pin.Path) == pin.Sha256, "Inventory input changed: " + pin.Path); }
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies().Where(item => !item.IsDynamic && item.Location.Length > 0
                    && (item.GetName().Name?.StartsWith("Stryker.", StringComparison.Ordinal) == true || Within(AppContext.BaseDirectory, item.Location))
                    && !Same(item.Location, Assembly.GetExecutingAssembly().Location)))
                {
                    Pin pin = toolsPins.Single(item => Path.GetFileName(item.Path) == Path.GetFileName(assembly.Location));
                    Require(Hash(assembly.Location) == pin.Sha256, "A loaded private/native-tool dependency differs: " + assembly.Location);
                }
                Require(log.MutantTestRuns == 0, "Native mutant-test execution was observed.");
            }
            catch (Exception error) { failures.Add(error.ToString()); }
            File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new
            {
                schema = "cis.healthcheck.native-mutation-inventory/v2-development", inventoryOnly = true, nativeMutantKillOutcomes = adapter?.KilledOutcomes,
                scope = "Real pinned Stryker full generation, compilation and native filters; pending execution intentionally suppressed. No mutation score, kill, equivalence or release claim.",
                status = failures.Count == 0 && completed && restored ? "InventoryCapturedWithVerifiedCleanup" : "InventoryFailed",
                exitCode = failures.Count == 0 && completed && restored ? 0 : 1,
                configPath, configSha256 = Hash(configPath), sessionPath, sessionSha256 = Hash(sessionPath), launcherSource,
                pinnedInputs = immutable, selectedSources = selected, log.MutantTestRuns,
                automaticRestorationObserved = adapter?.AutomaticRestorationObserved,
                explicitCleanupRequested = adapter?.ExplicitCleanupRequested ?? false,
                explicitCleanupCompleted = adapter?.ExplicitCleanupCompleted ?? false, exactRestorationVerified = restored,
                inventoryPath = adapter?.Captured == true ? Path.Combine(output, "inventory.json") : null,
                inventorySha256 = adapter?.Captured == true ? Hash(Path.Combine(output, "inventory.json")) : null,
                allGeneratedPath = adapter?.Generated == true ? Path.Combine(output, "all-generated.json") : null,
                allGeneratedSha256 = adapter?.Generated == true ? Hash(Path.Combine(output, "all-generated.json")) : null,
                injections = adapter?.InjectionEvidence(), failures
            }, Json) + "\n");
        }
        return failures.Count == 0 && completed && restored ? 0 : 1;
    }

    private sealed class InventoryProcess(CsharpMutationProcess native, string language, string output, string[] selected) : IMutationProcess
    {
        private readonly Dictionary<string, byte[]> originals = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> backups = new(StringComparer.Ordinal), instrumented = new(StringComparer.Ordinal);
        private readonly Dictionary<int, (MutantStatus Status, string? Reason)> before = [];
        private readonly Dictionary<int, (MutantStatus Status, string? Reason)> compiled = [];
        private MutationTestInput? input;
        private string? sourceAssembly, sourceSha;
        internal bool Captured { get; private set; }
        internal bool Generated { get; private set; }
        internal bool? AutomaticRestorationObserved { get; private set; }
        internal bool ExplicitCleanupRequested { get; private set; }
        internal bool ExplicitCleanupCompleted { get; private set; }
        internal bool ExactRestorationVerified { get; private set; }
        internal int? KilledOutcomes => input is null ? null
            : input.SourceProjectInfo.ProjectContents.Mutants.Count(mutant => mutant.ResultStatus == MutantStatus.Killed);
        public void Mutate(MutationTestInput actualInput, IStrykerOptions options)
        {
            Require(input is null, "Inventory supports exactly one real Core project."); input = actualInput;
            sourceAssembly = Path.GetFullPath(input.SourceProjectInfo.AnalyzerResult.Properties["TargetPath"]);
            Require(Within(Path.Combine(language, "src", "CIS.Core", "bin", "Release", "net10.0"), sourceAssembly)
                && Path.GetFileName(sourceAssembly) == "CIS.Core.dll", "Unexpected actual freshly built Core target.");
            byte[] built = File.ReadAllBytes(sourceAssembly); sourceSha = Hash(sourceAssembly);
            foreach (var test in input.SourceProjectInfo.TestProjectsInfo.AnalyzerResults)
            {
                string path = Path.GetFullPath(TestProjectsInfo.GetInjectionFilePath(test, input.SourceProjectInfo.AnalyzerResult));
                Require(Within(Path.Combine(language, "tests"), path) && Path.GetFileName(path) == "CIS.Core.dll"
                    && path.Replace('\\', '/').Contains("/bin/Release/net10.0/", StringComparison.Ordinal), "Unexpected actual injection target.");
                string backup = path + ".stryker-unchanged"; byte[] bytes = File.ReadAllBytes(backup);
                Require(bytes.SequenceEqual(built) && (!File.Exists(path) || File.ReadAllBytes(path).SequenceEqual(bytes)), "Native source/backup/live Core mismatch.");
                originals.Add(path, bytes); backups.Add(path, backup);
            }
            Require(originals.Count > 0, "No actual native original backups.");
            native.Mutate(input, options); // Unchanged complete built-in orchestrator, compiler, rollbacks and deployment.
            foreach (string path in originals.Keys) { instrumented.Add(path, Hash(path)); }
            var root = (ProjectComponent)input.SourceProjectInfo.ProjectContents;
            foreach (var mutant in root.Mutants) { compiled.Add(mutant.Id, (mutant.ResultStatus, mutant.ResultStatusReason)); }
            File.WriteAllText(Path.Combine(output, "all-generated.json"), JsonSerializer.Serialize(new
            {
                inventoryOnly = true, phase = "After delegated native generation/compilation, before delegated mutate filters.",
                preCompileStatusObserved = false,
                observationLimit = "The public CsharpMutationProcess.Mutate includes compilation internally; no earlier status is inferred.",
                globalGeneratedCount = compiled.Count,
                nativeStatusesAfterCompilation = compiled.Values.GroupBy(item => item.Status.ToString()).ToDictionary(group => group.Key, group => group.Count()),
                files = CaptureFiles(root.GetAllFiles().OfType<CsharpFileLeaf>())
            }, Json) + "\n");
            Generated = true;
        }
        public void FilterMutants(MutationTestInput actualInput)
        {
            Require(ReferenceEquals(input, actualInput) && !Captured, "Unexpected inventory phase/project.");
            native.FilterMutants(actualInput);
            var root = (ProjectComponent)actualInput.SourceProjectInfo.ProjectContents;
            var all = root.Mutants.OrderBy(mutant => mutant.Id).ToArray();
            Require(all.Length > 0 && all.Select(mutant => mutant.Id).SequenceEqual(Enumerable.Range(0, all.Length)), "Native global IDs are not unique contiguous generation IDs.");
            var selectedFiles = root.GetAllFiles().OfType<CsharpFileLeaf>()
                .Where(file => selected.Contains(Path.GetRelativePath(Path.Combine(language, "src", "CIS.Core"), file.SyntaxTree.FilePath).Replace('\\', '/'), StringComparer.Ordinal)).ToArray();
            Require(selectedFiles.Length == selected.Length, "Native selected source inventory differs from actual config.");
            FileInventory[] files = CaptureFiles(selectedFiles);
            foreach (var mutant in all)
            {
                Require(mutant.ResultStatus is MutantStatus.Pending or MutantStatus.Ignored or MutantStatus.CompileError or MutantStatus.NoCoverage, "Unexpected pre-execution native outcome.");
                before.Add(mutant.Id, (mutant.ResultStatus, mutant.ResultStatusReason));
            }
            File.WriteAllText(Path.Combine(output, "inventory.json"), JsonSerializer.Serialize(new
            {
                inventoryOnly = true, globalGeneratedCount = all.Length, selectedGeneratedCount = files.Sum(file => file.Mutants.Length),
                nativeStatusesBeforeSuppression = all.GroupBy(mutant => mutant.ResultStatus.ToString()).ToDictionary(group => group.Key, group => group.Count()),
                allPostCompileAndFilterStates = all.Select(mutant => new { mutant.Id, postCompile = compiled[mutant.Id].Status.ToString(),
                    postCompileReason = compiled[mutant.Id].Reason, postFilter = mutant.ResultStatus.ToString(), postFilterReason = mutant.ResultStatusReason }).ToArray(),
                pendingExecutionSuppressed = all.Count(mutant => mutant.ResultStatus == MutantStatus.Pending), suppressionReason = Suppression,
                helperNamespace = actualInput.SourceProjectInfo.HelperNamespace, sourceAssembly, sourceSha, files
            }, Json) + "\n");
            foreach (var mutant in all.Where(mutant => mutant.ResultStatus == MutantStatus.Pending))
            { mutant.ResultStatus = MutantStatus.Ignored; mutant.ResultStatusReason = Suppression; }
            Captured = true;
            VerifySuppressed();
        }
        private static FileInventory[] CaptureFiles(IEnumerable<CsharpFileLeaf> files) => files
            .OrderBy(file => file.SyntaxTree.FilePath, StringComparer.Ordinal).Select(file =>
            {
                string path = Path.GetFullPath(file.SyntaxTree.FilePath), source = file.SyntaxTree.GetText().ToString();
                Require(source == File.ReadAllText(path), "Native original syntax tree differs from frozen complete source: " + path);
                Definition[] definitions = file.Mutants.OrderBy(mutant => mutant.Id).Select(mutant =>
                {
                    var node = mutant.Mutation.OriginalNode; var span = node.Span; var line = node.GetLocation().GetLineSpan();
                    Require(Same(node.SyntaxTree.FilePath, path) && source.Substring(span.Start, span.Length) == node.ToString(), "Mutation original/source binding differs.");
                    return new Definition(mutant.Id, mutant.Mutation.Type.ToString(), mutant.Mutation.DisplayName, span.Start, span.Length,
                        new Location(new Position(line.StartLinePosition.Line + 1, line.StartLinePosition.Character + 1),
                            new Position(line.EndLinePosition.Line + 1, line.EndLinePosition.Character + 1)),
                        source.Substring(span.Start, span.Length), mutant.Mutation.ReplacementNode.ToFullString(), mutant.Mutation.ReplacementNode.ToString(),
                        mutant.ResultStatus.ToString(), mutant.ResultStatusReason);
                }).ToArray();
                return new FileInventory(path, source, Digest(source), Hash(path), definitions);
            }).ToArray();
        internal void VerifySuppressed()
        {
            Require(Captured && input is not null, "No completed native inventory.");
            var all = input!.SourceProjectInfo.ProjectContents.Mutants.ToArray();
            Require(all.Length == before.Count && all.All(mutant => before.TryGetValue(mutant.Id, out var previous)
                && (previous.Status == MutantStatus.Pending ? mutant.ResultStatus == MutantStatus.Ignored && mutant.ResultStatusReason == Suppression
                    : mutant.ResultStatus == previous.Status && mutant.ResultStatusReason == previous.Reason)), "Native statuses changed beyond intentional pending suppression.");
            Require(all.All(mutant => mutant.ResultStatus is MutantStatus.Ignored or MutantStatus.CompileError or MutantStatus.NoCoverage), "Unexpected pending/tested mutant outcome.");
        }
        internal void CleanupAndVerify()
        {
            Require(sourceAssembly is null || Hash(sourceAssembly) == sourceSha, "Fresh source Core target changed.");
            Require(input is not null && originals.Count > 0, "No actual initialized inventory/native backups to clean up.");
            Require(originals.All(pair => File.ReadAllBytes(backups[pair.Key]).SequenceEqual(pair.Value)), "Native original backup changed.");
            AutomaticRestorationObserved = originals.All(pair => File.Exists(pair.Key) && File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value));
            // An intentionally empty queue can skip the runner's ordinary restoration. This is an explicit inventory cleanup phase.
            ExplicitCleanupRequested = true;
            input!.TestProjectsInfo.RestoreOriginalAssembly(input.SourceProjectInfo.AnalyzerResult);
            ExplicitCleanupCompleted = true;
            Require(originals.All(pair => File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value) && File.ReadAllBytes(backups[pair.Key]).SequenceEqual(pair.Value)), "Original Core restoration verification failed.");
            Require(Hash(sourceAssembly!) == sourceSha, "Fresh source Core changed during inventory cleanup.");
            ExactRestorationVerified = true;
        }
        internal object InjectionEvidence() => originals.Select(pair => new { path = pair.Key, sourceAssembly, sourceSha,
            backupPath = backups[pair.Key], originalSha256 = Convert.ToHexStringLower(SHA256.HashData(pair.Value)),
            backupSha256 = File.Exists(backups[pair.Key]) ? Hash(backups[pair.Key]) : null, instrumentedSha256 = instrumented.GetValueOrDefault(pair.Key),
            restoredSha256 = File.Exists(pair.Key) ? Hash(pair.Key) : null }).ToArray();
    }

    private static void Require(bool condition, string message) { if (!condition) { throw new InvalidOperationException(message); } }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    private static string Digest(string value) => Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
    private static bool Same(string left, string right) => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static bool Within(string root, string path) => Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root))
        + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private sealed record Pin(string Path, string Sha256);
    private sealed record Position(int Line, int Column);
    private sealed record Location(Position Start, Position End);
    private sealed record Definition(int Id, string Type, string DisplayName, int Offset, int Length, Location Location,
        string Original, string Replacement, string ReplacementNode, string NativeStatus, string? NativeReason);
    private sealed record FileInventory(string Path, string Source, string SourceTextSha256, string FileSha256, Definition[] Mutants);
    private sealed class NativeLog(string path) : ILoggerProvider
    {
        private readonly StreamWriter writer = new(path, append: false) { AutoFlush = true };
        private int tested;
        internal int MutantTestRuns => Volatile.Read(ref tested);
        public ILogger CreateLogger(string categoryName) => new Line(this, categoryName);
        public void Dispose() { lock (writer) { writer.Dispose(); } }
        private sealed class Line(NativeLog owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;
            public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                string message = formatter(state, exception);
                if (message.Contains("Test run for ", StringComparison.Ordinal)) { Interlocked.Increment(ref owner.tested); }
                lock (owner.writer) { owner.writer.WriteLine($"{DateTimeOffset.UtcNow:O} [{level}] {category}: {message}"); if (exception is not null) { owner.writer.WriteLine(exception); } }
            }
        }
    }
}
