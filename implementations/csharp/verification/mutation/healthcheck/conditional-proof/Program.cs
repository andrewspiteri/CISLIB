using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Stryker.Abstractions;
using Stryker.Abstractions.Options;
using Stryker.Configuration.Options;
using Stryker.Core;
using Stryker.Core.Compiling;
using Stryker.Core.Infrastructure;
using Stryker.Core.MutantFilters;
using Stryker.Core.Mutants;
using Stryker.Core.Mutators;
using Stryker.Core.MutationTest;
using Stryker.Core.ProjectComponents;
using Stryker.Core.ProjectComponents.Csharp;
using Stryker.Core.ProjectComponents.TestProjects;
using Stryker.Utilities.Logging;

namespace CIS.Verification.HealthcheckConditionalProof;

// Finite supplemental diagnostic. This cannot satisfy the untouched broad Stryker/release gate.
internal static class Program
{
    internal static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static async Task<int> Main(string[] args)
    {
        Require(args.Length == 4 || args.Length == 5 && args[4] == "--registration",
            "Expected language-root, actual config, empty output, pinned Stryker tool directory, optionally --registration.");
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
        string language = Path.GetFullPath(args[0]), configPath = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[2]);
        bool registration = args.Length == 5;
        int expected = registration ? 3 : 4;
        Require(!Directory.Exists(output) || !Directory.EnumerateFileSystemEntries(output).Any(), "Use an empty proof output.");
        Require(!Within(language, output) && !Within(output, language), "Keep proof artifacts separate from source inputs.");
        Directory.CreateDirectory(output);
        Directory.CreateDirectory(Path.Combine(output, "native"));
        using JsonDocument configDocument = JsonDocument.Parse(File.ReadAllText(configPath));
        JsonElement config = configDocument.RootElement.GetProperty("stryker-config");
        int additionalTimeout = registration ? 60000 : 10000;
        Require(config.GetProperty("additional-timeout").GetInt32() == additionalTimeout,
            "Use the recorded finite timeout: 10 seconds for conditional proof or 60 seconds for one-case registration proof.");
        string sessionPath = Environment.GetEnvironmentVariable("CIS_HEALTHCHECK_MUTATION_SESSION") ?? throw new InvalidOperationException("A prepared child proof session is required.");
        using JsonDocument sessionDocument = JsonDocument.Parse(File.ReadAllText(sessionPath));
        JsonElement session = sessionDocument.RootElement;
        Require(session.GetProperty("LanguageRoot").GetString() == language && session.GetProperty("ConfigurationPath").GetString() == configPath
            && session.GetProperty("ConfigurationSha256").GetString() == Hash(configPath), "The actual native configuration/session differs.");
        Require(RuntimeInformation.FrameworkDescription == ".NET 10.0.12" && Environment.GetEnvironmentVariable("CisMutationTesting") == "true",
            "Use the pinned runtime and serialized mutation scheduling.");
        Require(typeof(StrykerRunner).Assembly.GetName().Version == new Version(4, 16, 0, 0)
            && Hash(typeof(StrykerRunner).Assembly.Location) == session.GetProperty("StrykerCoreSha256").GetString(), "The loaded Stryker.Core differs from the pinned tool.");
        Require(config.GetProperty("coverage-analysis").GetString() == "off" && config.GetProperty("test-runner").GetString() == "vstest"
            && config.GetProperty("configuration").GetString() == "Release" && config.GetProperty("target-framework").GetString() == "net10.0"
            && config.GetProperty("mutation-level").GetString() == "Complete", "Unexpected finite proof configuration.");
        Require(config.GetProperty("project").GetString() == "CIS.Core.csproj" && !config.TryGetProperty("ignore-mutations", out _)
            && !config.TryGetProperty("ignore-methods", out _), "No mutation/method exclusions are accepted by this proof.");
        var inputs = new StrykerInputs();
        inputs.BasePathInput.SuppliedInput = Path.Combine(language, "src", "CIS.Core");
        inputs.OutputPathInput.SuppliedInput = Path.Combine(output, "native");
        inputs.SourceProjectNameInput.SuppliedInput = "CIS.Core.csproj";
        inputs.TestProjectsInput.SuppliedInput = config.GetProperty("test-projects").EnumerateArray().Select(item => item.GetString()!).ToArray();
        inputs.TestCaseFilterInput.SuppliedInput = config.GetProperty("test-case-filter").GetString()!;
        inputs.ConfigurationInput.SuppliedInput = "Release"; inputs.TargetFrameworkInput.SuppliedInput = "net10.0";
        inputs.MutationLevelInput.SuppliedInput = "Complete"; inputs.CoverageAnalysisInput.SuppliedInput = "off";
        inputs.TestRunnerInput.SuppliedInput = "vstest"; inputs.ConcurrencyInput.SuppliedInput = 2;
        inputs.DisableBailInput.SuppliedInput = true; inputs.DisableMixMutantsInput.SuppliedInput = true;
        inputs.AdditionalTimeoutInput.SuppliedInput = additionalTimeout;
        inputs.MutateInput.SuppliedInput = config.GetProperty("mutate").EnumerateArray().Select(item => item.GetString()!).ToArray();
        inputs.ReportersInput.SuppliedInput = ["json", "cleartext", "progress"];
        inputs.ThresholdHighInput.SuppliedInput = 100; inputs.ThresholdLowInput.SuppliedInput = 90; inputs.ThresholdBreakInput.SuppliedInput = 90;
        var adapter = new ConditionalProcess(language, output, registration);
        var services = new ServiceCollection();
        services.AddStrykerCore(); services.AddLogging(builder => builder.AddProvider(new NativeLog()));
        services.AddSingleton<IMutationProcess>(adapter);
        using ServiceProvider provider = services.BuildServiceProvider();
        ApplicationLogging.LoggerFactory = provider.GetRequiredService<ILoggerFactory>();
        var failures = new List<string>(); bool nativeRestoration = false; double? score = null; string? reportPath = null;
        try
        {
            StrykerRunResult result = await provider.GetRequiredService<IStrykerRunner>().RunMutationTestAsync(inputs);
            score = result.MutationScore;
            reportPath = Directory.GetFiles(Path.Combine(output, "native"), "mutation-report.json", SearchOption.AllDirectories).Single();
            using JsonDocument report = JsonDocument.Parse(File.ReadAllText(reportPath));
            JsonElement[] mutants = report.RootElement.GetProperty("files").EnumerateObject()
                .SelectMany(file => file.Value.GetProperty("mutants").EnumerateArray()).ToArray();
            Require(mutants.Length == expected && mutants.All(mutant => mutant.GetProperty("status").GetString() == "Killed"),
                "The expected finite actual mutants were not all natively killed.");
        }
        catch (Exception error) { failures.Add(error.ToString()); Console.Error.WriteLine(error); }
        finally
        {
            try { nativeRestoration = adapter.RestoreAndVerify(); Require(nativeRestoration, "Native restoration was incomplete; explicit backup restoration was necessary."); }
            catch (Exception error) { failures.Add(error.ToString()); }
            try
            {
                foreach (JsonElement pin in session.GetProperty("SourceInputs").EnumerateArray())
                { Require(Hash(Path.Combine(language, pin.GetProperty("Path").GetString()!)) == pin.GetProperty("Sha256").GetString(), "Exact maintained input changed."); }
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies().Where(assembly => assembly.GetName().Name?.StartsWith("Stryker.", StringComparison.Ordinal) == true))
                { Require(Hash(assembly.Location) == Hash(Path.Combine(args[3], Path.GetFileName(assembly.Location))), "A loaded Stryker dependency differs from the supplied pinned tool."); }
            }
            catch (Exception error) { failures.Add(error.ToString()); }
            File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new
            {
                scope = registration
                    ? "Three actual pinned-Stryker registration-guard mutants; supplemental finite diagnostic, not broad automatic/release qualification."
                    : "Four actual pinned-Stryker conditional mutants; supplemental finite diagnostic, not broad automatic/release qualification.",
                finiteMode = registration ? "registration" : "conditional", expectedMutants = expected, additionalTimeout,
                status = failures.Count == 0 ? "FiniteNativeProofPassed" : "FiniteNativeProofFailed", exitCode = failures.Count == 0 ? 0 : 1,
                sessionPath, sessionSha256 = Hash(sessionPath), configPath, configSha256 = Hash(configPath),
                strykerCoreSha256 = Hash(typeof(StrykerRunner).Assembly.Location), score, reportPath,
                nativeRestoration, injections = adapter.InjectionEvidence(), failures
            }, Json) + "\n");
        }
        return failures.Count == 0 ? 0 : 1;
    }

    internal static void Require(bool condition, string message) { if (!condition) { throw new InvalidOperationException(message); } }
    internal static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    internal static bool Within(string root, string path) => Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root))
        + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private sealed class ConditionalProcess(string language, string output, bool registration) : IMutationProcess
    {
        private readonly Dictionary<string, byte[]> originals = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> instrumented = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> backups = new(StringComparer.Ordinal);
        private IStrykerOptions? options;
        private MutationTestInput? mutationInput;
        private string? sourceAssemblyPath, sourceAssemblySha256;
        public void Mutate(MutationTestInput input, IStrykerOptions actualOptions)
        {
            Require(options is null, "The finite proof supports one actual Core project only."); options = actualOptions;
            sourceAssemblyPath = Path.GetFullPath(input.SourceProjectInfo.AnalyzerResult.Properties["TargetPath"]);
            Require(Within(Path.Combine(language, "src", "CIS.Core", "bin", "Release", "net10.0"), sourceAssemblyPath)
                && Path.GetFileName(sourceAssemblyPath) == "CIS.Core.dll" && AssemblyName.GetAssemblyName(sourceAssemblyPath).Name == "CIS.Core",
                "The freshly built source Core target is missing or outside the expected normal output.");
            byte[] sourceOriginal = File.ReadAllBytes(sourceAssemblyPath);
            sourceAssemblySha256 = Hash(sourceAssemblyPath);
            foreach (var test in input.SourceProjectInfo.TestProjectsInfo.AnalyzerResults)
            {
                string path = Path.GetFullPath(TestProjectsInfo.GetInjectionFilePath(test, input.SourceProjectInfo.AnalyzerResult));
                Require(Within(Path.Combine(language, "tests"), path) && Path.GetFileName(path) == "CIS.Core.dll"
                    && path.Replace('\\', '/').Contains("/bin/Release/net10.0/", StringComparison.Ordinal), "Unexpected actual test injection path.");
                // Pinned 4.16 BackupOriginalAssembly moves the first original here before IMutationProcess.Mutate.
                // On reused outputs the backup already exists and the live original may still be present.
                string backup = path + ".stryker-unchanged";
                byte[] original = File.ReadAllBytes(backup);
                Require(original.SequenceEqual(sourceOriginal) && (!File.Exists(path) || File.ReadAllBytes(path).SequenceEqual(original)),
                    "The actual native original backup/live output differs from the freshly built source Core.");
                originals.Add(path, original); backups.Add(path, backup);
            }
            Require(originals.Count > 0, "No actual native original assembly backup was captured.");
            mutationInput = input;
            var root = (ProjectComponent)input.SourceProjectInfo.ProjectContents;
            var compiler = new CsharpCompilingProcess(input, options: actualOptions);
            IMutator[] mutators = registration ? [new BoundedRegistration()] : [new BoundedConditional()];
            var orchestrator = new CsharpMutantOrchestrator(new MutantPlacer(input.SourceProjectInfo.CodeInjector), mutators, actualOptions);
            var files = root.GetAllFiles().OfType<CsharpFileLeaf>().ToDictionary(file => file, file => compiler.GetSemanticModel(file.SyntaxTree));
            foreach (var pair in files)
            {
                CsharpFileLeaf file = pair.Key;
                file.MutatedSyntaxTree = orchestrator.Mutate(file.SyntaxTree, pair.Value);
                file.Mutants = orchestrator.GetLatestMutantBatch();
                compiler.ReplaceSyntaxTree(file.SyntaxTree, file.MutatedSyntaxTree);
            }
            IMutant[] mutants = root.Mutants.ToArray();
            if (registration) { BoundedRegistration.Verify(mutants); }
            else
            {
                Require(mutants.Length == 4 && mutants.All(mutant => mutant.Mutation.Type == Mutator.Conditional && mutant.ResultStatus == MutantStatus.Pending),
                    "Expected exactly four real pending conditional mutants before compilation.");
                var groups = mutants.GroupBy(mutant => mutant.Mutation.OriginalNode.SyntaxTree.FilePath).ToArray();
                Require(groups.Length == 2 && groups.All(group => group.Count() == 2 && group.Select(mutant => mutant.Mutation.DisplayName)
                    .Order(StringComparer.Ordinal).SequenceEqual(["Conditional (false) mutation", "Conditional (true) mutation"])),
                    "Each real HTTP/probe conditional must have precisely the tool's true and false mutations.");
            }
            File.WriteAllText(Path.Combine(output, "generated.json"), JsonSerializer.Serialize(mutants.Select(mutant => new
            {
                mutant.Id, mutant.Mutation.DisplayName, source = mutant.Mutation.OriginalNode.SyntaxTree.FilePath,
                start = mutant.Mutation.OriginalNode.Span.Start, length = mutant.Mutation.OriginalNode.Span.Length,
                replacement = mutant.Mutation.ReplacementNode.ToFullString(), controllerNamespace = input.SourceProjectInfo.HelperNamespace
            }), Json) + "\n");
            using var bytes = new MemoryStream();
            CompilingProcessResult compilation = compiler.Compile(bytes, null);
            Require(compilation.Success && !compilation.RolledbackIds.Any(), "The finite proof refuses every compiler rollback/error.");
            foreach (string path in originals.Keys)
            {
                bytes.Position = 0;
                using (FileStream destination = File.Create(path)) { bytes.CopyTo(destination); }
                instrumented.Add(path, Hash(path));
            }
            Require(originals.Count > 0, "No actual test output received the instrumented Core.");
        }
        public void FilterMutants(MutationTestInput input)
        {
            var filter = MutantFilterFactory.Create(options!, input);
            foreach (var file in ((ProjectComponent)input.SourceProjectInfo.ProjectContents).GetAllFiles())
            { _ = filter.FilterMutants(file.Mutants.Where(mutant => mutant.ResultStatus == MutantStatus.Pending), file, options!).ToArray(); }
            Require(input.SourceProjectInfo.ProjectContents.Mutants.Count(mutant => mutant.ResultStatus == MutantStatus.Pending) == (registration ? 3 : 4),
                "The actual normal Stryker filters did not retain exactly the expected finite mutants.");
        }
        internal bool RestoreAndVerify()
        {
            Require(sourceAssemblyPath is null || Hash(sourceAssemblyPath) == sourceAssemblySha256, "The freshly built source Core target changed during the finite proof.");
            Require(originals.All(pair => File.ReadAllBytes(backups[pair.Key]).SequenceEqual(pair.Value)), "The actual Stryker native original backup changed.");
            bool native = originals.Count > 0 && originals.All(pair => File.Exists(pair.Key) && File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value));
            if (!native && mutationInput is not null)
            { mutationInput.TestProjectsInfo.RestoreOriginalAssembly(mutationInput.SourceProjectInfo.AnalyzerResult); }
            Require(originals.All(pair => File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)
                && File.ReadAllBytes(backups[pair.Key]).SequenceEqual(pair.Value)), "Actual injection/native backup restoration verification failed."); return native;
        }
        internal object InjectionEvidence() => originals.Select(pair => new { path = pair.Key,
            sourceAssemblyPath, sourceAssemblySha256, backupPath = backups[pair.Key],
            backupSha256After = File.Exists(backups[pair.Key]) ? Hash(backups[pair.Key]) : null,
            originalSha256 = Convert.ToHexStringLower(SHA256.HashData(pair.Value)), instrumentedSha256 = instrumented.GetValueOrDefault(pair.Key),
            restoredSha256 = File.Exists(pair.Key) ? Hash(pair.Key) : null }).ToArray();
    }

    private sealed class BoundedConditional : IMutator
    {
        public IEnumerable<Mutation> Mutate(SyntaxNode node, SemanticModel semanticModel, IStrykerOptions options)
        {
            if (node is not ConditionalExpressionSyntax) { return []; }
            string path = node.SyntaxTree.FilePath.Replace('\\', '/'), text = node.ToString();
            bool http = path.EndsWith("/Healthcheck/AspNetCore/HealthcheckApplicationBuilderExtensions.cs", StringComparison.Ordinal)
                && text == "report.SelectedHealthy ? StatusCodes.Status200OK : StatusCodes.Status500InternalServerError";
            bool probe = path.EndsWith("/Healthcheck/Pipes/HealthProbeClient.cs", StringComparison.Ordinal) && text == "parsed.SelectedHealthy ? 0 : 1";
            return http || probe ? new ConditionalExpressionMutator().Mutate(node, semanticModel, options) : [];
        }
    }

    private sealed class BoundedRegistration : IMutator
    {
        private const string Guard = "settings.DetailsEnabled && authorizeDetails is null";
        public IEnumerable<Mutation> Mutate(SyntaxNode node, SemanticModel semanticModel, IStrykerOptions options)
        {
            if (!node.SyntaxTree.FilePath.Replace('\\', '/').EndsWith("/Healthcheck/AspNetCore/HealthcheckApplicationBuilderExtensions.cs", StringComparison.Ordinal)) { return []; }
            if (node is BinaryExpressionSyntax && node.ToString() == Guard)
            {
                return new BinaryExpressionMutator().Mutate(node, semanticModel, options)
                    .Concat(new NegateConditionMutator().Mutate(node, semanticModel, options));
            }
            if (node is IsPatternExpressionSyntax && node.ToString() == "authorizeDetails is null" && node.Parent is BinaryExpressionSyntax parent && parent.ToString() == Guard)
            { return new IsPatternExpressionMutator().Mutate(node, semanticModel, options); }
            return [];
        }
        internal static void Verify(IMutant[] mutants)
        {
            string[] expected =
            [
                "Logical mutation|" + Guard + "|settings.DetailsEnabled || authorizeDetails is null",
                "Negate expression|" + Guard + "|!(settings.DetailsEnabled && authorizeDetails is null)",
                "Equality mutation|authorizeDetails is null|authorizeDetails is not null"
            ];
            Require(mutants.Length == 3 && mutants.All(mutant => mutant.ResultStatus == MutantStatus.Pending)
                && mutants.Select(mutant => mutant.Mutation.DisplayName + "|" + mutant.Mutation.OriginalNode + "|" + mutant.Mutation.ReplacementNode)
                    .Order(StringComparer.Ordinal).SequenceEqual(expected.Order(StringComparer.Ordinal)),
                "Registration mode requires exactly the three original native logical/negation/is-null replacements.");
        }
    }

    private sealed class NativeLog : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new LineLog(categoryName);
        public void Dispose() { }
        private sealed class LineLog(string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            { if (IsEnabled(logLevel)) { Console.WriteLine($"[{logLevel}] {category}: {formatter(state, exception)}"); if (exception is not null) { Console.WriteLine(exception); } } }
        }
    }
}
