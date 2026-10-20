using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using static CIS.Verification.TypeManagerVerificationInputs;

namespace CIS.Verification;

// Real first-publication controls; an empty publication ledger is never historical replay credit.
internal static class FirstReleaseCompatibility
{
    internal static async Task<int> RunAsync(string[] args)
    {
        var input = new TypeManagerVerificationInputs(args,
            ["repo-root", "candidate-package", "dependency-directory", "output-root"], ["frozen-fixtures", "fixture-directory"]);
        input.EmptyOutput();
        string root = input.Output, repo = input.PathOf("repo-root"), package = input.PathOf("candidate-package");
        string dependencies = input.PathOf("dependency-directory"), language = input.Language;
        string ledgerPath = Path.Combine(repo, "compatibility/csharp/publications/ledger.json");
        JsonObject ledger = Read(ledgerPath), policy = Read(Path.Combine(language, "eng/toolchain.json"));
        Require(ledger["publications"]!.AsArray().Count == 0, "Published history requires cumulative replay; first-release controls cannot replace it.");
        Require((string?)policy["sdk"] == "10.0.401" && (string?)policy["targetRuntime"] == "10.0.12", "Requalify consumer settings for changed toolchain pins.");
        string fixture = input.Optional("fixture-directory") is { } suppliedFixture ? Path.GetFullPath(suppliedFixture) : Path.Combine(language, "verification/compatibility/first-release");
        string packagePin = Hash(package), ledgerPin = Hash(ledgerPath);
        JsonObject sources = input.CoreSources(), projects = input.CoreProjects();
        var report = new JsonObject
        {
            ["schema"] = "cis.first-release-compatibility/v1",
            ["qualified"] = false,
            ["historyStatus"] = "NoBaselineForIdentity",
            ["historicalVersionsReplayed"] = 0,
            ["scope"] = "Exact candidate package and first-release positive/negative controls, not released historical replay",
            ["candidatePackageSha256"] = packagePin,
            ["ledgerSha256"] = ledgerPin,
            ["verifierSha256"] = Hash(typeof(FirstReleaseCompatibility).Assembly.Location),
            ["coreSourceInputs"] = sources.DeepClone(),
            ["results"] = new JsonArray()
        };
        string reportPath = Path.Combine(root, "results.json");
        var environment = new Dictionary<string, string?>
        {
            ["MSBUILDDISABLENODEREUSE"] = "1",
            ["UseSharedCompilation"] = "false",
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
            ["GIT_CEILING_DIRECTORIES"] = root
        };
        foreach (string key in Environment.GetEnvironmentVariables().Keys.Cast<string>().Where(key => key.StartsWith("CORE_", StringComparison.OrdinalIgnoreCase) || key.StartsWith("DD_", StringComparison.OrdinalIgnoreCase)))
        { environment[key] = null; }
        void Passed(string name, JsonNode? observation = null) => report["results"]!.AsArray().Add(new JsonObject { ["id"] = name, ["passed"] = true, ["observation"] = observation });
        try
        {
            Require((await input.Run("sdk", ["--version"], language)).StandardOutput.Trim() == (string?)policy["sdk"], "Wrong SDK selected.");
            string feed = Path.Combine(root, "feed"); Directory.CreateDirectory(feed);
            using var zip = ZipFile.OpenRead(package);
            ZipArchiveEntry coreEntry = zip.Entries.Single(entry => entry.FullName == "lib/net10.0/CIS.Core.dll");
            string expectedCore = Path.Combine(root, "references/CIS.Core.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(expectedCore)!);
            coreEntry.ExtractToFile(expectedCore);
            string corePin = Hash(expectedCore);
            Require(corePin == Hash(Path.Combine(dependencies, "CIS.Core.dll")), "Package and selected built dependency directory differ.");
            report["candidateCoreSha256"] = corePin;
            Copy(package, Path.Combine(feed, "CIS.Core.0.1.0-dev.1.nupkg"), packagePin);
            JsonObject assets = Read(Path.Combine(language, "src/CIS.Core/obj/project.assets.json"));
            string[] cacheRoots = assets["packageFolders"]!.AsObject().Select(row => row.Key).ToArray();
            JsonObject approved = Read(Path.Combine(language, "eng/runtime-dependencies.json"));
            foreach (JsonNode? dependency in approved["packages"]!.AsArray())
            {
                string id = dependency!["id"]!.GetValue<string>(), version = dependency["version"]!.GetValue<string>();
                string relative = Path.Combine(id.ToLowerInvariant(), version, id.ToLowerInvariant() + "." + version + ".nupkg");
                string nupkg = cacheRoots.Select(cache => Path.Combine(cache, relative)).Single(File.Exists);
                Copy(nupkg, Path.Combine(feed, Path.GetFileName(nupkg)));
                Copy(Path.Combine(dependencies, id + ".dll"), Path.Combine(root, "references", id + ".dll"));
            }
            string original = Path.Combine(root, "original"); Directory.CreateDirectory(original);
            Copy(Path.Combine(fixture, "Program.cs"), Path.Combine(original, "Program.cs"));
            Copy(Path.Combine(fixture, "Consumer.csproj.template"), Path.Combine(original, "Consumer.csproj"));
            var originalPins = TreeHashes(original);
            await BuildConsumer("source", original, feed, expectedError: null);
            string compiled = Path.Combine(original, "bin/Release/net10.0");
            await Execute("source", compiled, corePin);
            string frozen = Path.Combine(root, "frozen");
            string? prior = input.Optional("frozen-fixtures");
            if (prior is null) { CopyTree(compiled, frozen); }
            else
            {
                JsonObject previous = Read(Path.Combine(prior, "results.json"));
                Require((bool?)previous["qualified"] == true && (string?)previous["candidatePackageSha256"] == packagePin, "Cross-lane frozen fixtures identify another or incomplete candidate.");
                CopyTree(Path.Combine(prior, "frozen"), frozen);
                Require(JsonNode.DeepEquals(TreeHashes(frozen), previous["frozenFiles"]), "Cross-lane frozen consumer bytes changed.");
            }
            JsonObject frozenPins = TreeHashes(frozen); report["frozenFiles"] = frozenPins.DeepClone();
            foreach (string scenario in new[] { "CoreReplacement", "CompleteUpgrade" })
            {
                string directory = Path.Combine(root, scenario); CopyTree(frozen, directory);
                Copy(expectedCore, Path.Combine(directory, "CIS.Core.dll"), corePin);
                await Execute(scenario, directory, corePin);
            }
            await input.Run("tool-restore", ["tool", "restore", "--configfile", "eng/Tools.NuGet.config"], language);
            string toolVersion = policy["tools"]!.AsArray().Single(node => (string?)node!["id"] == "Microsoft.DotNet.ApiCompat.Tool")!["version"]!.GetValue<string>();
            string api = cacheRoots.Select(cache => Path.Combine(cache, "microsoft.dotnet.apicompat.tool", toolVersion, "tools/net8.0/any/Microsoft.DotNet.ApiCompat.Tool.dll")).Single(File.Exists);
            string netRoot = (await input.Run("reference-pack-root", ["msbuild", Path.Combine(language, "src/CIS.Core/CIS.Core.csproj"), "-nologo", "-getProperty:NetCoreRoot"], language)).StandardOutput.Trim();
            string referencePack = Path.Combine(netRoot, "packs/Microsoft.NETCore.App.Ref/10.0.12/ref/net10.0");
            Require(Directory.Exists(referencePack), "The pinned .NET 10 reference pack is missing.");
            JsonObject referencePins = TreeHashes(referencePack, path => path.EndsWith(".dll", StringComparison.Ordinal));
            Require(referencePins.Count > 0, "Reference pack has no assemblies.");
            report["apiReferenceInputs"] = referencePins.DeepClone();
            await Api("api-self", expectedCore, negative: false);
            string controlled = Path.Combine(root, "controlled-source");
            string controlledLanguage = Path.Combine(controlled, "implementations/csharp");
            CopyTree(language, controlledLanguage, sourceOnly: true);
            Copy(Path.Combine(repo, "README.md"), Path.Combine(controlled, "README.md"));
            string controlledCore = Path.Combine(controlledLanguage, "src/CIS.Core/CIS.Core.csproj");
            string addition = Path.Combine(controlledLanguage, "src/CIS.Core/Logging/QualificationAddition.cs");
            Write(addition, "namespace CIS.Logging; public sealed partial class CoreLogger { /// <summary>Qualification-only additive API control.</summary>\n public bool QualificationAddition() => true; }\n");
            await input.Run("additive-build", ["build", controlledCore, "-c", "Release", "-m:1", "-p:RestoreLockedMode=true"], language, environment: environment);
            string changedCore = Path.Combine(controlledLanguage, "src/CIS.Core/bin/Release/net10.0/CIS.Core.dll");
            await Api("api-additive", changedCore, negative: false);
            string changed = Path.Combine(controlledLanguage, "src/CIS.Core/Logging/CoreLogger.Interpolation.cs");
            const string signature = "public LogWriteResult Info(ref LogInterpolatedStringHandler message, IReadOnlyDictionary<string, LogValue> values)";
            string text = File.ReadAllText(changed);
            Require(text.Split(signature, StringSplitOptions.None).Length == 2, "Expected exactly one controlled overload.");
            Write(changed, text.Replace(signature, signature.Replace(" Info(", " RemovedInfo(", StringComparison.Ordinal), StringComparison.Ordinal));
            await input.Run("breaking-build", ["build", controlledCore, "-c", "Release", "-m:1", "--no-restore"], language, environment: environment);
            await Api("api-breaking", changedCore, negative: true);
            string badFeed = Path.Combine(root, "breaking-feed");
            foreach (string file in Directory.GetFiles(feed).Where(path => !Path.GetFileName(path).StartsWith("CIS.Core.", StringComparison.OrdinalIgnoreCase))) { Copy(file, Path.Combine(badFeed, Path.GetFileName(file))); }
            await input.Run("breaking-pack", ["pack", controlledCore, "-c", "Release", "--no-build", "--no-restore", "-o", badFeed], language, environment: environment);
            string badSource = Path.Combine(root, "breaking-source");
            foreach ((string path, JsonNode? pin) in originalPins) { Copy(Path.Combine(original, path), Path.Combine(badSource, path), (string)pin!); }
            await BuildConsumer("source-breaking", badSource, badFeed, "CS1503");
            Passed("source-breaking");
            string badBinary = Path.Combine(root, "breaking-binary"); CopyTree(frozen, badBinary);
            Copy(changedCore, Path.Combine(badBinary, "CIS.Core.dll"));
            await input.Run("binary-breaking", ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", Path.Combine(badBinary, "CIS.FirstRelease.Consumer.dll")], badBinary, expectedError: "MissingMethodException", environment: environment);
            Passed("binary-breaking");
            Require(JsonNode.DeepEquals(frozenPins, TreeHashes(frozen)), "Frozen consumer bytes changed during controls.");
            foreach ((string path, JsonNode? pin) in originalPins) { Require(Hash(Path.Combine(original, path)) == (string?)pin, "Original consumer source changed."); }
            Require(Hash(package) == packagePin && Hash(ledgerPath) == ledgerPin && JsonNode.DeepEquals(sources, input.CoreSources()) && JsonNode.DeepEquals(projects, input.CoreProjects()), "Candidate or source inputs changed during qualification.");
            Require(JsonNode.DeepEquals(referencePins, TreeHashes(referencePack, path => path.EndsWith(".dll", StringComparison.Ordinal))), "API reference pack changed during qualification.");
            Require(Hash(Path.Combine(fixture, "Program.cs")) == (string?)originalPins["Program.cs"] && Hash(Path.Combine(fixture, "Consumer.csproj.template")) == (string?)originalPins["Consumer.csproj"], "Supplied consumer fixture changed during qualification.");
            report["qualified"] = true;
            return 0;

            async Task BuildConsumer(string name, string directory, string packageFeed, string? expectedError)
            {
                string config = Path.Combine(root, name + ".NuGet.config");
                new XDocument(new XElement("configuration", new XElement("packageSources", new XElement("clear"), new XElement("add", new XAttribute("key", "candidate"), new XAttribute("value", packageFeed))), new XElement("fallbackPackageFolders", new XElement("clear")), new XElement("auditSources", new XElement("clear")))).Save(config);
                string project = Path.Combine(directory, "Consumer.csproj");
                var childEnvironment = new Dictionary<string, string?>(environment) { ["NUGET_PACKAGES"] = Path.Combine(root, name + "-packages"), ["NUGET_HTTP_CACHE_PATH"] = Path.Combine(root, name + "-http") };
                await input.Run(name + "-restore", ["restore", project, "--configfile", config, "--packages", childEnvironment["NUGET_PACKAGES"]!, "-p:NuGetAudit=false"], language, environment: childEnvironment);
                await input.Run(name + "-build", ["build", project, "-c", "Release", "--no-restore", "-m:1", "-p:NuGetAudit=false"], language, expectedError: expectedError, environment: childEnvironment);
            }
            async Task Execute(string name, string directory, string pin)
            {
                var result = await input.Run(name, ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", Path.Combine(directory, "CIS.FirstRelease.Consumer.dll")], directory, environment: environment);
                JsonObject observed = JsonNode.Parse(result.StandardOutput)!.AsObject();
                Require((bool?)observed["succeeded"] == true && (string?)observed["coreSha256"] == pin && (string?)observed["runtime"] == "10.0.12" && (int?)observed["records"] == 2, "Consumer observation differs from candidate/runtime/behavior.");
                Passed(name, observed);
            }
            async Task Api(string name, string right, bool negative)
            {
                string references = Path.Combine(root, "references") + "," + referencePack;
                var result = await input.Run(name, ["exec", "--fx-version", "8.0.31", "--roll-forward", "Disable", api, "--left", expectedCore, "--right", right, "--enable-rule-attributes-must-match", "--enable-rule-cannot-change-parameter-name", "--left-assembly-references", references, "--right-assembly-references", references], language, expectedError: negative ? "CP0002" : null, environment: environment);
                Require(!(result.StandardOutput + result.StandardError).Contains("Could not resolve reference", StringComparison.Ordinal), "API comparison has unresolved references.");
                Require(!negative || (result.StandardOutput + result.StandardError).Contains("CoreLogger.Info", StringComparison.Ordinal), "API control did not identify the removed logger overload.");
                Passed(name);
            }
        }
        catch (Exception error) { report["error"] = error.Message; throw; }
        finally { WriteJson(reportPath, report); }
    }
}
