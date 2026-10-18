using System.Text.Json.Nodes;
using CIS.TestSupport;
using static CIS.Verification.TypeManagerVerificationInputs;

namespace CIS.Verification;

// Preserve the three unpublished provider scenarios in the existing engineering tool.
internal static class MicrosoftLoggingCompatibilityChecks
{
    private const string Consumer = "CIS.Logging.ProviderContractConsumer";
    private static readonly Dictionary<string, string> LoadedFiles = new(StringComparer.Ordinal)
    {
        ["core"] = "CIS.Core.dll",
        ["logging"] = "Microsoft.Extensions.Logging.Abstractions.dll",
        ["dependencyInjection"] = "Microsoft.Extensions.DependencyInjection.Abstractions.dll",
        ["consumer"] = Consumer + ".dll"
    };

    internal static async Task<int> RunAsync(string[] args)
    {
        var input = new TypeManagerVerificationInputs(args, ["repo-root", "candidate-core", "output-root"], []);
        Require(!Directory.Exists(input.Output) && !File.Exists(input.Output), "Use a new output directory to retain immutable observations.");
        input.EmptyOutput();
        string core = input.PathOf("candidate-core"), corePin = Hash(core);
        string fixtures = Path.Combine(input.Language, "verification/compatibility/microsoft-logging");
        JsonObject fixturePins = TreeHashes(fixtures);
        string policyPath = Path.Combine(input.Language, "eng/toolchain.json");
        JsonObject policy = Read(policyPath, out string policyPin);
        string ledgerPath = Path.Combine(input.PathOf("repo-root"), "compatibility/csharp/publications/ledger.json");
        JsonObject ledger = Read(ledgerPath, out string ledgerPin);
        int publicationRows = ledger["publications"]!.AsArray().Count;
        var sourceInputs = new JsonArray(fixturePins.Select(row => (JsonNode?)new JsonObject { ["path"] = row.Key, ["sha256"] = row.Value!.DeepClone() }).ToArray());
        var report = new JsonObject
        {
            ["publicationRows"] = publicationRows,
            ["historyStatus"] = publicationRows == 0 ? "NoHistoricalPublications" : "HistoricalReplayNotPerformedByFixtureRunner",
            ["scope"] = "Unpublished provider fixture qualification; not historical compatibility or full release",
            ["completed"] = false,
            ["historicalComparisons"] = 0,
            ["coreSha256"] = corePin,
            ["sourceInputs"] = sourceInputs,
            ["runnerSha256"] = Hash(Path.Combine(input.Language, "eng/CIS.DependencyChecker/MicrosoftLoggingCompatibilityChecks.cs")),
            ["wrapperSha256"] = Hash(Path.Combine(input.Language, "eng/check-microsoft-logging-compatibility.ps1")),
            ["toolSha256"] = Hash(typeof(MicrosoftLoggingCompatibilityChecks).Assembly.Location),
            ["toolchainSha256"] = policyPin,
            ["ledgerSha256"] = ledgerPin,
            ["results"] = new JsonArray()
        };
        try
        {
            string sdk = (await input.Run("sdk", ["--version"], input.Language)).StandardOutput.Trim();
            Require(sdk == (string?)policy["sdk"], "SDK differs from policy.");
            string runtime = (string)policy["targetRuntime"]!;
            JsonObject api = ReadPinned(Path.Combine(fixtures, "api-contract.json"), (string)fixturePins["api-contract.json"]!, "API fixture changed.");
            string assetsPath = Path.Combine(input.Language, "src/CIS.Core/obj/project.assets.json");
            string approvedPath = Path.Combine(input.Language, "eng/runtime-dependencies.json");
            JsonObject assets = Read(assetsPath, out string assetsPin), approved = Read(approvedPath, out string approvedPin);
            report["assetsSha256"] = assetsPin; report["runtimePolicySha256"] = approvedPin;
            Dictionary<string, string> libraries = ResolveLibraries(assets, approved, core);
            var libraryPins = libraries.ToDictionary(row => row.Key, row => Hash(row.Value), StringComparer.Ordinal);
            string source = Path.Combine(input.Output, "source");
            Copy(Path.Combine(fixtures, "source/Program.cs"), Path.Combine(source, "Program.cs"), (string)fixturePins["source/Program.cs"]!);
            Copy(Path.Combine(fixtures, "Consumer.csproj.template"), Path.Combine(source, "Consumer.csproj"), (string)fixturePins["Consumer.csproj.template"]!);
            foreach (string name in new[] { "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props" }) { Write(Path.Combine(source, name), "<Project />"); }
            Write(Path.Combine(source, "NuGet.config"), "<configuration><packageSources><clear /></packageSources></configuration>");
            await input.Run("source-build", ["build", Path.Combine(source, "Consumer.csproj"), "-c", "Release", "-m:1",
                "-p:CoreAssemblyPath=" + core, "-p:LoggingAssemblyPath=" + libraries[LoadedFiles["logging"]],
                "-p:DependencyInjectionAssemblyPath=" + libraries[LoadedFiles["dependencyInjection"]]], input.Language);
            string compiled = Path.Combine(source, "bin/Release/net10.0");
            await InvokeConsumer("Source", compiled);
            string frozen = Path.Combine(input.Output, "frozen");
            var frozenPins = new JsonObject();
            foreach (string suffix in new[] { ".dll", ".pdb", ".deps.json", ".runtimeconfig.json" })
            {
                string name = Consumer + suffix, pin = Hash(Path.Combine(compiled, name));
                Copy(Path.Combine(compiled, name), Path.Combine(frozen, name), pin);
                frozenPins[name] = pin;
            }
            foreach (string scenario in new[] { "CoreReplacement", "CompleteUpgrade" })
            {
                string directory = Path.Combine(input.Output, scenario);
                foreach ((string name, JsonNode? pin) in frozenPins) { Copy(Path.Combine(frozen, name), Path.Combine(directory, name), (string)pin!); }
                foreach ((string name, string path) in libraries) { Copy(path, Path.Combine(directory, name), libraryPins[name]); }
                RequireUnchanged(directory, frozenPins);
                await InvokeConsumer(scenario, directory);
                RequireUnchanged(directory, frozenPins);
            }
            RequireUnchanged(frozen, frozenPins);
            RequireUnchanged(fixtures, fixturePins);
            foreach ((string name, string path) in libraries) { Require(Hash(path) == libraryPins[name], "Runtime input changed: " + name); }
            Require(Hash(core) == corePin && Hash(policyPath) == policyPin && Hash(ledgerPath) == ledgerPin &&
                Hash(assetsPath) == assetsPin && Hash(approvedPath) == approvedPin, "Compatibility input changed during qualification.");
            report["frozenInputs"] = new JsonArray(frozenPins.Select(row => (JsonNode?)new JsonObject { ["name"] = row.Key, ["sha256"] = row.Value!.DeepClone() }).ToArray());
            report["runtimeInputs"] = new JsonArray(libraries.Select(row => (JsonNode?)new JsonObject { ["name"] = row.Key, ["sha256"] = libraryPins[row.Key] }).ToArray());
            report["sameRuntimePayloadForBothScenarios"] = true;
            report["completed"] = true;
            Console.WriteLine("Provider compatibility fixtures passed. Reports: " + input.Output);
            return 0;

            async Task InvokeConsumer(string id, string directory)
            {
                string consumer = Path.Combine(directory, Consumer + ".dll");
                var environment = Environment.GetEnvironmentVariables().Keys.Cast<string>()
                    .Where(key => key.StartsWith("CORE_", StringComparison.OrdinalIgnoreCase) || new[] { "DD_SERVICE", "DD_VERSION", "DD_ENV" }.Contains(key, StringComparer.OrdinalIgnoreCase))
                    .ToDictionary(key => key, _ => (string?)null, StringComparer.Ordinal);
                ProcessResult child = await input.Run(id, ["exec", "--fx-version", runtime, "--roll-forward", "Disable", consumer, Path.Combine(input.Output, "observations", id)],
                    directory, environment: environment, timeout: TimeSpan.FromSeconds(60), requireSuccess: false);
                Write(Path.Combine(input.Output, id + ".stdout.json"), child.StandardOutput);
                Write(Path.Combine(input.Output, id + ".stderr.txt"), child.StandardError);
                ValidateProcess(child.ExitCode, child.StandardError);
                JsonObject observed = JsonNode.Parse(child.StandardOutput)!.AsObject();
                ValidateObservation(observed, directory, corePin, api, runtime);
                report["results"]!.AsArray().Add(new JsonObject { ["id"] = id, ["passed"] = true, ["actual"] = observed, ["consumerSha256"] = Hash(consumer) });
            }
        }
        catch (Exception error) { report["error"] = error.Message; throw; }
        finally { WriteJson(Path.Combine(input.Output, "results.json"), report); }
    }

