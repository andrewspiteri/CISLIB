using System.Buffers;
using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.TestSupport;

namespace CIS.Verification;

internal sealed partial class DependencyChecks
{
    // Share only an overlapping physical input verification. A completed scan
    // covers a request only when the scan began strictly after that request.
    internal sealed class InputVerificationGate
    {
        private readonly SemaphoreSlim turn = new(1, 1);
        private long successfulScanStarted;

        internal async Task VerifyAsync(Action verifyInputs, Action checkDeadline, CancellationToken cancellationToken)
        {
            checkDeadline();
            long requested = Stopwatch.GetTimestamp();
            await turn.WaitAsync(cancellationToken);
            try
            {
                checkDeadline();
                long scanStarted = 0;
                if (successfulScanStarted <= requested)
                {
                    successfulScanStarted = 0;
                    scanStarted = Stopwatch.GetTimestamp();
                    verifyInputs();
                }
                checkDeadline();
                if (scanStarted != 0) { successfulScanStarted = scanStarted; }
            }
            finally { turn.Release(); }
        }
    }

    // Only evaluated build inputs are shared. Every invocation still runs the
    // dependency, restore, PE and package validators against its current inputs.
    internal sealed class PreparedEvaluation
    {
        private readonly string language, project, projectJson, referencesJson, environment;
        private readonly string[] importDirectories, membership, sdkInputs, factInputs;
        private readonly InputPin[] pins;
        private readonly InputVerificationGate verification = new();
        internal string Helper { get; }

