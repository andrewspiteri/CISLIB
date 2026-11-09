using System.Collections.Concurrent;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.TestSupport;
using static CIS.Tests.CompatibilityTests.TypeManagerCompatibilityInputs;

namespace CIS.Tests.CompatibilityTests;

// Frozen development consumers exercise real binary, source and behavior compatibility.
// Native build/ApiCompat do the work; the fixture only deploys isolated consumers.
internal sealed class TypeManagerCompatibilityFixture : IDisposable
{
    private static readonly string[] Dependencies = ["Microsoft.Extensions.Logging.Abstractions.dll", "Microsoft.Extensions.DependencyInjection.Abstractions.dll"];
    private readonly TemporaryDirectory temporary = new();
    private readonly Dictionary<string, ApprovedDependency> dependencies = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Lazy<Task<string>>> builds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> environment;
    private readonly JsonObject policy;
    private readonly string coreSha256;
    private readonly string dotnet = "dotnet";
    internal string Root => temporary.Path;
    internal string Language { get; }
    internal string Baseline => Path.Combine(Language, "verification/contracts/type-manager/v1-behavior-development");
    internal string Original => Path.Combine(Language, "verification/contracts/type-manager/v1-development");
    internal string Core { get; }
    internal JsonArray Cases { get; }

    internal static string Repository()
    {
        for (DirectoryInfo? at = new(AppContext.BaseDirectory); at is not null; at = at.Parent)
        { if (File.Exists(Path.Combine(at.FullName, "implementations/csharp/CisLib.slnx"))) { return at.FullName; } }
        throw new DirectoryNotFoundException("Repository root is required.");
    }

    internal TypeManagerCompatibilityFixture()
    {
        Language = Path.Combine(Repository(), "implementations/csharp");
        policy = Read(Path.Combine(Language, "eng/toolchain.json"));
        Core = typeof(CIS.TypeManagement.TypeManagerOptions).Assembly.Location;
        coreSha256 = Hash(Core);
        Unchanged();
        Cases = Read(Path.Combine(Baseline, "cases.json"))["cases"]!.AsArray();
        JsonObject locked = Read(Path.Combine(Language, "src/CIS.Core/packages.lock.json"))["dependencies"]!["net10.0"]!.AsObject();
        string packages = Environment.GetEnvironmentVariable("NUGET_PACKAGES") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget/packages");
        foreach (string name in Dependencies)
        {
            string packageId = Path.GetFileNameWithoutExtension(name);
            dependencies.Add(name, DependencyAsset(packages, packageId, locked[packageId]!.AsObject()));
        }
        environment = Environment.GetEnvironmentVariables().Keys.Cast<string>()
            .Where(key => key.StartsWith("CORE_", StringComparison.OrdinalIgnoreCase) || key.StartsWith("CIS_COMPAT_", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(key => key, _ => (string?)null, StringComparer.OrdinalIgnoreCase);
        environment["MSBUILDDISABLENODEREUSE"] = "1";
        environment["UseSharedCompilation"] = "false";
        Directory.CreateDirectory(Path.Combine(Root, "cwd"));
        File.Copy(Path.Combine(Language, "global.json"), Path.Combine(Root, "global.json"));
        File.WriteAllText(Path.Combine(Root, "Directory.Build.props"), "<Project />");
        File.WriteAllText(Path.Combine(Root, "NuGet.Config"), "<configuration><packageSources><clear/></packageSources></configuration>");
    }

    internal async Task Initialize()
    {
        Require((await Run("sdk", ["--version"])).Trim() == policy["sdk"]!.GetValue<string>(), "Wrong SDK.");
    }

    internal void Unchanged()
    {
        Baseline(Original, OriginalHash);
        Baseline(Baseline, BehaviorHash);
        Require(Hash(Core) == coreSha256, "The selected Core changed during compatibility testing.");
    }

    public void Dispose() => temporary.Dispose();

    internal async Task<string> Run(string name, string[] arguments, string[]? errors = null, bool api = false, bool additionalDependencies = true, string? workingDirectory = null)
    {
        string executable = api ? Environment.GetEnvironmentVariable("CIS_COMPAT_API_DOTNET") ?? dotnet : dotnet;
        string supplemental = Path.Combine(Path.GetDirectoryName(arguments[0]) ?? Root, "approved-dependencies.deps.json");
        string[] dependencyArguments = !api && additionalDependencies && File.Exists(supplemental) ? ["--additional-deps", supplemental] : [];
        string[] command = arguments[0].EndsWith(".dll", StringComparison.Ordinal)
            ? ["exec", "--fx-version", policy[api ? "toolHostRuntime" : "targetRuntime"]!.GetValue<string>(), "--roll-forward", "Disable", .. dependencyArguments, .. arguments]
            : arguments;
        ProcessResult child = await ProcessRunner.RunAsync(executable, command, workingDirectory ?? Path.Combine(Root, "cwd"), TimeSpan.FromSeconds(180), environment, Encoding.UTF8);
        string output = child.StandardOutput + child.StandardError;
        Require(child.OutputDrained && (errors is null ? child.ExitCode == 0 : child.ExitCode != 0 && errors.All(text => output.Contains(text, StringComparison.Ordinal))), name + ": " + output);
        return child.StandardOutput;
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

    internal Task<string> Source(string name, string source, string candidate, bool historical = false)
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
            await Run(name + "-build", ["build", project, "-c", "Release", "-m:1", "--nologo"]);
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

    internal async Task Behavior(string name, string binaries, string candidate, string caseId)
    {
        JsonNode item;
        item = Cases.Single(c => c!["id"]!.GetValue<string>() == caseId)!.DeepClone();
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
        Require(Same(observed, item["expected"]), caseName + " differs from frozen expectation: " + observedPath);
        Unchanged();
    }

    internal async Task Historical(string name, string candidate, bool source, bool missingDependencies = false)
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
        string[]? errors = missingDependencies ? ["TypeManagerInitializationException", "Microsoft.Extensions.Logging.Abstractions", "ReflectionTypeLoadException"] : null;
        string output = await Run(name, [consumer, manifest], errors, additionalDependencies: !missingDependencies);
        if (errors is null)
        {
            JsonNode observed = JsonNode.Parse(output)!;
            Require(observed["contractConsumer"]?.GetValue<string>() == "passed" && Same(observed["selectedAssemblyCount"], JsonValue.Create(2)), "Original consumer did not pass.");
        }
        Unchanged();
    }

    internal async Task Api(string name, string candidate)
    {
        string tool = Environment.GetEnvironmentVariable("CIS_COMPAT_API_TOOL") ?? Path.Combine(Environment.GetEnvironmentVariable("NUGET_PACKAGES") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget/packages"), "microsoft.dotnet.apicompat.tool/10.0.401/tools/net8.0/any/Microsoft.DotNet.ApiCompat.Tool.dll");
        Require(File.Exists(tool), "Restore the pinned ApiCompat tool or set CIS_COMPAT_API_TOOL to its entry DLL.");
        tool = Path.GetFullPath(tool);
        string version = (await Run(name + "-version", [tool, "--version"], api: true)).Trim();
        Require(version.StartsWith("10.0.401+", StringComparison.Ordinal), "Wrong API compatibility tool.");
        await Run(name, [tool, "-l", Path.Combine(Original, "reference/CIS.Core.dll"), "-r", candidate, "--enable-rule-cannot-change-parameter-name", "--enable-rule-attributes-must-match"], api: true);
        Unchanged();
    }
}