    internal static Dictionary<string, string> ResolveLibraries(JsonObject assets, JsonObject approved, string core)
    {
        var libraries = new Dictionary<string, string>(StringComparer.Ordinal) { ["CIS.Core.dll"] = core };
        foreach (JsonNode? package in approved["packages"]!.AsArray())
        {
            string key = (string)package!["id"]! + "/" + (string)package["version"]!;
            JsonObject? runtime = assets["targets"]?["net10.0"]?[key]?["runtime"]?.AsObject();
            string[] dlls = runtime?.Select(row => row.Key).Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)).ToArray() ?? [];
            Require(dlls.Length == 1, "Expected one approved runtime assembly per package: " + key);
            string packagePath = (string?)assets["libraries"]?[key]?["path"] ?? throw new InvalidDataException("Approved package path missing: " + key);
            string relative = Path.Combine(packagePath, dlls[0]).Replace('\\', Path.DirectorySeparatorChar);
            string[] found = assets["packageFolders"]!.AsObject().Select(row => Path.GetFullPath(Path.Combine(row.Key, relative))).Where(File.Exists).ToArray();
            Require(found.Length == 1, "Approved dependency missing or ambiguous: " + key);
            Require(libraries.TryAdd(Path.GetFileName(found[0]), found[0]), "Duplicate approved runtime assembly name.");
        }
        return libraries;
    }

    internal static void ValidateProcess(int exitCode, string stderr) =>
        Require(exitCode == 0 && stderr.Length == 0, "Consumer failed; inspect retained native stdout/stderr.");

    internal static void RequireUnchanged(string directory, JsonObject pins)
    {
        foreach ((string file, JsonNode? pin) in pins) { Require(Hash(Path.Combine(directory, file)) == (string?)pin, "Frozen or source input changed: " + file); }
    }

    internal static void ValidateObservation(JsonObject observed, string directory, string corePin, JsonObject api, string runtime)
    {
        Require((string?)observed["contract"] == "passed" && (int?)observed["records"] == 3 && (string?)observed["runtime"] == runtime, "Unexpected consumer result.");
        foreach ((string name, string file) in LoadedFiles)
        {
            string expected = Path.GetFullPath(Path.Combine(directory, file));
            string actual = Path.GetFullPath((string)observed[name]!["path"]!);
            Require(actual.Equals(expected, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) &&
                (string?)observed[name]!["sha256"] == Hash(expected), "Unexpected actual loaded assembly: " + name);
        }
        Require((string?)observed["core"]!["sha256"] == corePin, "Consumer did not load selected Core bytes.");
        JsonObject actualApi = observed["api"]!.AsObject();
        Require(JsonNode.DeepEquals(actualApi["type"], api["type"]) && JsonNode.DeepEquals(actualApi["isSealed"], api["isSealed"]), "Provider API identity changed.");
        foreach (JsonNode? contract in api["interfaces"]!.AsArray())
        { Require(actualApi["interfaces"]!.AsArray().Any(item => JsonNode.DeepEquals(item, contract)), "Supported provider interface missing."); }
        Signature(api["constructor"]!, actualApi["constructor"]!);
        foreach (JsonNode? method in api["methods"]!.AsArray())
        {
            JsonNode?[] methods = actualApi["methods"]!.AsArray().Where(row => (string?)row!["name"] == (string?)method!["name"]).ToArray();
            Require(methods.Length == 1 && JsonNode.DeepEquals(methods[0]!["returns"], method!["returns"]), "Supported method missing or return changed.");
            Signature(method!["signature"]!, methods[0]!["signature"]!);
        }

        static void Signature(JsonNode expected, JsonNode actual)
        {
            JsonArray left = expected["parameters"]!.AsArray(), right = actual["parameters"]!.AsArray();
            Require(left.Count == right.Count, "Provider parameter count changed.");
            for (int index = 0; index < left.Count; index++)
            {
                foreach (string field in new[] { "name", "type", "optional" })
                { Require(JsonNode.DeepEquals(left[index]![field], right[index]![field]), "Provider parameter " + field + " changed."); }
            }
        }
    }
}
