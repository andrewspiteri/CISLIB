using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CIS.TestSupport;
using static CIS.Tests.CompatibilityTests.TypeManagerCompatibilityInputs;

namespace CIS.Tests.CompatibilityTests;

// Owned, retained build/child directories. Frozen inputs are only read and rechecked after every case.
internal sealed class TypeManagerCompatibilityFixture
{
    internal sealed record Mutation(string File, string Before, string After);
    internal sealed record Control(string Name, Mutation[] Changes, string Case, string Field, JsonNode Value);
    internal static readonly Control[] Controls =
    [
        new("failure-value", [new("TypeManagerFailureKind.cs", "InvalidInventory = 1", "InvalidInventory = 99")], "schema-1", "failureValues.InvalidInventory", JsonValue.Create(99)!),
        new("accept-new-schema", [new("AssemblyInventory.cs", "if (number != 1)", "if (number != 1 && number != 2)")], "schema-new", "outcome.status", JsonValue.Create("success")!),
        new("public-only", [new("TypeManager.cs", "candidate.IsClass && !candidate.IsAbstract", "candidate.IsClass && candidate.IsPublic && !candidate.IsAbstract")], "schema-1", "outcome.markers", new JsonArray()),
        new("mutable-query", [new("TypeManager.cs", "result = Array.AsReadOnly(matches.ToArray());", "result = matches.ToArray();")], "schema-1", "outcome.collectionsProtected", JsonValue.Create(false)!),
        new("lookup-loads", [new("TypeManager.cs", "return exactNames.TryGetValue(assemblyQualifiedTypeName, out type);", "type = Type.GetType(assemblyQualifiedTypeName); return type is not null;")], "schema-1", "outcome.lookupDoesNotLoad", JsonValue.Create(false)!),
        new("culture-order", [new("TypeManager.cs", "int comparison = StringComparer.Ordinal.Compare(left!.AssemblyQualifiedName, right!.AssemblyQualifiedName);", "int comparison = StringComparer.CurrentCulture.Compare(left!.AssemblyQualifiedName, right!.AssemblyQualifiedName);")], "schema-1", "outcome.markers", new JsonArray("CompatibilityFixtures.ÉCandidate", "CompatibilityFixtures.ZCandidate")),
        new("cwd-relative", [new("PreparationInputs.cs", "AppContext.BaseDirectory", "Environment.CurrentDirectory")], "relative-path", "outcome.status", JsonValue.Create("failure")!),
        new("duplicate-property", [new("AssemblyInventory.cs", "if (!names.Add(property.Name))", "if (!names.Add(property.Name) && property.Name.Length < 0)")], "duplicate-root-property", "outcome.status", JsonValue.Create("success")!),
        new("path-traversal", [new("AssemblyInventory.cs", "segment is \".\" or \"..\"", "segment == \".\""), new("AssemblyInventory.cs", "segment.EndsWith('.')", "(segment != \"..\" && segment.EndsWith('.'))"), new("AssemblyInventory.cs", "if (Path.IsPathRooted(check) || check == \"..\" || check.StartsWith(\"..\" + Path.DirectorySeparatorChar, StringComparison.Ordinal))", "if (Path.IsPathRooted(check))")], "path-traversal", "outcome.status", JsonValue.Create("success")!)
    ];
    private static readonly string[] Dependencies = ["Microsoft.Extensions.Logging.Abstractions.dll", "Microsoft.Extensions.DependencyInjection.Abstractions.dll"];
    private readonly Dictionary<string, string?> environment;
    private readonly Dictionary<string, string> immutable = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ApprovedDependency> dependencies = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Lazy<Task<string>>> builds = new(StringComparer.Ordinal);
    private sealed record ModuleCompiler(string Workspace, string Compiler, string Response, string Output,
        string[] Arguments, Dictionary<string, string> FixedInputs);
    private readonly Lazy<Task<ModuleCompiler>> modulePreparation;
    private readonly object reportGate = new();
    private readonly string coreSha256;
    private readonly JsonObject report;
    private readonly JsonObject policy;
    internal string Root { get; }
    internal string Language { get; }
    internal string Baseline => Path.Combine(Language, "verification/contracts/type-manager/v1-behavior-development");
    internal string Original => Path.Combine(Language, "verification/contracts/type-manager/v1-development");
    internal string Core { get; }
    internal JsonArray Cases { get; }
    private readonly string dotnet = Environment.GetEnvironmentVariable("CIS_COMPAT_DOTNET") ?? "dotnet";

    internal static string Repository()
    {
        for (DirectoryInfo? at = new(AppContext.BaseDirectory); at is not null; at = at.Parent)
        { if (File.Exists(Path.Combine(at.FullName, "implementations/csharp/CisLib.slnx"))) { return at.FullName; } }
        throw new DirectoryNotFoundException("Repository root is required.");
    }