        private PreparedEvaluation(string language, string project, JsonObject evaluation, JsonObject references,
            JsonObject helperEvaluation, IEnumerable<string> initialInputs, IEnumerable<InputPin> earlierPins, Action checkDeadline)
        {
            checkDeadline();
            this.language = language;
            this.project = project;
            projectJson = evaluation.ToJsonString();
            referencesJson = references.ToJsonString();
            Helper = Text(helperEvaluation["Properties"]!, "TargetPath");
            Require(File.Exists(Helper), "Build the Release Type Manager helper before preparing dependency checks.");
            string[] imports = FactImports(language, evaluation, references, helperEvaluation, checkDeadline);
            importDirectories = imports.Select(path => Path.GetDirectoryName(path)!).Distinct(PathComparer).ToArray();
            membership = EvaluationMembership(language, importDirectories, checkDeadline);
            sdkInputs = EvaluationSdkInputs(language, checkDeadline);
            factInputs = FactInputs(language, evaluation, references, helperEvaluation, checkDeadline);
            var inputs = new HashSet<string>(initialInputs.Concat(imports).Concat(membership).Concat(sdkInputs), PathComparer)
            {
                Helper, typeof(DependencyChecks).Assembly.Location, typeof(ProcessRunner).Assembly.Location,
                typeof(object).Assembly.Location, typeof(JsonObject).Assembly.Location
            };
            JsonNode properties = evaluation["Properties"]!;
            string sdkRoot = Text(properties, "MSBuildBinPath");
            foreach (string path in Directory.EnumerateFiles(sdkRoot, "*.dll")) { checkDeadline(); inputs.Add(path); }
            foreach (string name in new[] { "MSBuild.deps.json", "MSBuild.runtimeconfig.json" }) { inputs.Add(Path.Combine(sdkRoot, name)); }
            inputs.Add(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
                ?? Path.Combine(Text(properties, "NetCoreRoot"), OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
            foreach (JsonObject reference in Items(references["Items"]!, "ReferencePath")) { checkDeadline(); inputs.Add(FullPath(Text(reference, "Identity"), language)); }
            pins = ExpandInputPins(inputs, earlierPins, checkDeadline);
            environment = EvaluationEnvironment(checkDeadline);
        }

        private PreparedEvaluation(PreparedEvaluation binding, JsonObject evaluation, JsonObject references, JsonObject helperEvaluation, Action checkDeadline)
        {
            Require(binding.factInputs.SequenceEqual(FactInputs(binding.language, evaluation, references, helperEvaluation, checkDeadline), PathComparer),
                "Dependency evaluation import, reference or helper closure changed during recapture.");
            language = binding.language;
            project = binding.project;
            projectJson = evaluation.ToJsonString();
            referencesJson = references.ToJsonString();
            Helper = binding.Helper;
            environment = binding.environment;
            importDirectories = binding.importDirectories;
            membership = binding.membership;
            sdkInputs = binding.sdkInputs;
            factInputs = binding.factInputs;
            pins = binding.pins;
            checkDeadline();
        }

        internal static async Task<PreparedEvaluation> CaptureAsync(string language, TimeSpan timeout)
        {
            using var deadline = new EvaluationDeadline(timeout);
            language = Path.GetFullPath(language);
            string project = Path.Combine(language, "src/CIS.Core/CIS.Core.csproj");
            string[] initial = EvaluationInputs(language, deadline.Check), sdkBefore = EvaluationSdkInputs(language, deadline.Check);
            InputPin[] before = CaptureInputPins(initial.Concat(sdkBefore), deadline.Check);
            string originalEnvironment = EvaluationEnvironment(deadline.Check);
            var check = new DependencyChecks(language) { deadline = deadline };
            var discovery = await CaptureFacts();
            VerifyInputPins(before, deadline.Check);
            Require(initial.SequenceEqual(EvaluationInputs(language, deadline.Check), PathComparer) && sdkBefore.SequenceEqual(EvaluationSdkInputs(language, deadline.Check), PathComparer)
                && originalEnvironment == EvaluationEnvironment(deadline.Check),
                "Dependency evaluation inputs changed during capture.");
            // Discovery facts are not admitted. Bind their external imports and
            // references first, then obtain all reusable facts under those pins.
            var binding = new PreparedEvaluation(language, project, discovery.Project, discovery.References, discovery.Helper, initial, before, deadline.Check);
            binding.VerifyUnchanged(deadline.Check);
            var facts = await CaptureFacts();
            binding.VerifyUnchanged(deadline.Check);
            var prepared = new PreparedEvaluation(binding, facts.Project, facts.References, facts.Helper, deadline.Check);
            _ = deadline.Remaining();
            return prepared;

            async Task<(JsonObject Project, JsonObject References, JsonObject Helper)> CaptureFacts()
            {
                // All three queries are read-only: reference resolution does not
                // build project references, and the helper has no Core reference.
                // Await every child, including failures/cancellation, before any
                // facts are admitted or the bound SDK origin is accepted.
                JsonObject[] facts = await Task.WhenAll(check.Evaluate(project), check.Evaluate(project, true), CaptureHelper());
                JsonObject evaluation = facts[0];
                Require(Text(evaluation["Properties"]!, "NETCoreSdkVersion") == check.sdk, "Prepared dependency evaluation used another SDK.");
                Require(sdkBefore.Contains(Path.Combine(Text(evaluation["Properties"]!, "MSBuildBinPath"), "MSBuild.dll"), PathComparer),
                    "Prepared dependency evaluation used an unbound SDK directory.");
                deadline.Check();
                return (evaluation, facts[1], facts[2]);

                async Task<JsonObject> CaptureHelper()
                {
                    ProcessResult helper = await check.Run(["msbuild", "eng/CIS.TypeManager.Build/CIS.TypeManager.Build.csproj", "-p:Configuration=Release",
                        "-getProperty:TargetPath,MSBuildAllProjects,NETCoreSdkVersion", "-nologo"]);
                    Require(helper.ExitCode == 0 && helper.OutputDrained, "Type Manager helper evaluation failed during dependency preparation.");
                    JsonObject helperEvaluation = JsonNode.Parse(helper.StandardOutput)!.AsObject();
                    Require(Text(helperEvaluation["Properties"]!, "NETCoreSdkVersion") == check.sdk, "Prepared helper evaluation used another SDK.");
                    return helperEvaluation;
                }
            }
        }

        internal JsonObject Evaluation(string path, bool resolve)
        {
            Require(PathComparer.Equals(Path.GetFullPath(path), project), "Prepared dependency evaluation belongs to another project.");
            return JsonNode.Parse(resolve ? referencesJson : projectJson)!.AsObject();
        }

        internal void VerifyUnchanged()
        {
            using var deadline = new EvaluationDeadline(TimeSpan.FromMinutes(1));
            VerifyUnchanged(deadline.Check);
        }

        internal void VerifyUnchanged(Action checkDeadline)
        {
            Require(environment == EvaluationEnvironment(checkDeadline), "Prepared dependency evaluation environment changed.");
            Require(membership.SequenceEqual(EvaluationMembership(language, importDirectories, checkDeadline), PathComparer), "Prepared dependency evaluation input membership changed.");
            Require(sdkInputs.SequenceEqual(EvaluationSdkInputs(language, checkDeadline), PathComparer), "Prepared dependency evaluation SDK membership changed.");
            VerifyInputPins(pins, checkDeadline);
        }

        internal Task VerifyForRequestAsync(Action checkDeadline, CancellationToken cancellationToken)
            => verification.VerifyAsync(() => VerifyUnchanged(checkDeadline), checkDeadline, cancellationToken);

        internal bool Matches(string root, string selected) => PathComparer.Equals(Path.GetFullPath(root), language)
            && PathComparer.Equals(FullPath(selected, root), project);
    }

    internal static async Task<int> RunPreparedAsync(string[] args, PreparedEvaluation prepared, TextWriter output, TextWriter error, TimeSpan timeout)
    {
        try
        {
            Require(args.Length == 5 && prepared.Matches(args[0], args[1]), "Prepared dependency check must select its captured canonical Core project.");
            using var deadline = new EvaluationDeadline(timeout);
            await prepared.VerifyForRequestAsync(deadline.Check, deadline.Token);
            var check = new DependencyChecks(args[0], output, prepared) { deadline = deadline };
            try { await check.Verify(args[1], args[2], args[3], args[4]); }
            finally { await prepared.VerifyForRequestAsync(deadline.Check, deadline.Token); _ = deadline.Remaining(); }
            return 0;
        }
        catch (Exception failure) when (failure is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or JsonException or System.Xml.XmlException or BadImageFormatException or TimeoutException
            or System.ComponentModel.Win32Exception or OperationCanceledException)
        {
            error.WriteLine(failure.Message);
            return 1;
        }
    }

    // One explicit ordinary fixture batch shares only its physical evaluation
    // input boundary. Every variant still executes the real validators with
    // private arguments and writers; the per-request API above is unchanged.
    internal static async Task RunPreparedMetadataBatchAsync(string language, PreparedEvaluation prepared,
        Func<Func<string[], TextWriter, TextWriter, Task<int>>, Task> execute, TimeSpan timeout)
    {
        using var deadline = new EvaluationDeadline(timeout);
        await VerifyMetadataBatchBoundaryAsync(
            () => prepared.VerifyForRequestAsync(deadline.Check, deadline.Token),
            () => execute(Validate), deadline.Check);

        async Task<int> Validate(string[] args, TextWriter output, TextWriter error)
        {
            Require(args.Length == 5 && prepared.Matches(args[0], args[1]) &&
                PathComparer.Equals(Path.GetFullPath(args[0]), Path.GetFullPath(language)) &&
                (args[2] != "-" || args[4] != "-"), "A metadata batch may select only its captured canonical Core metadata inputs.");
            deadline.Check();
            var check = new DependencyChecks(language, output, prepared) { deadline = deadline };
            try
            {
                await check.Verify(args[1], args[2], args[3], args[4]);
                deadline.Check();
                return 0;
            }
            catch (Exception failure) when (failure is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException
                or ArgumentException or JsonException or System.Xml.XmlException or BadImageFormatException or TimeoutException
                or System.ComponentModel.Win32Exception or OperationCanceledException)
            {
                error.WriteLine(failure.Message);
                return 1;
            }
        }
    }

    internal static async Task VerifyMetadataBatchBoundaryAsync(Func<Task> verifyInputs, Func<Task> execute, Action checkDeadline)
    {
        checkDeadline();
        await verifyInputs();
        try { await execute(); }
        finally { await verifyInputs(); checkDeadline(); }
    }

    private sealed class EvaluationDeadline : IDisposable
    {
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly TimeSpan limit;
        private readonly CancellationTokenSource cancellation;
        internal CancellationToken Token => cancellation.Token;
        internal EvaluationDeadline(TimeSpan limit)
        {
            Require(limit > TimeSpan.Zero && limit <= TimeSpan.FromMinutes(1), "Dependency evaluation needs a positive deadline of at most 60 seconds.");
            this.limit = limit;
            cancellation = new CancellationTokenSource(limit);
        }
        internal TimeSpan Remaining()
        {
            TimeSpan remaining = limit - clock.Elapsed;
            if (remaining <= TimeSpan.Zero || Token.IsCancellationRequested) { throw new TimeoutException("The shared 60-second dependency evaluation deadline expired."); }
            return remaining;
        }
        internal void Check() => _ = Remaining();
        public void Dispose() => cancellation.Dispose();
    }

    internal sealed record InputPin(string Path, string? Sha256);
    internal static InputPin[] CaptureInputPins(IEnumerable<string> paths)
    {
        using var deadline = new EvaluationDeadline(TimeSpan.FromMinutes(1));
        return CaptureInputPins(paths, deadline.Check);
    }
    internal static InputPin[] CaptureInputPins(IEnumerable<string> paths, Action checkDeadline)
    {
        var names = new HashSet<string>(PathComparer);
        foreach (string path in paths) { checkDeadline(); names.Add(Path.GetFullPath(path)); }
        var pins = new List<InputPin>();
        foreach (string path in names.Order(PathComparer))
        { checkDeadline(); pins.Add(new(path, File.Exists(path) ? HashInput(path, checkDeadline) : null)); checkDeadline(); }
        return pins.ToArray();
    }
    // Earlier identities remain authoritative. Expanding the discovered closure
    // must not rebaseline a changed input, including an originally absent file.
    internal static InputPin[] ExpandInputPins(IEnumerable<string> paths, IEnumerable<InputPin> earlierPins, Action checkDeadline)
    {
        var retained = new Dictionary<string, InputPin>(PathComparer);
        foreach (InputPin pin in earlierPins)
        {
            checkDeadline();
            string path = Path.GetFullPath(pin.Path);
            Require(retained.TryAdd(path, new(path, pin.Sha256)), "Duplicate earlier dependency evaluation input: " + path);
        }
        var additional = new HashSet<string>(PathComparer);
        foreach (string path in paths)
        {
            checkDeadline();
            string fullPath = Path.GetFullPath(path);
            if (!retained.ContainsKey(fullPath)) { additional.Add(fullPath); }
        }
        foreach (InputPin pin in CaptureInputPins(additional, checkDeadline))
        { checkDeadline(); retained.Add(pin.Path, pin); }
        checkDeadline();
        return retained.Values.OrderBy(pin => pin.Path, PathComparer).ToArray();
    }

    internal static void VerifyInputPins(IEnumerable<InputPin> pins)
    {
        using var deadline = new EvaluationDeadline(TimeSpan.FromMinutes(1));
        VerifyInputPins(pins, deadline.Check);
    }
    internal static void VerifyInputPins(IEnumerable<InputPin> pins, Action checkDeadline)
    {
        foreach (InputPin pin in pins)
        {
            checkDeadline();
            Require(pin.Sha256 is null ? !File.Exists(pin.Path) && !Directory.Exists(pin.Path)
                : File.Exists(pin.Path) && HashInput(pin.Path, checkDeadline) == pin.Sha256, "Prepared dependency evaluation input changed: " + pin.Path);
            checkDeadline();
        }
    }

    // Deadline checks bracket every synchronous filesystem operation and each
    // hash block. An individual blocking OS open/read/enumerator move cannot be
    // preempted; no background scan or abandoned task outlives a failed caller.
    private static string HashInput(string path, Action checkDeadline)
    {
        checkDeadline();
        using Stream input = File.OpenRead(path);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] bytes = ArrayPool<byte>.Shared.Rent(65536);
        try
        {
            while (true)
            {
                checkDeadline();
                int read = input.Read(bytes, 0, bytes.Length);
                checkDeadline();
                if (read == 0) { break; }
                hash.AppendData(bytes, 0, read);
            }
            return Convert.ToHexStringLower(hash.GetHashAndReset());
        }
        finally { ArrayPool<byte>.Shared.Return(bytes); }
    }

