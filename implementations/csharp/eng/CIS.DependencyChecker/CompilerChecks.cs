using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CIS.TestSupport;

namespace CIS.Verification;

internal static partial class CompilerChecks
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly string[] Surfaces = ["PublicSurface.cs", "EventSurface.cs", "RuntimeSurface.cs", "ProviderSurface.cs"];

    internal static async Task<int> RunAsync(string[] args)
    {
        if (args.Length is < 3 or > 4 || args[0] is not ("startup" or "samples" or "log-values") ||
            (args[0] == "samples" ? args.Length != 4 : args.Length != 3))
        {
            Console.Error.WriteLine("Usage: compiler-checks <startup|samples|log-values> <language-root> <output-root> [startup-root]");
            return 2;
        }
        string language = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[2]);
        string schema = args[0] switch { "startup" => "cis.startup-contract/v1", "samples" => "cis.compatibility-samples/v1", _ => "cis.actual-log-value-consumers/v1" };
        string scope = args[0] switch
        {
            "startup" => "Reference-only API specification and compile-time consumers; no runtime implementation or published compatibility claim.",
            "samples" => "Nonshipping reference-only compiler shape controls, not a published package, runtime or historical compatibility pass.",
            _ => "Actual Core DLL typed API compiler controls and one executable consumer; no published compatibility claim."
        };
        var report = new JsonObject
        {
            ["schema"] = schema,
            ["scope"] = scope,
            ["status"] = "Running",
            ["exitCode"] = 1,
            ["source"] = new JsonArray(),
            ["controls"] = new JsonArray(),
            ["detail"] = null
        };
        Directory.CreateDirectory(output);
        try
        {
            switch (args[0])
            {
                case "startup": await Startup(language, output, report); break;
                case "samples": await Samples(language, output, Path.GetFullPath(args[3]), report); break;
                default: await LogValues(language, output, report); break;
            }
            report["status"] = "Passed";
            report["exitCode"] = 0;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or JsonException or TimeoutException or System.ComponentModel.Win32Exception)
        {
            report["status"] = "Failed";
            report["detail"] = error.Message;
        }
        Write(Path.Combine(output, "summary.json"), report);
        Console.WriteLine(report.ToJsonString(JsonOptions));
        return report["exitCode"]!.GetValue<int>();
    }

    internal static bool Matches(int exitCode, string text, string? diagnostic) => string.IsNullOrEmpty(diagnostic)
        ? exitCode == 0
        : exitCode != 0 && Regex.IsMatch(text, @"(?:^|:\s*)error " + Regex.Escape(diagnostic) + @"\s*:", RegexOptions.Multiline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    internal static string Mutate(string original, string find, string replace)
    {
        original = original.Replace("\r\n", "\n", StringComparison.Ordinal);
        int position = original.IndexOf(find, StringComparison.Ordinal);
        Require(find.Length > 0 && position >= 0 && position == original.LastIndexOf(find, StringComparison.Ordinal), "Mutation anchor must occur exactly once.");
        return original.Replace(find, replace, StringComparison.Ordinal);
    }

    internal static string VerifiedReference(string root, JsonNode reference)
    {
        string path = Path.GetFullPath(Path.Combine(root, Text(reference, "path")));
        string prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        Require(path.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) &&
            Hash(path) == Text(reference, "sha256"), "Startup reference artifact is outside its root or changed.");
        return path;
    }

    private static async Task Startup(string language, string output, JsonObject report)
    {
        string source = Path.Combine(language, "verification/contracts/startup");
        JsonNode approved = Read(Path.Combine(language, "eng/runtime-dependencies.json"))["packages"]!.AsArray()
            .Single(item => Text(item!, "id") == "Microsoft.Extensions.Logging.Abstractions")!;
        JsonObject assets = Read(Path.Combine(language, "src/CIS.Core/obj/project.assets.json"));
        string key = Text(approved, "id") + "/" + Text(approved, "version");
        string[] compile = assets["targets"]!["net10.0"]![key]!["compile"]!.AsObject().Select(item => item.Key).Where(name => name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)).ToArray();
        Require(compile.Length == 1, "Expected one approved logging abstraction compile asset.");
        string logging = LoggingReference(assets, key, compile[0]);
        report["approvedCompileReference"] = new JsonObject { ["package"] = Text(approved, "id"), ["version"] = Text(approved, "version"), ["asset"] = compile[0], ["sha256"] = Hash(logging) };
        report["source"] = Inputs(language, source, true);
        string directory = Path.Combine(output, "reference");
        CopySurface(source, directory);
        string referenceWorkspace = Path.Combine(output, "compiler/reference");
        CopySurface(source, referenceWorkspace);
        PreparedCompiler referenceCompiler = await PrepareCompiler(language, output, report, "reference", referenceWorkspace, "Reference.csproj", logging);
        await CompilePrepared(language, output, report, referenceCompiler, "reference-only-specification", directory, Surfaces);
        report["referenceAssembly"] = Reference(output, Path.Combine(directory, "bin/Release/net10.0/CIS.Core.ContractSpecification.dll"), "dotnet.reference-assembly/v1");
        string consumerWorkspace = Path.Combine(output, "compiler/consumer");
        CopyConsumer(source, consumerWorkspace, "Consumer.cs");
        PreparedCompiler consumerCompiler = await PrepareCompiler(language, output, report, "consumer", consumerWorkspace, "Consumer.csproj", logging);
        foreach ((string id, string file) in new[] { ("positive-consumer", "Consumer.cs"), ("event-consumer", "EventConsumer.cs"), ("runtime-consumer", "RuntimeConsumer.cs"), ("provider-consumer", "ProviderConsumer.cs") })
        {
            string consumer = Path.Combine(output, id == "positive-consumer" ? "positive" : id);
            CopyConsumer(source, consumer, file);
            await CompilePrepared(language, output, report, consumerCompiler, id, consumer, ["Consumer.cs"]);
        }
        foreach (JsonNode? item in JsonNode.Parse(File.ReadAllText(Path.Combine(source, "negative-cases.json")))!.AsArray())
        {
            string id = Text(item!, "id"), consumer = Path.Combine(output, id);
            CopyConsumer(source, consumer, Text(item!, "file"));
            await CompilePrepared(language, output, report, consumerCompiler, id, consumer, ["Consumer.cs"], Text(item!, "expectedDiagnostic"));
        }
    }

    private static async Task Samples(string language, string output, string startupRoot, JsonObject report)
    {
        report["mutations"] = new JsonArray();
        JsonObject startup = Read(Path.Combine(startupRoot, "summary.json"));
        Require(startup["exitCode"]!.GetValue<int>() == 0, "Run successful startup contract controls first.");
        string original = VerifiedReference(startupRoot, startup["referenceAssembly"]!);
        string baseline = Path.Combine(output, "baseline/CIS.Core.ContractSpecification.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(baseline)!);
        File.Copy(original, baseline, true);
        report["baselineReference"] = Reference(output, baseline, "dotnet.reference-assembly/v1");
        JsonNode approved = startup["approvedCompileReference"]!;
        JsonObject assets = Read(Path.Combine(language, "src/CIS.Core/obj/project.assets.json"));
        string logging = LoggingReference(assets, Text(approved, "package") + "/" + Text(approved, "version"), Text(approved, "asset"));
        Require(Hash(logging) == Text(approved, "sha256"), "Approved logging reference is missing, ambiguous or changed.");
        report["approvedCompileReference"] = approved.DeepClone();
        string source = Path.Combine(language, "verification/compatibility/samples"), surface = Path.Combine(language, "verification/contracts/startup");
        report["source"] = Inputs(language, source, true);
        JsonObject catalog = Read(Path.Combine(source, "catalog.json"));
        string referenceWorkspace = Path.Combine(output, "compiler/reference");
        CopySurface(surface, referenceWorkspace);
        PreparedCompiler referenceCompiler = await PrepareCompiler(language, output, report, "reference", referenceWorkspace, "Reference.csproj", logging);
        string consumerWorkspace = Path.Combine(output, "compiler/consumer");
        CopyConsumer(source, consumerWorkspace, Text(catalog["sources"]!.AsArray()[0]!, "file"));
        string stagedReference = Path.Combine(consumerWorkspace, "reference/CIS.Core.ContractSpecification.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(stagedReference)!);
        File.Copy(baseline, stagedReference, true);
        PreparedCompiler consumerCompiler = await PrepareCompiler(language, output, report, "consumer", consumerWorkspace, "Consumer.csproj", logging, stagedReference);
        foreach (JsonNode? consumer in catalog["sources"]!.AsArray())
        {
            await Consumer(Text(consumer!, "id"), Text(consumer!, "file"), baseline, null);
        }
        foreach (JsonNode? sample in catalog["apiSamples"]!.AsArray())
        {
            string id = Text(sample!, "id"), directory = Path.Combine(output, id + "-reference");
            CopySurface(surface, directory);
            string path = Path.Combine(directory, Text(sample!, "surfaceFile"));
            File.WriteAllText(path, Mutate(File.ReadAllText(path), Text(sample!, "find"), Text(sample!, "replace")));
            report["mutations"]!.AsArray().Add(new JsonObject
            {
                ["id"] = id,
                ["source"] = Text(sample!, "surfaceFile"),
                ["originalSha256"] = Hash(Path.Combine(surface, Text(sample!, "surfaceFile"))),
                ["changedSha256"] = Hash(path),
                ["consumerId"] = Text(sample!, "consumerId"),
                ["expectedDiagnostic"] = Text(sample!, "expectedDiagnostic")
            });
            await CompilePrepared(language, output, report, referenceCompiler, id + "-reference", directory, Surfaces);
            JsonNode consumer = catalog["sources"]!.AsArray().Single(item => Text(item!, "id") == Text(sample!, "consumerId"))!;
            await Consumer(id, Text(consumer, "file"), Path.Combine(directory, "bin/Release/net10.0/CIS.Core.ContractSpecification.dll"), Text(sample!, "expectedDiagnostic"));
        }

        async Task Consumer(string id, string file, string reference, string? diagnostic)
        {
            string directory = Path.Combine(output, id);
            CopyConsumer(source, directory, file);
            File.Copy(reference, stagedReference, true);
            Require(Hash(reference) == Hash(stagedReference), "The staged compiler reference differs from this control's actual reference assembly.");
            await CompilePrepared(language, output, report, consumerCompiler, id, directory, ["Consumer.cs"], diagnostic, reference);
        }
    }

    private static async Task LogValues(string language, string output, JsonObject report)
    {
        JsonObject policy = Read(Path.Combine(language, "eng/toolchain.json"));
        ProcessResult selectedSdk = await Run(language, ["--version"]);
        string version = selectedSdk.StandardOutput.Trim();
        Require(selectedSdk.ExitCode == 0 && version == Text(policy, "sdk"), "Pinned SDK mismatch.");
        ProcessResult sdks = await Run(language, ["--list-sdks"]);
        Require(sdks.ExitCode == 0, "Cannot list pinned SDK locations.");
        string[] locations = sdks.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Trim())
            .Where(line => line.StartsWith(version + " [", StringComparison.Ordinal) && line.EndsWith(']')).ToArray();
        Require(locations.Length == 1, "Expected one pinned SDK location.");
        string sdkDirectory = locations[0][(locations[0].IndexOf('[') + 1)..^1];
        string compiler = Path.Combine(sdkDirectory, version, "Roslyn/bincore/csc.dll");
        string referenceRoot = Path.Combine(Path.GetDirectoryName(sdkDirectory)!, "packs/Microsoft.NETCore.App.Ref", Text(policy, "targetRuntime"), "ref", Text(policy, "targetFramework"));
        string core = Path.Combine(language, "src/CIS.Core/bin/Release/net10.0/CIS.Core.dll"), coreHash = Hash(core);
        report["coreSha256"] = coreHash;
        report["compilerSha256"] = Hash(compiler);
        string[] references = Directory.GetFiles(referenceRoot, "*.dll").OrderBy(Path.GetFileName, StringComparer.Ordinal).ToArray();
        Require(references.Length > 0, "Pinned framework reference pack unavailable.");
        report["references"] = new JsonArray(references.Select(path => (JsonNode)new JsonObject { ["name"] = Path.GetFileName(path), ["sha256"] = Hash(path) }).ToArray());
        string source = Path.Combine(language, "verification/consumers/log-values");
        JsonObject catalog = Read(Path.Combine(source, "controls.json"));
        Require(catalog["controlCount"]!.GetValue<int>() == 12 && catalog["controls"]!.AsArray().Count == 12, "Consumer control set differs.");
        report["source"] = Inputs(language, source, false);
        foreach (JsonNode? control in catalog["controls"]!.AsArray())
        {
            string id = Text(control!, "id"), directory = Path.Combine(output, id), binary = Path.Combine(directory, "Consumer.dll");
            Directory.CreateDirectory(directory);
            bool execute = control!["execute"]!.GetValue<bool>();
            string[] arguments = [compiler, "/nologo", "/noconfig", "/nostdlib+", "/nullable:enable", "/warnaserror+", "/deterministic+", "/optimize+", "/langversion:latest",
                execute ? "/target:exe" : "/target:library", "/out:" + binary, "/reference:" + core,
                .. references.Select(path => "/reference:" + path), Path.Combine(source, Text(control!, "file"))];
            JsonObject entry = await Compile(language, output, report, id, directory, arguments, control["expectedDiagnostic"]?.GetValue<string>(), true);
            if (entry["actualExitCode"]!.GetValue<int>() == 0) { entry["binary"] = Reference(output, binary, "dotnet.consumer/v1"); }
            if (execute)
            {
                File.Copy(core, Path.Combine(directory, "CIS.Core.dll"), true);
                Write(Path.Combine(directory, "Consumer.runtimeconfig.json"), new JsonObject
                {
                    ["runtimeOptions"] = new JsonObject
                    {
                        ["tfm"] = Text(policy, "targetFramework"),
                        ["rollForward"] = "Disable",
                        ["framework"] = new JsonObject { ["name"] = "Microsoft.NETCore.App", ["version"] = Text(policy, "targetRuntime") }
                    }
                });
                ProcessResult result = await Run(language, ["exec", "--fx-version", Text(policy, "targetRuntime"), "--roll-forward", "Disable", binary]);
                string log = Path.Combine(directory, "execution.log"), text = Output(result);
                File.WriteAllText(log, text);
                entry["executionExitCode"] = result.ExitCode;
                entry["executionLog"] = Reference(output, log, "dotnet.consumer-execution/v1");
                entry["executed"] = true;
                Require(result.ExitCode == 0 && text.TrimEnd('\r', '\n') == "Typed log-value consumer passed.", "Actual typed consumer execution failed.");
            }
        }
        Require(Hash(core) == coreHash, "Core changed during consumer qualification.");
    }

    private static async Task<JsonObject> Compile(string language, string output, JsonObject report, string id, string directory, IEnumerable<string> arguments, string? diagnostic, bool executableControl = false, string? workingDirectory = null)
    {
        ProcessResult result = await Run(workingDirectory ?? language, arguments);
        string log = Path.Combine(directory, "compiler.log"), text = Output(result);
        File.WriteAllText(log, text);
        bool matched = Matches(result.ExitCode, text, diagnostic);
        var entry = new JsonObject { ["id"] = id, ["actualExitCode"] = result.ExitCode, ["expectedDiagnostic"] = string.IsNullOrEmpty(diagnostic) ? null : diagnostic, ["matched"] = matched, ["log"] = Reference(output, log, "dotnet.compiler-log/v1") };
        if (executableControl) { entry["executed"] = false; }
        report["controls"]!.AsArray().Add(entry);
        Require(matched, "Compiler control " + id + " did not match its expected outcome. See " + log);
        return entry;
    }

    private static string LoggingReference(JsonObject assets, string key, string asset)
    {
        string relative = Path.Combine(Text(assets["libraries"]![key]!, "path"), asset);
        string[] candidates = assets["packageFolders"]!.AsObject().Select(folder => Path.Combine(folder.Key, relative)).Where(File.Exists).ToArray();
        Require(candidates.Length == 1, "Approved logging reference is missing or ambiguous; run locked restore first.");
        return candidates[0];
    }

    private static JsonArray Inputs(string language, string source, bool recursive) => new(Directory.GetFiles(source, "*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
        .Order(StringComparer.Ordinal).Select(path => (JsonNode)new JsonObject { ["path"] = Path.GetRelativePath(Path.GetFullPath(Path.Combine(language, "../..")), path).Replace('\\', '/'), ["sha256"] = Hash(path) }).ToArray());
    private static void CopySurface(string source, string directory)
    {
        Directory.CreateDirectory(directory);
        foreach (string name in Surfaces) { File.Copy(Path.Combine(source, name), Path.Combine(directory, name), true); }
        File.Copy(Path.Combine(source, "Reference.csproj.template"), Path.Combine(directory, "Reference.csproj"), true);
    }
    private static void CopyConsumer(string source, string directory, string file)
    {
        Directory.CreateDirectory(directory);
        File.Copy(Path.Combine(source, "Consumer.csproj.template"), Path.Combine(directory, "Consumer.csproj"), true);
        File.Copy(Path.Combine(source, file), Path.Combine(directory, "Consumer.cs"), true);
    }
    private static Task<ProcessResult> Run(string language, IEnumerable<string> arguments) => ProcessRunner.RunAsync("dotnet", arguments, language, TimeSpan.FromMinutes(2));
    private static string Output(ProcessResult result) => result.StandardOutput + result.StandardError;
    private static JsonObject Read(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    private static string Text(JsonNode node, string key) => node[key]!.GetValue<string>();
    private static string Hash(string path) { using Stream stream = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(stream)); }
    private static JsonObject Reference(string root, string path, string schema) => new() { ["path"] = Path.GetRelativePath(root, path).Replace('\\', '/'), ["sha256"] = Hash(path), ["schema"] = schema };
    private static void Write(string path, JsonObject report) => File.WriteAllText(path, report.ToJsonString(JsonOptions));
    private static void Require(bool condition, string detail) { if (!condition) { throw new InvalidDataException(detail); } }
}