    internal TypeManagerCompatibilityFixture()
    {
        Require((OperatingSystem.IsWindows() || OperatingSystem.IsLinux()) && RuntimeInformation.ProcessArchitecture == Architecture.X64, "Supported qualification lanes are Windows x64 and Linux x64.");
        Language = Path.GetFullPath(Path.Combine(Repository(), "implementations/csharp"));
        Root = Path.GetFullPath(Path.Combine(Repository(), ".artifacts/csharp/type-manager-compatibility", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(Path.Combine(Root, "logs"));
        Directory.CreateDirectory(Path.Combine(Root, "cwd"));
        Console.WriteLine("Type Manager compatibility evidence: " + Root);
        policy = Read(Path.Combine(Language, "eng/toolchain.json"));
        BaselineCheck();
        Cases = Read(Path.Combine(Baseline, "cases.json"))["cases"]!.AsArray();
        Require(Cases.Count == 22 && Cases.Select(c => c!["id"]!.GetValue<string>()).Distinct(StringComparer.Ordinal).Count() == 22, "Unexpected behavior case inventory.");
        foreach (JsonNode? item in Cases)
        {
            Require(Regex.IsMatch(item!["id"]!.GetValue<string>(), "^[a-z0-9-]+$", RegexOptions.CultureInvariant) && item["mode"]!.GetValue<string>() is "manifest" or "default" or "relative" or "prepared" or "arguments" or "cancel" or "selected", "Invalid behavior case.");
            if (item["input"] is not null) { Within(Path.Combine(Baseline, "cases"), item["input"]!.GetValue<string>()); }
            Require(item["assetMode"] is null || item["assetMode"]!.GetValue<string>() == "bad-image", "Unknown fixture asset mode.");
        }
        string? selected = Environment.GetEnvironmentVariable("CIS_COMPAT_CORE");
        string? producerPath = Environment.GetEnvironmentVariable("CIS_COMPAT_REPORT");
        string? expected = Environment.GetEnvironmentVariable("CIS_COMPAT_CORE_SHA256");
        Require(new[] { selected, producerPath, expected }.All(value => value is null) || new[] { selected, producerPath, expected }.All(value => !string.IsNullOrWhiteSpace(value)), "Set all three CIS_COMPAT_CORE, CIS_COMPAT_REPORT and CIS_COMPAT_CORE_SHA256 values, or none.");
        Core = Path.GetFullPath(selected ?? typeof(CIS.TypeManagement.TypeManagerOptions).Assembly.Location);
        Dictionary<string, string> sources = ModuleSources(Language);
        byte[]? producerBytes = producerPath is null ? null : File.ReadAllBytes(producerPath);
        JsonObject producer = producerBytes is null ? ProjectProducer(Core, sources) : JsonNode.Parse(producerBytes)!.AsObject();
        Producer(producer, Core, expected ?? Hash(Core), sources);
        File.WriteAllText(Path.Combine(Root, "candidate-producer.json"), producer.ToJsonString());
        foreach (string path in new[] { Core, Path.Combine(Language, "eng/toolchain.json"), Path.Combine(Language, "global.json"), Path.Combine(Root, "candidate-producer.json") }) { Pin(path); }
        if (producerPath is not null) { immutable.Add(Path.GetFullPath(producerPath), Convert.ToHexStringLower(SHA256.HashData(producerBytes!))); }
        else { Pin(Path.ChangeExtension(Core, ".pdb")); }
        // ASP.NET can satisfy these references from its shared framework instead of
        // copying NuGet assets beside Core. Frozen consumers still receive only the
        // independently locked NuGet bytes through their external dependency manifest.
        string lockPath = Path.Combine(Language, "src/CIS.Core/packages.lock.json");
        Pin(lockPath);
        JsonObject locked = Read(lockPath)["dependencies"]!["net10.0"]!.AsObject();
        string packages = Environment.GetEnvironmentVariable("NUGET_PACKAGES") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget/packages");
        foreach (string name in Dependencies)
        {
            string packageId = Path.GetFileNameWithoutExtension(name);
            ApprovedDependency dependency = DependencyAsset(packages, packageId, locked[packageId]!.AsObject());
            dependencies.Add(name, dependency);
            Pin(dependency.SourcePath); Pin(dependency.ArchivePath);
            Pin(dependency.RestoreMetadataPath); Pin(dependency.ArchiveSha512Path);
            Require(immutable[dependency.SourcePath] == dependency.Sha256 && immutable[dependency.ArchivePath] == dependency.ArchiveSha256
                && immutable[dependency.RestoreMetadataPath] == dependency.RestoreMetadataSha256
                && immutable[dependency.ArchiveSha512Path] == dependency.ArchiveSha512FileSha256,
                "Approved dependency changed while its inputs were pinned.");
        }
        foreach (string relative in sources.Keys) { Pin(Path.Combine(Language, "src/CIS.Core", relative)); }
        foreach (string path in Directory.GetFiles(Path.Combine(Language, "tests/CIS.Core.CompatibilityTests"), "*.cs")) { Pin(path); }
        coreSha256 = Hash(Core);
        report = new JsonObject
        {
            ["scope"] = "Unpublished development compatibility; not released historical behavior",
            ["platform"] = RuntimeInformation.OSDescription,
            ["coreSha256"] = coreSha256,
            ["consumerRuntime"] = policy["targetRuntime"]!.DeepClone(),
            ["apiRuntime"] = policy["toolHostRuntime"]!.DeepClone(),
            ["originalBaselineManifestSha256"] = OriginalHash,
            ["behaviorBaselineManifestSha256"] = BehaviorHash,
            ["approvedDependencies"] = JsonSerializer.SerializeToNode(dependencies.Values),
            ["dependencyAttestation"] = "Locked NuGet restore metadata; separately checked raw archive SHA-512 and exact DLL ZIP entry. Signed-package content hash is not independently recomputed.",
            ["inputs"] = JsonSerializer.SerializeToNode(immutable),
            ["results"] = new JsonArray(),
            ["commands"] = new JsonArray()
        };
        string[] allowed = ["PATH", "SYSTEMROOT", "WINDIR", "COMSPEC", "TEMP", "TMP", "TMPDIR", "HOME", "USERPROFILE", "APPDATA", "LOCALAPPDATA", "PROGRAMFILES", "PROGRAMFILES(X86)", "PROGRAMW6432", "DOTNET_ROOT", "DOTNET_ROOT_X64", "NUMBER_OF_PROCESSORS", "PROCESSOR_ARCHITECTURE"];
        environment = Environment.GetEnvironmentVariables().Keys.Cast<string>().Where(key => !allowed.Contains(key, StringComparer.OrdinalIgnoreCase)).ToDictionary(key => key, _ => (string?)null, StringComparer.Ordinal);
        foreach ((string key, string value) in new Dictionary<string, string>
        {
            ["DOTNET_CLI_HOME"] = Path.Combine(Root, "dotnet-home"),
            ["NUGET_PACKAGES"] = Path.Combine(Root, "packages"),
            ["NUGET_HTTP_CACHE_PATH"] = Path.Combine(Root, "http-cache"),
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
            ["DOTNET_NOLOGO"] = "1",
            ["MSBUILDDISABLENODEREUSE"] = "1",
            ["UseSharedCompilation"] = "false",
            ["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0",
            ["DOTNET_GENERATE_ASPNET_CERTIFICATE"] = "false",
            ["DOTNET_ADD_GLOBAL_TOOLS_TO_PATH"] = "false",
            ["DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE"] = "true"
        }) { environment[key] = value; }
        File.Copy(Path.Combine(Language, "global.json"), Path.Combine(Root, "global.json"));
        foreach (string name in new[] { "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props" }) { File.WriteAllText(Path.Combine(Root, name), "<Project />"); }
        File.WriteAllText(Path.Combine(Root, "Directory.Build.props"), "<Project><PropertyGroup><RestoreConfigFile>" + SecurityElement.Escape(Path.Combine(Root, "NuGet.Config")) + "</RestoreConfigFile></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(Root, "Directory.Build.rsp"), "");
        File.WriteAllText(Path.Combine(Root, "NuGet.Config"), "<configuration><packageSources><clear/></packageSources><fallbackPackageFolders><clear/></fallbackPackageFolders><auditSources><clear/></auditSources></configuration>");
        modulePreparation = new(PrepareModuleCompiler);
        Save();
    }

    internal async Task Initialize()
    {
        string sdk = (await Run("sdk", ["--version"])).Trim();
        Require(sdk == policy["sdk"]!.GetValue<string>(), "Wrong SDK.");
        lock (reportGate) { report["sdk"] = sdk; Save(); }
    }

    private void Pin(string path) { lock (reportGate) { immutable.Add(Path.GetFullPath(path), Hash(path)); } }
    private void BaselineCheck() { Baseline(Original, OriginalHash); Baseline(Baseline, BehaviorHash); }
    internal void Unchanged()
    {
        BaselineCheck();
        KeyValuePair<string, string>[] inputs;
        lock (reportGate) { inputs = immutable.ToArray(); }
        foreach ((string path, string hash) in inputs) { Require(Hash(path) == hash, "Qualification modified an input: " + path); }
        Producer(Read(Path.Combine(Root, "candidate-producer.json")), Core, coreSha256, ModuleSources(Language));
    }
    private void Save()
    {
        lock (reportGate)
        {
            report["inputs"] = JsonSerializer.SerializeToNode(immutable);
            File.WriteAllText(Path.Combine(Root, "results.json"), report.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
    }
    internal void Passed(string name, JsonObject? details = null)
    {
        Unchanged();
        details ??= new(); details["case"] = name; details["passed"] = true;
        lock (reportGate) { report["results"]!.AsArray().Add(details); Save(); }
    }

    internal async Task<string> Run(string name, string[] arguments, string[]? errors = null, bool api = false, bool additionalDependencies = true, string? workingDirectory = null)
    {
        string executable = api ? Environment.GetEnvironmentVariable("CIS_COMPAT_API_DOTNET") ?? dotnet : dotnet;
        string supplemental = Path.Combine(Path.GetDirectoryName(arguments[0]) ?? Root, "approved-dependencies.deps.json");
        string[] dependencyArguments = !api && additionalDependencies && File.Exists(supplemental) ? ["--additional-deps", supplemental] : [];
        string[] command = arguments[0].EndsWith(".dll", StringComparison.Ordinal) ? ["exec", "--fx-version", policy[api ? "toolHostRuntime" : "targetRuntime"]!.GetValue<string>(), "--roll-forward", "Disable", .. dependencyArguments, .. arguments] : arguments;
        var entry = new JsonObject { ["name"] = name, ["executable"] = executable, ["arguments"] = JsonSerializer.SerializeToNode(command), ["expectedErrors"] = JsonSerializer.SerializeToNode(errors) };
        lock (reportGate) { report["commands"]!.AsArray().Add(entry); Save(); }
        try
        {
            ProcessResult child = await ProcessRunner.RunAsync(executable, command, workingDirectory ?? Path.Combine(Root, "cwd"), TimeSpan.FromSeconds(180), environment, Encoding.UTF8);
            Require(child.OutputDrained, "The compatibility child output did not drain: " + name);
            string output = child.StandardOutput + child.StandardError;
            File.WriteAllText(Path.Combine(Root, "logs", name + ".log"), output);
            lock (reportGate) { entry["exitCode"] = child.ExitCode; entry["processId"] = child.ProcessId; Save(); }
            Require(errors is null ? child.ExitCode == 0 : child.ExitCode != 0 && errors.All(text => output.Contains(text, StringComparison.Ordinal)), name + " failed or did not fail for the specified defect; inspect " + Path.Combine(Root, "logs", name + ".log"));
            return child.StandardOutput;
        }
        catch (Exception error) { lock (reportGate) { entry["error"] = error.Message; Save(); } throw; }
    }

    private void Runtime(string directory, string candidate)
    {
        Directory.CreateDirectory(directory);
        File.Copy(candidate, Path.Combine(directory, "CIS.Core.dll"), true);
        Require(Hash(candidate) == Hash(Path.Combine(directory, "CIS.Core.dll")), "Candidate copy differs.");
        foreach (string name in Dependencies)
        {
            string source = dependencies[name].SourcePath;
            File.Copy(source, Path.Combine(directory, name), true);
            Require(Hash(Path.Combine(directory, name)) == dependencies[name].Sha256, "Dependency copy differs.");
        }
        // The first frozen consumer predates Core's approved Microsoft dependency closure.
        // Deploy those exact bytes via an external host manifest; never rewrite the frozen deps file.
        var assets = new JsonObject(); var libraries = new JsonObject();
        foreach (string name in Dependencies)
        {
            var identity = System.Reflection.AssemblyName.GetAssemblyName(Path.Combine(directory, name));
            string key = identity.Name + "/" + identity.Version;
            assets[key] = new JsonObject { ["runtime"] = new JsonObject { [name] = new JsonObject { ["assemblyVersion"] = identity.Version!.ToString() } } };
            libraries[key] = new JsonObject { ["type"] = "project", ["serviceable"] = false, ["sha512"] = "" };
        }
        const string target = ".NETCoreApp,Version=v10.0";
        File.WriteAllText(Path.Combine(directory, "approved-dependencies.deps.json"), new JsonObject { ["runtimeTarget"] = new JsonObject { ["name"] = target }, ["targets"] = new JsonObject { [target] = assets }, ["libraries"] = libraries }.ToJsonString());
    }

    internal Task<string> Source(string name, string source, string candidate, bool historical = false, string[]? errors = null)
    {
        return builds.GetOrAdd("source:" + name, _ => new Lazy<Task<string>>(Build)).Value;
        async Task<string> Build()
        {
            string directory = Path.Combine(Root, name); Directory.CreateDirectory(directory);
            File.Copy(Path.Combine(source, "Program.cs"), Path.Combine(directory, "Program.cs"));
            string template = File.ReadAllText(Path.Combine(source, "Consumer.csproj.template"));
            template = Replace(template, historical ? "../reference/CIS.Core.dll" : "@@CORE@@", SecurityElement.Escape(candidate)!, true);
            if (!historical) { template = Replace(template, "@@EXTRA@@", SecurityElement.Escape(Path.Combine(Baseline, "compiled/CIS.TypeManager.BehaviorExtra.dll"))!, true); }
            string project = Path.Combine(directory, "Consumer.csproj"); File.WriteAllText(project, template);
            await Run(name + "-build", ["build", project, "-c", "Release", "-m:1", "--nologo"], errors);
            return Path.Combine(directory, "bin/Release/net10.0");
        }
    }

    internal static string Replace(string text, string before, string after, bool unique)
    {
        Require(before.Length > 0 && before != after, "No-op mutation/template replacement.");
        int count = text.Split(before, StringSplitOptions.None).Length - 1;
        Require(count > 0 && (!unique || count == 1), "Mutation/template anchor missing or not unique: " + before);
        return text.Replace(before, after, StringComparison.Ordinal);
    }

    internal Task<string> Module(string name, Mutation[] changes)
    {
        return builds.GetOrAdd("module:" + name, _ => new Lazy<Task<string>>(Build)).Value;
        async Task<string> Build()
        {
            Unchanged();
            string directory = Path.Combine(Root, "controls", name), module = Path.Combine(Language, "src/CIS.Core/TypeManagement");
            int[] hits = new int[changes.Length];
            foreach (string file in Directory.GetFiles(module, "*.cs", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                string text = File.ReadAllText(file);
                for (int i = 0; i < changes.Length; i++)
                {
                    Mutation change = changes[i];
                    if (change.File != "*" && Path.GetFileName(file) != change.File) { continue; }
                    int count = text.Split(change.Before, StringSplitOptions.None).Length - 1;
                    if (change.File == "*" && count == 0) { continue; }
                    text = Replace(text, change.Before, change.After, change.File != "*"); hits[i] += count;
                }
                string destination = Path.Combine(directory, Path.GetRelativePath(module, file)); Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.WriteAllText(destination, text);
            }
            Require(hits.All(count => count > 0), "Mutation anchor was not found.");
            ModuleCompiler seed = await modulePreparation.Value;
            ModuleCompiler compiler = PrivateModuleCompiler(seed, name);
            foreach ((string path, string hash) in compiler.FixedInputs) { Require(Hash(path) == hash, "Prepared module compiler input changed: " + path); }
            var sourcePins = new JsonObject();
            foreach (string file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                string staged = Path.Combine(compiler.Workspace, Path.GetRelativePath(directory, file));
                Directory.CreateDirectory(Path.GetDirectoryName(staged)!); File.Copy(file, staged, true);
                sourcePins[file] = Hash(file);
                Require(Hash(staged) == Hash(file), "A module control's actual source differs from its compiler input.");
                compiler.FixedInputs[staged] = Hash(file);
            }
            File.Delete(compiler.Output);
            File.Delete(Path.ChangeExtension(compiler.Output, ".pdb"));
            await Run(name + "-compile", [compiler.Compiler, "/noconfig", "@" + compiler.Response], workingDirectory: compiler.Workspace);
            Require(File.Exists(compiler.Output), "The real compiler did not emit the module control assembly.");
            string binary = Path.Combine(directory, "bin/Release/net10.0/CIS.Core.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(binary)!); File.Copy(compiler.Output, binary);
            Require(Hash(binary) == Hash(compiler.Output), "The emitted module control copy differs.");
            string pdb = Path.ChangeExtension(compiler.Output, ".pdb");
            if (File.Exists(pdb))
            {
                string destination = Path.ChangeExtension(binary, ".pdb"); File.Copy(pdb, destination);
                Require(Hash(pdb) == Hash(destination), "The emitted module debug-symbol copy differs.");
            }
            foreach ((string path, string hash) in compiler.FixedInputs) { Require(Hash(path) == hash, "Prepared module compiler input changed during compilation: " + path); }
            foreach ((string path, JsonNode? hash) in sourcePins)
            {
                string expected = hash!.GetValue<string>();
                Require(Hash(path) == expected && Hash(Path.Combine(compiler.Workspace, Path.GetRelativePath(directory, path))) == expected,
                    "A module control source changed during compilation.");
            }
            var controlRecord = new JsonObject
            {
                ["binarySha256"] = Hash(binary),
                ["mutations"] = JsonSerializer.SerializeToNode(changes),
                ["occurrences"] = JsonSerializer.SerializeToNode(hits),
                ["sourceInputs"] = sourcePins,
                ["compilerWorkspace"] = compiler.Workspace,
                ["compilerResponseSha256"] = Hash(compiler.Response),
                ["compilerSha256"] = Hash(compiler.Compiler)
            };
            lock (reportGate)
            {
                report["controls"] ??= new JsonObject();
                report["controls"]![name] = controlRecord;
                Save();
            }
            return binary;
        }
    }

    private async Task<ModuleCompiler> PrepareModuleCompiler()
    {
        string workspace = Path.Combine(Root, "module-compiler");
        string module = Path.Combine(Language, "src/CIS.Core/TypeManagement");
        foreach (string source in Directory.EnumerateFiles(module, "*.cs", SearchOption.AllDirectories))
        {
            string target = Path.Combine(workspace, Path.GetRelativePath(module, source));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(source, target);
        }
        string project = Path.Combine(workspace, "Core.csproj");
        File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><AssemblyName>CIS.Core</AssemblyName><AssemblyVersion>1.0.0.0</AssemblyVersion><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup></Project>");
        string capture = await Run("module-compiler-prepare", ["msbuild", project, "-restore", "-t:Compile", "-p:Configuration=Release",
            "-p:SkipCompilerExecution=true", "-p:ProvideCommandLineArgs=true", "-getTargetResult:CoreCompile",
            "-getProperty:NETCoreSdkVersion,RoslynTargetsPath,CscToolPath,CscToolExe", "-nologo", "-v:quiet"], workingDirectory: workspace);
        string capturePath = Path.Combine(workspace, "compiler-command.json"); File.WriteAllText(capturePath, capture);
        JsonObject command = JsonNode.Parse(capture)!.AsObject(), properties = command["Properties"]!.AsObject();
        Require(properties["NETCoreSdkVersion"]!.GetValue<string>() == policy["sdk"]!.GetValue<string>()
            && string.IsNullOrEmpty((string?)properties["CscToolPath"]) && string.IsNullOrEmpty((string?)properties["CscToolExe"]), "The module capture must use the pinned SDK's managed compiler.");
        string compiler = Path.Combine(properties["RoslynTargetsPath"]!.GetValue<string>(), "bincore/csc.dll");
        JsonObject compile = command["TargetResults"]!["CoreCompile"]!.AsObject();
        Require((string?)compile["Result"] == "Success", "The SDK did not capture its actual module compiler command.");
        string[] arguments = compile["Items"]!.AsArray().Select(item => item!["Identity"]!.GetValue<string>()).ToArray();
        Require(arguments.Length > 0 && arguments.All(value => !value.Contains('\r') && !value.Contains('\n'))
            && arguments.Count(value => value == "/noconfig") == 1 && arguments.Contains("/nullable:enable", StringComparer.Ordinal)
            && arguments.Contains("/warnaserror+", StringComparer.Ordinal) && arguments.Contains("/deterministic+", StringComparer.Ordinal), "The captured module compiler options are incomplete.");
        string response = Path.Combine(workspace, "compiler.rsp");
        File.WriteAllText(response, string.Join(Environment.NewLine, arguments.Where(value => value != "/noconfig")) + Environment.NewLine, new UTF8Encoding(false));
        string output = Path.GetFullPath(arguments.Single(value => value.StartsWith("/out:", StringComparison.OrdinalIgnoreCase))[5..].Trim('"'), workspace);
        Require(output.StartsWith(workspace + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal), "The captured compiler output escapes its owned workspace.");
        foreach (string prefix in new[] { "/out:", "/refout:", "/pdb:" })
        {
            foreach (string argument in arguments.Where(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                string path = Path.GetFullPath(argument[prefix.Length..].Trim('"'), workspace);
                Require(path.StartsWith(workspace + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal), "A compiler output escapes its owned workspace.");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            }
        }
        var fixedInputs = new Dictionary<string, string>(StringComparer.Ordinal)
        { [project] = Hash(project), [compiler] = Hash(compiler), [response] = Hash(response), [capturePath] = Hash(capturePath) };
        foreach (string generated in Directory.EnumerateFiles(Path.Combine(workspace, "obj"), "*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path) is ".cs" or ".editorconfig")) { fixedInputs.Add(generated, Hash(generated)); }
        foreach (string prefix in new[] { "/reference:", "/analyzer:", "/analyzerconfig:", "/additionalfile:" })
        {
            foreach (string argument in arguments.Where(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                string path = Path.GetFullPath(argument[prefix.Length..].Trim('"'), workspace);
                fixedInputs.TryAdd(path, Hash(path));
            }
        }
        foreach (string source in Directory.EnumerateFiles(module, "*.cs", SearchOption.AllDirectories))
        {
            string staged = Path.Combine(workspace, Path.GetRelativePath(module, source));
            Require(Hash(staged) == Hash(source), "The captured module source differs from its actual input.");
            fixedInputs.Add(staged, Hash(staged));
        }
        lock (reportGate)
        {
            report["moduleCompilerPreparation"] = new JsonObject
            {
                ["scope"] = "One actual SDK compiler command; every distinct positive/negative module still emits a fresh real assembly in a private compiler workspace and executes its original consumers/API checks.",
                ["sdk"] = properties["NETCoreSdkVersion"]!.DeepClone(),
                ["fixedInputs"] = JsonSerializer.SerializeToNode(fixedInputs),
                ["argumentCount"] = arguments.Length,
                ["capturePath"] = capturePath,
                ["responsePath"] = response
            };
            Save();
        }
        return new(workspace, compiler, response, output, arguments, fixedInputs);
    }

    private ModuleCompiler PrivateModuleCompiler(ModuleCompiler seed, string name)
    {
        foreach ((string path, string hash) in seed.FixedInputs) { Require(Hash(path) == hash, "Prepared module compiler input changed: " + path); }
        string workspace = Path.Combine(Root, "module-compilers", name);
        Require(!Directory.Exists(workspace), "A module compiler workspace must be fresh.");
        foreach (string file in Directory.EnumerateFiles(seed.Workspace, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(workspace, Path.GetRelativePath(seed.Workspace, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target);
            Require(Hash(target) == Hash(file), "The private compiler workspace differs from its captured seed.");
        }
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string[] arguments = seed.Arguments.Select(argument => argument.Replace(seed.Workspace, workspace, comparison)).ToArray();
        Require(arguments.Select(argument => argument.Replace(workspace, seed.Workspace, comparison)).SequenceEqual(seed.Arguments, StringComparer.Ordinal),
            "The private module compiler changed options beyond its owned workspace paths.");
        string response = Path.Combine(workspace, Path.GetRelativePath(seed.Workspace, seed.Response));
        File.WriteAllText(response, string.Join(Environment.NewLine, arguments.Where(value => value != "/noconfig")) + Environment.NewLine, new UTF8Encoding(false));
        foreach (string prefix in new[] { "/out:", "/refout:", "/pdb:" })
        {
            foreach (string argument in arguments.Where(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                string path = Path.GetFullPath(argument[prefix.Length..].Trim('"'), workspace);
                Require(path.StartsWith(workspace + Path.DirectorySeparatorChar, comparison), "A private compiler output escapes its owned workspace.");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            }
        }
        var fixedInputs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string path, string hash) in seed.FixedInputs)
        {
            string relative = Path.GetRelativePath(seed.Workspace, path);
            bool local = !Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
            string target = local ? Path.Combine(workspace, relative) : path;
            string expected = path == seed.Response ? Hash(response) : hash;
            Require(Hash(target) == expected, "A private module compiler input differs from its captured input.");
            fixedInputs.Add(target, expected);
        }
        foreach ((string path, string hash) in seed.FixedInputs) { Require(Hash(path) == hash, "Prepared module compiler input changed during cloning: " + path); }
        return new(workspace, seed.Compiler, response, Path.Combine(workspace, Path.GetRelativePath(seed.Workspace, seed.Output)), arguments, fixedInputs);
    }

    internal async Task Behavior(string name, string binaries, string candidate, string caseId, string? field = null, JsonNode? value = null)
    {
        JsonNode item;
        lock (reportGate) { item = Cases.Single(c => c!["id"]!.GetValue<string>() == caseId)!.DeepClone(); }
        string caseName = name + "-" + caseId, runRoot = Path.Combine(Root, "runs", caseName), directory = Path.Combine(runRoot, "consumer");
        Directory.CreateDirectory(directory);
        foreach (string file in new[] { "CIS.TypeManager.BehaviorConsumer.dll", "CIS.TypeManager.BehaviorConsumer.deps.json", "CIS.TypeManager.BehaviorConsumer.runtimeconfig.json", "CIS.TypeManager.BehaviorExtra.dll" }) { File.Copy(Path.Combine(binaries, file), Path.Combine(directory, file)); }
        if (item["assetMode"]?.GetValue<string>() == "bad-image") { File.WriteAllText(Path.Combine(directory, "CIS.TypeManager.BehaviorExtra.dll"), "Not a managed assembly"); }
        if (caseId == "path-traversal") { File.Copy(Path.Combine(directory, "CIS.TypeManager.BehaviorConsumer.dll"), Path.Combine(runRoot, "Outside.dll"), true); }
        Runtime(directory, candidate);
        string mode = item["mode"]!.GetValue<string>(), manifest = Path.Combine(directory, mode == "default" ? "CIS.TypeManager.BehaviorConsumer.types.json" : "input.types.json");
        if (item["input"] is not null) { File.Copy(Within(Path.Combine(Baseline, "cases"), item["input"]!.GetValue<string>()), manifest); }
        string consumer = Path.Combine(directory, "CIS.TypeManager.BehaviorConsumer.dll");
        JsonNode observed = JsonNode.Parse(await Run(caseName, [consumer, mode, manifest]))!;
        string observedPath = Path.Combine(directory, "observed.json"); File.WriteAllText(observedPath, observed.ToJsonString());
        bool equal = Same(observed, item["expected"]);
        if (field is null) { Require(equal, caseName + " differs from frozen expectation: " + observedPath); }
        else
        {
            JsonNode? actual = observed, expected = item["expected"];
            foreach (string key in field.Split('.')) { actual = actual![key]; expected = expected![key]; }
            Require(!equal && Same(actual, value) && !Same(actual, expected), caseName + " missed its specified deliberate regression.");
        }
        Passed(caseName, new JsonObject { ["consumerSha256"] = Hash(consumer), ["candidateSha256"] = Hash(candidate), ["observedSha256"] = Hash(observedPath), ["regressionDetected"] = field is not null, ["regressionField"] = field, ["regressionValue"] = value?.DeepClone() });
    }

    internal async Task Historical(string name, string candidate, bool source, bool negative = false, bool missingDependencies = false)
    {
        string directory;
        if (source) { directory = await Source(name, Path.Combine(Original, "source"), candidate, true); }
        else
        {
            directory = Path.Combine(Root, name); Directory.CreateDirectory(directory);
            foreach (string file in Directory.GetFiles(Path.Combine(Original, "compiled"))) { File.Copy(file, Path.Combine(directory, Path.GetFileName(file))); }
            Require(Hash(Path.Combine(directory, "CIS.TypeManager.ContractConsumer.dll")) == Baseline(Original, OriginalHash)["compiled/CIS.TypeManager.ContractConsumer.dll"], "Historical consumer changed.");
        }
        Runtime(directory, candidate);
        string manifest = Path.Combine(directory, "consumer.types.json");
        File.WriteAllText(manifest, JsonSerializer.Serialize(new { schemaVersion = 1, hostAssembly = "CIS.TypeManager.ContractConsumer, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null", assemblies = new[] { "CIS.TypeManager.ContractConsumer", "CIS.Core" }.Select(identity => new { assemblyName = identity + ", Version=1.0.0.0, Culture=neutral, PublicKeyToken=null", relativePath = identity + ".dll" }) }));
        string consumer = Path.Combine(directory, "CIS.TypeManager.ContractConsumer.dll");
        string[]? errors = negative ? ["MissingMethodException", "AssemblyManifestPath"] : missingDependencies ? ["TypeManagerInitializationException", "Microsoft.Extensions.Logging.Abstractions", "ReflectionTypeLoadException"] : null;
        string output = await Run(name, [consumer, manifest], errors, additionalDependencies: !missingDependencies);
        if (errors is null)
        {
            JsonNode observed = JsonNode.Parse(output)!;
            Require(observed["contractConsumer"]?.GetValue<string>() == "passed" && Same(observed["selectedAssemblyCount"], JsonValue.Create(2)), "Original consumer did not pass.");
        }
        Passed(name, new JsonObject { ["consumerSha256"] = Hash(consumer), ["candidateSha256"] = Hash(candidate) });
    }

    internal async Task Api(string name, string candidate, bool negative = false)
    {
        string tool = Environment.GetEnvironmentVariable("CIS_COMPAT_API_TOOL") ?? Path.Combine(Environment.GetEnvironmentVariable("NUGET_PACKAGES") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget/packages"), "microsoft.dotnet.apicompat.tool/10.0.401/tools/net8.0/any/Microsoft.DotNet.ApiCompat.Tool.dll");
        Require(File.Exists(tool), "Restore the pinned ApiCompat tool or set CIS_COMPAT_API_TOOL to its entry DLL.");
        tool = Path.GetFullPath(tool);
        lock (reportGate)
        {
            if (!immutable.ContainsKey(tool)) { Pin(tool); report["apiToolSha256"] = immutable[tool]; Save(); }
        }
        string version = (await Run(name + "-version", [tool, "--version"], api: true)).Trim();
        Require(version.StartsWith("10.0.401+", StringComparison.Ordinal), "Wrong API compatibility tool.");
        await Run(name, [tool, "-l", Path.Combine(Original, "reference/CIS.Core.dll"), "-r", candidate, "--enable-rule-cannot-change-parameter-name", "--enable-rule-attributes-must-match"], negative ? ["AssemblyManifestPath"] : null, api: true);
        Passed(name, new JsonObject { ["apiToolVersion"] = version, ["candidateSha256"] = Hash(candidate) });
    }
}