    private static string[] EvaluationInputs(string language, Action checkDeadline)
    {
        var files = new HashSet<string>(EvaluationMembership(language, [], checkDeadline), PathComparer);
        foreach (string relative in new[] { "src/CIS.Core", "eng/CIS.TypeManager.Build" })
        {
            checkDeadline();
            string project = Path.Combine(language, relative);
            files.Add(Path.Combine(project, "packages.lock.json"));
            files.Add(Path.Combine(project, "obj/project.assets.json"));
            foreach (DirectoryInfo parent in Ancestors(new DirectoryInfo(project)))
            {
                checkDeadline();
                foreach (string name in new[] { "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "Directory.Build.rsp", "global.json", "NuGet.Config", "NuGet.config" })
                { checkDeadline(); files.Add(Path.Combine(parent.FullName, name)); }
            }
            string obj = Path.Combine(project, "obj");
            if (Directory.Exists(obj))
            { foreach (string path in Directory.EnumerateFiles(obj)) { checkDeadline(); if (Path.GetExtension(path) is ".props" or ".targets") { files.Add(path); } } }
        }
        foreach (string relative in new[] { "eng/project-boundaries.json", "eng/runtime-dependencies.json", "eng/development-dependencies.json", "eng/toolchain.json" })
        { checkDeadline(); files.Add(Path.Combine(language, relative)); }
        checkDeadline();
        return files.Order(PathComparer).ToArray();
    }

    private static string[] EvaluationMembership(string language, string[] importDirectories, Action checkDeadline)
    {
        var paths = new HashSet<string>(PathComparer);
        var pending = new Stack<string>(); pending.Push(language);
        while (pending.TryPop(out string? directory))
        {
            checkDeadline();
            foreach (string path in Directory.EnumerateFiles(directory)) { checkDeadline(); if (EvaluationFile(path)) { paths.Add(path); } }
            foreach (string child in Directory.EnumerateDirectories(directory))
            { checkDeadline(); if (Path.GetFileName(child) is not ("bin" or "obj" or ".git" or ".cis" or ".artifacts")) { pending.Push(child); } }
        }
        foreach (string directory in importDirectories)
        { checkDeadline(); foreach (string path in Directory.EnumerateFiles(directory)) { checkDeadline(); if (EvaluationFile(path)) { paths.Add(path); } } }
        checkDeadline();
        return paths.Order(PathComparer).ToArray();
    }
    private static bool EvaluationFile(string path) => Path.GetExtension(path) is ".csproj" or ".props" or ".targets" or ".rsp" or ".tasks" or ".overridetasks";
    private static string[] FactImports(string language, JsonObject evaluation, JsonObject references, JsonObject helper, Action checkDeadline)
    {
        var paths = new HashSet<string>(PathComparer);
        foreach (JsonObject fact in new[] { evaluation, references, helper })
        {
            checkDeadline();
            foreach (string path in Text(fact["Properties"]!, "MSBuildAllProjects").Split(';', StringSplitOptions.RemoveEmptyEntries))
            { checkDeadline(); paths.Add(FullPath(path, language)); }
        }
        return paths.Order(PathComparer).ToArray();
    }
    private static string[] FactInputs(string language, JsonObject evaluation, JsonObject references, JsonObject helper, Action checkDeadline)
    {
        var paths = new HashSet<string>(FactImports(language, evaluation, references, helper, checkDeadline), PathComparer)
        { FullPath(Text(helper["Properties"]!, "TargetPath"), language) };
        foreach (JsonObject reference in Items(references["Items"]!, "ReferencePath"))
        { checkDeadline(); paths.Add(FullPath(Text(reference, "Identity"), language)); }
        foreach (string property in new[] { "MSBuildBinPath", "MSBuildSDKsPath", "NETCoreSdkBundledVersionsProps", "NetCoreRoot", "NETCoreSdkVersion" })
        { checkDeadline(); paths.Add(property + ":" + Text(evaluation["Properties"]!, property)); }
        return paths.Order(PathComparer).ToArray();
    }
    private static string[] EvaluationSdkInputs(string language, Action checkDeadline)
    {
        checkDeadline();
        string dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT")
            ?? Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "../../.."));
        string sdk = Text(Read(Path.Combine(language, "global.json"))["sdk"]!, "version");
        checkDeadline();
        string directory = Path.Combine(dotnetRoot, "sdk", sdk);
        Require(Directory.Exists(directory), "The pinned SDK directory is required for dependency evaluation.");
        return SdkInputFiles(directory, checkDeadline)
            .Concat([Path.Combine(dotnetRoot, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"),
                typeof(DependencyChecks).Assembly.Location, typeof(ProcessRunner).Assembly.Location,
                typeof(object).Assembly.Location, typeof(JsonObject).Assembly.Location])
            .Distinct(PathComparer)
            .Order(PathComparer).ToArray();
    }
    internal static string[] SdkInputFiles(string directory, Action checkDeadline)
    {
        var paths = new List<string>();
        checkDeadline();
        foreach (string path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            checkDeadline();
            if (EvaluationFile(path) || Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase))
            { paths.Add(path); }
        }
        checkDeadline();
        return paths.Order(PathComparer).ToArray();
    }
    private static IEnumerable<DirectoryInfo> Ancestors(DirectoryInfo directory)
    { for (DirectoryInfo? parent = directory; parent is not null; parent = parent.Parent) { yield return parent; } }
    private static string EvaluationEnvironment(Action checkDeadline)
    {
        var values = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (DictionaryEntry item in Environment.GetEnvironmentVariables()) { checkDeadline(); values.Add((string)item.Key, (string)item.Value!); }
        checkDeadline();
        return JsonSerializer.Serialize(values);
    }
}
