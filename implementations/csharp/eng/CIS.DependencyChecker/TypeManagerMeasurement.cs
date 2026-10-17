using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using static CIS.Verification.TypeManagerVerificationInputs;

namespace CIS.Verification;

internal sealed class TypeManagerMeasurement
{
    private readonly TypeManagerVerificationInputs input;
    private readonly string root, language, operation, corePin;
    private readonly JsonObject policy, report;
    private readonly IReadOnlyDictionary<string, string?> environment;
    private TypeManagerMeasurement(string operation, TypeManagerVerificationInputs input)
    {
        this.operation = operation; this.input = input; root = input.Output; language = input.Language; corePin = input.Get("expected-core-sha256");
        policy = Read(Path.Combine(language, "eng", "toolchain.json"), out string policyHash);
        var env = HermeticEnvironment(root).ToDictionary(p => p.Key, p => p.Value);
        env["DOTNET_TieredCompilation"] = "1"; env["DOTNET_TieredPGO"] = "1"; environment = env;
        report = new JsonObject
        {
            ["complete"] = false,
            ["qualified"] = false,
            ["schemaVersion"] = 1,
            ["operation"] = operation,
            ["platform"] = RuntimeInformation.OSDescription,
            ["harnessSha256"] = Hash(typeof(TypeManagerMeasurement).Assembly.Location),
            ["runtimePolicySha256"] = policyHash,
            ["sdk"] = policy["sdk"]!.DeepClone(),
            ["runtime"] = policy["targetRuntime"]!.DeepClone(),
            ["processWallObservation"] = "Verifier elapsed time around child execution, strict output decoding, output hashing and log writes; not comparable to the retired Python child-only timer."
        };
    }
    internal static async Task<int> RunAsync(string[] arguments)
    {
        if (arguments.Length == 0 || arguments[0] is not ("prepare" or "run")) { throw new ArgumentException("Use type-manager-measure prepare|run with named options."); }
        var input = new TypeManagerVerificationInputs(arguments[1..], ["repo-root", "output-root", "expected-core-sha256"], ["candidate-core", "candidate-report", "fixtures", "expected-fixtures-sha256", "samples", "warmups", "iterations"]);
        Require(RuntimeInformation.ProcessArchitecture == Architecture.X64 && (OperatingSystem.IsWindows() || OperatingSystem.IsLinux()), "Only native Windows/Linux x64 measurement lanes are supported");
        input.EmptyOutput(); return await new TypeManagerMeasurement(arguments[0], input).Execute();
    }
    private void Save() => WriteJson(Path.Combine(root, "results.json"), report);
    private async Task<(string Output, double Wall)> Run(string name, params string[] args)
    {
        var timer = Stopwatch.StartNew(); var result = await input.Run(name, args, environment: environment, timeout: TimeSpan.FromSeconds(180));
        return (result.StandardOutput, timer.Elapsed.TotalMilliseconds);
    }
    private async Task<int> Execute()
    {
        Save();
        try
        {
            Copy(Path.Combine(language, "global.json"), Path.Combine(root, "global.json"));
            Write(Path.Combine(root, "Directory.Build.props"), new XElement("Project", new XElement("PropertyGroup", new XElement("RestoreConfigFile", Path.Combine(root, "NuGet.Config")))).ToString());
            foreach (string name in new[] { "Directory.Build.targets", "Directory.Packages.props" }) { Write(Path.Combine(root, name), "<Project />"); }
            Write(Path.Combine(root, "Directory.Build.rsp"), "");
            Write(Path.Combine(root, "NuGet.Config"), "<configuration><packageSources><clear/></packageSources><fallbackPackageFolders><clear/></fallbackPackageFolders><auditSources><clear/></auditSources></configuration>");
            Require((await Run("sdk", "--version")).Output.Trim() == (string?)policy["sdk"], "Wrong SDK");
            int samples = Number("samples", 12), warmups = Number("warmups", 2), iterations = Number("iterations", 10000);
            Require(samples >= 12 && samples % 4 == 0 && warmups >= 1 && iterations >= 1000, "Use at least 12 samples in a multiple of four, one warmup round and 1000 query iterations");
            if (operation == "prepare") { await Prepare(); } else { await Measure(samples, warmups, iterations); }
            report["complete"] = true; return 0;
        }
        catch (Exception error) { report["error"] = error.Message; throw; }
        finally { Save(); }
    }
    private int Number(string name, int fallback) => input.Optional(name) is string value && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) ? parsed : input.Optional(name) is null ? fallback : throw new ArgumentException("Invalid --" + name);
    private async Task<string> Project(string name, string source, IEnumerable<string>? references = null, bool executable = false)
    {
        string directory = Path.Combine(root, "build", name);
        Write(Path.Combine(directory, "Program.cs"), source.Replace("\r\n", "\n"));
        var properties = new XElement("PropertyGroup", new XElement("TargetFramework", "net10.0"), new XElement("AssemblyName", name), new XElement("AssemblyVersion", "1.0.0.0"),
            new XElement("OutputType", executable ? "Exe" : "Library"), new XElement("ImplicitUsings", "enable"), new XElement("Nullable", "enable"), new XElement("TreatWarningsAsErrors", "true"),
            new XElement("ServerGarbageCollection", "false"), new XElement("IsPackable", "false"));
        var refs = (references ?? []).Select(path => new XElement("Reference", new XAttribute("Include", Path.GetFileNameWithoutExtension(path)), new XAttribute("HintPath", path), new XAttribute("Private", "true")));
        string target = Path.Combine(directory, "Fixture.csproj"); Write(target, new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"), properties, new XElement("ItemGroup", refs)).ToString());
        await Run("build-" + name, "build", target, "-c", "Release", "--nologo", "-m:1"); return Path.Combine(directory, "bin", "Release", "net10.0");
    }
    private async Task Prepare()
    {
        string core = input.PathOf("candidate-core"), producerPath = input.PathOf("candidate-report"); JsonObject producer = Read(producerPath, out string producerHash);
        Require(Hash(core) == corePin && (bool?)producer["complete"] == true && (string?)producer["coreSha256"] == corePin, "Candidate does not match completed producer and expected hash");
        string coreRoot = Path.Combine(language, "src", "CIS.Core");
        JsonObject sources = new(TreeHashes(Path.Combine(coreRoot, "TypeManagement"), path => path.EndsWith(".cs", StringComparison.Ordinal)).Select(p => new KeyValuePair<string, JsonNode?>("TypeManagement/" + p.Key, p.Value!.DeepClone())));
        JsonObject expected = new(producer["coreSourceInputs"]!.AsObject().Where(p => p.Key.StartsWith("TypeManagement/", StringComparison.Ordinal)).Select(p => new KeyValuePair<string, JsonNode?>(p.Key, p.Value!.DeepClone())));
        Require(sources.Count > 0 && JsonNode.DeepEquals(sources, expected), "Current module source differs from the candidate record");
        string[] dependencies = ["Microsoft.Extensions.Logging.Abstractions.dll", "Microsoft.Extensions.DependencyInjection.Abstractions.dll"];
        foreach (string name in dependencies) { Require((string?)producer["fixtureFiles"]!["Host/bin/Release/net10.0/" + name] == Hash(Path.Combine(Path.GetDirectoryName(core)!, name)), "Dependency differs from candidate producer: " + name); }
        string templates = Path.Combine(language, "tests", "fixtures", "TypeManager", "Performance");
        string contracts = await Project("CIS.TypeManager.PerformanceContracts", File.ReadAllText(Path.Combine(templates, "Contracts.cs"))), contract = Path.Combine(contracts, "CIS.TypeManager.PerformanceContracts.dll");
        string host = await Project("CIS.TypeManager.PerformanceHost", File.ReadAllText(Path.Combine(templates, "Host.cs")), new[] { core, contract }.Concat(dependencies.Select(name => Path.Combine(Path.GetDirectoryName(core)!, name))), executable: true);
        JsonObject identities = JsonNode.Parse((await Run("dependency-identities", "exec", "--fx-version", (string)policy["targetRuntime"]!, Path.Combine(host, "CIS.TypeManager.PerformanceHost.dll"), "--identities")).Output)!.AsObject();
        var catalogs = new JsonObject();
        foreach ((string size, int libraries, int classes) in new[] { ("small", 2, 32), ("large", 8, 512) })
        {
            string layout = Path.Combine(root, "deploy", size); Directory.CreateDirectory(layout);
            foreach (string suffix in new[] { ".dll", ".deps.json", ".runtimeconfig.json" }) { Copy(Path.Combine(host, "CIS.TypeManager.PerformanceHost" + suffix), Path.Combine(layout, "CIS.TypeManager.PerformanceHost" + suffix)); }
            foreach (string path in new[] { contract, core }.Concat(dependencies.Select(name => Path.Combine(Path.GetDirectoryName(core)!, name)))) { Copy(path, Path.Combine(layout, Path.GetFileName(path))); }
            foreach (string name in dependencies) { Require(Hash(Path.Combine(layout, name)) == (string?)producer["fixtureFiles"]!["Host/bin/Release/net10.0/" + name], "Deployed dependency changed"); }
            var names = new List<string>(["CIS.TypeManager.PerformanceHost", "CIS.TypeManager.PerformanceContracts", "CIS.Core", "Microsoft.Extensions.Logging.Abstractions", "Microsoft.Extensions.DependencyInjection.Abstractions"]);
            for (int index = 0; index < libraries; index++)
            {
                string name = "Perf." + size + ".Library" + index.ToString("D2", CultureInfo.InvariantCulture);
                var definitions = new List<string> { "using PerformanceFixtures; namespace SyntheticCatalog;" };
                for (int number = 0; number < classes; number++)
                {
                    var interfaces = new List<string>(); if (number % 2 == 0) { interfaces.Add("IPrepared"); }
                    if (number % 3 == 0) { interfaces.Add("ILazy"); }
                    string candidate = "Candidate" + number.ToString("D4", CultureInfo.InvariantCulture);
                    definitions.Add("internal sealed class " + candidate + (interfaces.Count == 0 ? "" : " : " + string.Join(", ", interfaces)) + " { private " + candidate + "() { Counters.Activated++; } }");
                }
                string built = await Project(name, string.Join("\n", definitions) + "\n", [contract]); Copy(Path.Combine(built, name + ".dll"), Path.Combine(layout, name + ".dll")); names.Add(name);
            }
            string Identity(string name) => (string?)identities[name] ?? name + ", Version=1.0.0.0, Culture=neutral, PublicKeyToken=null";
            WriteJson(Path.Combine(layout, "CIS.TypeManager.PerformanceHost.types.json"), new JsonObject
            {
                ["schemaVersion"] = 1,
                ["hostAssembly"] = Identity(names[0]),
                ["assemblies"] = new JsonArray(names.Select(name => (JsonNode)new JsonObject { ["assemblyName"] = Identity(name), ["relativePath"] = name + ".dll" }).ToArray())
            });
            catalogs[size] = new JsonObject { ["libraries"] = libraries, ["classesPerLibrary"] = classes, ["generatedClasses"] = libraries * classes };
        }
        report["coreSha256"] = Hash(core); report["candidateReportSha256"] = producerHash; report["moduleSourceInputs"] = sources; report["dependencyIdentities"] = identities;
        report["catalogs"] = catalogs; report["fixtureSourceInputs"] = TreeHashes(templates, p => p.EndsWith(".cs", StringComparison.Ordinal));
        report["generatedSourceInputs"] = TreeHashes(Path.Combine(root, "build"), p => Path.GetFileName(p) == "Program.cs");
        report["deployment"] = new JsonObject(TreeHashes(Path.Combine(root, "deploy")).Select(p => new KeyValuePair<string, JsonNode?>("deploy/" + p.Key, p.Value!.DeepClone())));
        Require(Hash(core) == corePin, "Producer Core changed during fixture build");
    }

    private async Task Measure(int samples, int warmups, int iterations)
    {
        string fixtures = input.PathOf("fixtures"), manifest = Path.Combine(fixtures, "results.json");
        string fixturePin = input.Get("expected-fixtures-sha256");
        JsonObject inputs = ReadPinned(manifest, fixturePin, "Fixture producer report hash mismatch");
        Require((bool?)inputs["complete"] == true && (string?)inputs["operation"] == "prepare" && (string?)inputs["coreSha256"] == corePin && (string?)inputs["harnessSha256"] == (string?)report["harnessSha256"], "Fixture provenance mismatch");
        string templates = Path.Combine(language, "tests", "fixtures", "TypeManager", "Performance");
        Require((string?)inputs["runtimePolicySha256"] == (string?)report["runtimePolicySha256"] && JsonNode.DeepEquals(inputs["fixtureSourceInputs"], TreeHashes(templates, p => p.EndsWith(".cs", StringComparison.Ordinal))), "Fixture source or runtime policy differs from producer");
        void VerifyDeployment()
        {
            var actual = new JsonObject(); var pending = new Stack<string>(); pending.Push(Path.Combine(fixtures, "deploy"));
            while (pending.TryPop(out string? directory))
            {
                Require((File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0, "Frozen deployment contains a linked directory");
                foreach (string path in Directory.EnumerateFileSystemEntries(directory))
                {
                    FileAttributes attributes = File.GetAttributes(path); Require((attributes & FileAttributes.ReparsePoint) == 0, "Frozen deployment contains a linked entry");
                    if ((attributes & FileAttributes.Directory) != 0) { pending.Push(path); }
                    else { actual[Path.GetRelativePath(fixtures, path).Replace('\\', '/')] = Hash(path); }
                }
            }
            Require(JsonNode.DeepEquals(actual, inputs["deployment"]), "Deployment file inventory/hashes mismatch");
            string boundary = Path.GetFullPath(Path.Combine(fixtures, "deploy")) + Path.DirectorySeparatorChar;
            foreach ((string relative, JsonNode? hash) in inputs["deployment"]!.AsObject())
            {
                string path = Path.GetFullPath(Path.Combine(fixtures, relative));
                Require(path.StartsWith(boundary, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) && Hash(path) == (string?)hash, "Frozen deployment changed: " + relative);
            }
            foreach ((string size, _) in inputs["catalogs"]!.AsObject()) { Require(Hash(Path.Combine(fixtures, "deploy", size, "CIS.Core.dll")) == corePin, "Deployment Core mismatch"); }
        }
        VerifyDeployment();
        report["coreSha256"] = corePin; report["fixtureReportSha256"] = fixturePin; report["deployment"] = inputs["deployment"]!.DeepClone();
        report["samplesPerGroup"] = samples; report["warmupRounds"] = warmups; report["queryIterations"] = iterations; report["observations"] = new JsonArray();
        for (int round = 0; round < warmups + samples; round++)
        {
            string[] modes = round % 2 == 0 ? ["eager", "deferred"] : ["deferred", "eager"];
            KeyValuePair<string, JsonNode?>[] catalogs = inputs["catalogs"]!.AsObject().ToArray(); if ((round / 2) % 2 != 0) { Array.Reverse(catalogs); }
            foreach ((string size, JsonNode? catalog) in catalogs)
            {
                foreach (string mode in modes)
                {
                    string name = size + "-" + mode + "-" + round.ToString("D2", CultureInfo.InvariantCulture);
                    string host = Path.Combine(fixtures, "deploy", size, "CIS.TypeManager.PerformanceHost.dll");
                    (string output, double wall) = await Run(name, "exec", "--fx-version", (string)policy["targetRuntime"]!, "--roll-forward", "Disable", host, mode,
                        ((int)catalog!["libraries"]!).ToString(CultureInfo.InvariantCulture), ((int)catalog["classesPerLibrary"]!).ToString(CultureInfo.InvariantCulture), iterations.ToString(CultureInfo.InvariantCulture));
                    JsonObject sample = JsonNode.Parse(output)!.AsObject();
                    Require((bool?)sample["valid"] == true && (string?)sample["mode"] == mode && (string?)sample["environment"]!["runtime"] == (string?)policy["targetRuntime"]
                        && (string?)sample["environment"]!["processArchitecture"] == "X64" && (string?)sample["environment"]!["osArchitecture"] == "X64", "Wrong or invalid measurement process");
                    sample["catalog"] = size; sample["round"] = round; sample["warmup"] = round < warmups; sample["processWallMilliseconds"] = wall;
                    report["observations"]!.AsArray().Add(sample); Save();
                }
            }
        }
        VerifyDeployment(); var summaries = new JsonObject();
        foreach ((string size, _) in inputs["catalogs"]!.AsObject())
        {
            foreach (string mode in new[] { "eager", "deferred" })
            {
                JsonObject[] rows = report["observations"]!.AsArray().Select(n => n!.AsObject()).Where(s => (string?)s["catalog"] == size && (string?)s["mode"] == mode && (bool?)s["warmup"] == false).ToArray();
                Require(rows.Length == samples, "Wrong measured sample count"); var metrics = new JsonObject();
                foreach (string name in new[] { "bootstrapBoundary", "firstUse", "preparation", "firstPrepared", "firstUncached", "preparedCached", "lazyCached", "loopControl", "exactLookup" })
                { foreach (string key in new[] { "Milliseconds", "AllocatedBytes" }) { metrics[name + "." + key] = Distribution(rows.Select(s => s[name]![key]!.GetValue<double>())); } }
                foreach (string name in new[] { "retainedManagedBytes", "retainedPrivateBytes", "retainedWorkingSetBytes", "processWallMilliseconds" }) { metrics[name] = Distribution(rows.Select(s => s[name]!.GetValue<double>())); }
                metrics["bootstrapPlusFirstUseMilliseconds"] = Distribution(rows.Select(s => s["bootstrapBoundary"]!["Milliseconds"]!.GetValue<double>() + s["firstUse"]!["Milliseconds"]!.GetValue<double>()));
                summaries[size + "-" + mode] = metrics;
            }
        }
        report["summaries"] = summaries;
    }
    private static JsonObject Distribution(IEnumerable<double> values)
    {
        double[] sorted = values.Order().ToArray(); Require(sorted.Length > 0 && sorted.All(double.IsFinite), "Invalid measurement values"); int middle = sorted.Length / 2;
        return new JsonObject { ["n"] = sorted.Length, ["minimum"] = sorted[0], ["median"] = sorted.Length % 2 == 0 ? (sorted[middle - 1] + sorted[middle]) / 2 : sorted[middle], ["maximum"] = sorted[^1] };
    }

    internal static async Task<int> RunControlsAsync(string[] arguments)
    {
        var input = new TypeManagerVerificationInputs(arguments, ["repo-root", "fixtures", "expected-fixtures-sha256", "expected-core-sha256", "output-root"], []);
        input.EmptyOutput(); string root = input.Output, fixtures = input.PathOf("fixtures"), original = Path.Combine(fixtures, "results.json"), tool = typeof(TypeManagerMeasurement).Assembly.Location;
        JsonObject producer = ReadPinned(original, input.Get("expected-fixtures-sha256"), "Control fixture report pin mismatch"); var report = new JsonObject { ["complete"] = false, ["qualified"] = false, ["verifierSha256"] = Hash(tool), ["results"] = new JsonArray() };
        string path = Path.Combine(root, "results.json"); WriteJson(path, report);
        try
        {
            string runtime = (string)Read(Path.Combine(input.Language, "eng", "toolchain.json"))["targetRuntime"]!;
            foreach (string name in new[] { "changed-report-pin", "incomplete-producer", "changed-harness", "changed-template", "changed-deployment", "extra-deployment-file", "insufficient-samples" })
            {
                string selected = fixtures, pin = input.Get("expected-fixtures-sha256"), reason;
                if (name == "changed-report-pin") { pin = new string('0', 64); reason = "Fixture producer report hash mismatch"; }
                else if (name == "insufficient-samples") { reason = "Use at least 12 samples"; }
                else
                {
                    selected = Path.Combine(root, name, "fixtures"); CopyTree(Path.Combine(fixtures, "deploy"), Path.Combine(selected, "deploy"));
                    JsonObject changed = producer.DeepClone().AsObject();
                    if (name == "incomplete-producer") { changed["complete"] = false; reason = "Fixture provenance mismatch"; }
                    else if (name == "changed-harness") { changed["harnessSha256"] = new string('0', 64); reason = "Fixture provenance mismatch"; }
                    else if (name == "changed-template") { changed["fixtureSourceInputs"]!["Host.cs"] = new string('0', 64); reason = "Fixture source or runtime policy differs from producer"; }
                    else
                    {
                        reason = "Deployment file inventory/hashes mismatch";
                        if (name == "extra-deployment-file") { Write(Path.Combine(selected, "deploy", "small", "unexpected.txt"), "unrecorded"); }
                        else { using FileStream changedCore = new(Path.Combine(selected, "deploy", "small", "CIS.Core.dll"), FileMode.Append); changedCore.WriteByte(0); }
                    }
                    string selectedReport = Path.Combine(selected, "results.json"); WriteJson(selectedReport, changed); pin = Hash(selectedReport);
                }
                string output = Path.Combine(root, name, "run");
                await input.Run(name, ["exec", "--fx-version", runtime, "--roll-forward", "Disable", tool, "type-manager-measure", "run",
                    "--repo-root", input.PathOf("repo-root"), "--output-root", output, "--fixtures", selected, "--expected-fixtures-sha256", pin,
                    "--expected-core-sha256", input.Get("expected-core-sha256"), "--samples", name == "insufficient-samples" ? "8" : "12", "--warmups", "1", "--iterations", "1000", "--dotnet", input.Dotnet], expectedError: reason);
                JsonObject rejected = Read(Path.Combine(output, "results.json"));
                Require((bool?)rejected["complete"] == false && (rejected["observations"] is null || rejected["observations"]!.AsArray().Count == 0), "Rejected measurement produced complete observations");
                report["results"]!.AsArray().Add(new JsonObject { ["case"] = name, ["passed"] = true, ["rejectedFor"] = reason }); WriteJson(path, report);
            }
            Require(Hash(original) == input.Get("expected-fixtures-sha256"), "Original fixture report changed"); report["complete"] = true; return 0;
        }
        catch (Exception error) { report["error"] = error.Message; throw; }
        finally { WriteJson(path, report); }
    }
}
