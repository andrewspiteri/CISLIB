using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using static CIS.Verification.TypeManagerVerificationInputs;

namespace CIS.Verification;

// Development qualification of actual application sources against one supplied
// package. It neither publishes that package nor qualifies an entire release.
internal sealed partial class HealthcheckPackageVerification
{
    private readonly TypeManagerVerificationInputs input;
    private readonly string language, root, package, provenance, dependencyCache, feed;
    private readonly IReadOnlyDictionary<string, string?> environment;
    private readonly JsonObject report;
    private readonly Dictionary<string, string> originals = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> staged = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Consumer> consumers = new(StringComparer.Ordinal);
    private string version = "", coreHash = "", packageHash = "", provenanceHash = "";
    private const string OriginPrefix = "CIS_HEALTHCHECK_PACKAGE_ORIGIN ";

    private sealed record Consumer(string Name, string Source, string Project, string Assembly, string Output);

    private HealthcheckPackageVerification(TypeManagerVerificationInputs input)
    {
        this.input = input;
        language = input.PathOf("language-root"); root = input.PathOf("output");
        package = input.PathOf("package"); provenance = input.PathOf("provenance");
        dependencyCache = input.PathOf("dependency-cache"); feed = Path.Combine(root, "feed");
        var isolated = new Dictionary<string, string?>(HermeticEnvironment(root))
        {
            ["UseSharedCompilation"] = "false",
            ["DOTNET_ROLL_FORWARD"] = "Disable",
            ["DOTNET_MULTILEVEL_LOOKUP"] = "0"
        };
        environment = isolated;
        report = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["complete"] = false,
            ["scope"] = "Native source-copy consumers of one exact supplied package; no publication or release qualification",
            ["platform"] = RuntimeInformation.OSDescription,
            ["architecture"] = RuntimeInformation.ProcessArchitecture.ToString(),
            ["verifierSha256"] = Hash(typeof(HealthcheckPackageVerification).Assembly.Location),
            ["expectedSdk"] = "10.0.401",
            ["expectedRuntime"] = ".NET 10.0.12",
            ["results"] = new JsonArray(),
            ["processes"] = new JsonArray()
        };
    }

    internal static async Task<int> RunAsync(string[] arguments)
    {
        if (arguments.Length > 0 && arguments[0] == "prepared-inputs") { return WritePreparedInputs(arguments[1..]); }
        var input = new TypeManagerVerificationInputs(arguments,
            ["language-root", "package", "dependency-cache", "provenance", "output"], []);
        string output = input.PathOf("output"), language = input.PathOf("language-root");
        Require(RuntimeInformation.ProcessArchitecture == Architecture.X64 && (OperatingSystem.IsWindows() || OperatingSystem.IsLinux()),
            "Only an actual Windows x64 or Linux x64 execution can qualify its own lane.");
        Require(Directory.Exists(language) && File.Exists(input.PathOf("package")) && File.Exists(input.PathOf("provenance")), "Missing qualification input.");
        Require(!SamePath(output, language) && !SamePath(output, Path.GetDirectoryName(input.PathOf("package"))!), "Use a separate output directory.");
        Require(!Directory.Exists(output) || !Directory.EnumerateFileSystemEntries(output).Any(), "Use an empty output directory; prior attempts are retained.");
        Directory.CreateDirectory(Path.Combine(output, "logs"));
        return await new HealthcheckPackageVerification(input).VerifyAsync().ConfigureAwait(false);
    }

    private async Task<int> VerifyAsync()
    {
        int exit = 1;
        try
        {
            Setup(); Save();
            ProcessObservation sdk = await RunChildAsync("sdk", ["--version"], root).ConfigureAwait(false);
            Require(sdk.ExitCode == 0 && !sdk.TimedOut && sdk.StandardOutput.Trim() == "10.0.401", "The exact pinned SDK was not selected.");
            await BuildConsumersAsync().ConfigureAwait(false);
            await CountryListAsync().ConfigureAwait(false);
            await TransportAsync().ConfigureAwait(false);
            report["complete"] = true;
            exit = 0;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException
            or InvalidOperationException or JsonException or System.Xml.XmlException or TimeoutException or System.ComponentModel.Win32Exception
            or System.Net.Http.HttpRequestException or OperationCanceledException)
        { report["failure"] = error.ToString(); }
        finally
        {
            try
            {
                Require(packageHash.Length == 64 && Hash(package) == packageHash, "Supplied package changed during qualification.");
                Require(provenanceHash.Length == 64 && Hash(provenance) == provenanceHash, "Producer provenance changed during qualification.");
                foreach ((string path, string hash) in originals) { Require(Hash(path) == hash, "Original input changed during qualification: " + path); }
                foreach ((string path, string hash) in staged) { Require(Hash(path) == hash, "Staged input changed during qualification: " + path); }
                ValidatePreparedProducer(Read(provenance), language, package);
                report["inputsVerifiedAfter"] = true;
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or JsonException or System.Xml.XmlException)
            {
                report["integrityFailure"] = error.ToString(); report["complete"] = false; exit = 1;
            }
            report["exitCode"] = exit; report["finishedUtc"] = DateTimeOffset.UtcNow; Save();
        }
        Console.WriteLine(report.ToJsonString());
        return exit;
    }

    private void Setup()
    {
        packageHash = Hash(package); provenanceHash = Hash(provenance);
        using (ZipArchive archive = ZipFile.OpenRead(package))
        {
            ZipArchiveEntry[] specs = archive.Entries.Where(entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal)).ToArray();
            Require(specs.Length == 1, "Expected exactly one package specification.");
            using Stream specification = specs[0].Open();
            XElement metadata = XDocument.Load(specification).Descendants().Single(element => element.Name.LocalName == "metadata");
            Require(metadata.Elements().Single(element => element.Name.LocalName == "id").Value == "CIS.Core", "The supplied package is not CIS.Core.");
            version = metadata.Elements().Single(element => element.Name.LocalName == "version").Value;
            Require(version.Length > 0 && version.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '+'), "Unsafe package version.");
            ZipArchiveEntry core = archive.GetEntry("lib/net10.0/CIS.Core.dll") ?? throw new InvalidDataException("Missing net10.0 Core package asset.");
            using Stream data = core.Open(); coreHash = Convert.ToHexStringLower(SHA256.HashData(data));
        }
        JsonObject producer = Read(provenance);
        if ((string?)producer["schema"] == PreparedInputSchema) { ValidatePreparedOutput(language, root); }
        ValidatePreparedProducer(producer, language, package);
        Require((string?)producer["packageSha256"] == packageHash && (string?)producer["coreSha256"] == coreHash, "Producer provenance does not identify the supplied package and Core asset.");
        JsonArray pins = producer["sourceInputs"]?.AsArray() ?? throw new InvalidDataException("Producer provenance has no sourceInputs.");
        var expected = Directory.EnumerateFiles(Path.Combine(language, "src", "CIS.Core"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(language, path).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Select(path => Path.GetRelativePath(language, path).Replace('\\', '/')).ToHashSet(StringComparer.Ordinal);
        expected.UnionWith(["src/CIS.Core/CIS.Core.csproj", "global.json", "Directory.Build.props", "Directory.Packages.props"]);
        var observed = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonNode? pin in pins)
        {
            string relative = (string?)pin?["path"] ?? throw new InvalidDataException("Missing producer input path.");
            Require(observed.Add(relative), "Duplicate producer input pin: " + relative);
            string original = SourceReplay.InputPath(language, relative);
            string hash = (string?)pin?["sha256"] ?? "";
            Require(hash.Length == 64 && Hash(original) == hash, "Producer input checksum mismatch: " + relative);
            originals.Add(original, hash);
        }
        Require(expected.IsSubsetOf(observed), "Producer provenance omits required Core C# or build inputs.");
        report["producerProvenance"] = new JsonObject
        {
            ["path"] = provenance,
            ["sha256"] = provenanceHash,
            ["sourceInputs"] = pins.DeepClone(),
            ["scope"] = "Validated supplied producer pins; does not independently prove the build process or transitive toolchain"
        };
        report["package"] = new JsonObject { ["path"] = package, ["sha256"] = packageHash, ["version"] = version, ["coreSha256"] = coreHash };
        Directory.CreateDirectory(feed);
        Copy(package, Path.Combine(feed, "CIS.Core." + version + ".nupkg"), packageHash);
        PinStaged(Path.Combine(feed, "CIS.Core." + version + ".nupkg"));
        string policyPath = Path.Combine(language, "eng", "runtime-dependencies.json");
        originals.TryAdd(policyPath, Hash(policyPath));
        JsonObject dependencies = Read(policyPath);
        var dependencyEvidence = new JsonArray();
        foreach (JsonNode? dependency in dependencies["packages"]!.AsArray())
        {
            string id = ((string)dependency!["id"]!).ToLowerInvariant(), dependencyVersion = (string)dependency["version"]!;
            string source = Path.Combine(dependencyCache, id, dependencyVersion, id + "." + dependencyVersion + ".nupkg");
            string hash = Hash(source), destination = Path.Combine(feed, Path.GetFileName(source));
            Copy(source, destination, hash); originals.TryAdd(source, hash); PinStaged(destination);
            dependencyEvidence.Add(new JsonObject { ["id"] = id, ["version"] = dependencyVersion, ["sha256"] = hash });
        }
        report["dependencyPolicySha256"] = Hash(policyPath); report["dependencyPackages"] = dependencyEvidence;
        Put("global.json", """{"sdk":{"version":"10.0.401","rollForward":"disable","allowPrerelease":false}}""");
        Put("Directory.Build.props", new XElement("Project", new XElement("PropertyGroup",
            new XElement("TargetFramework", "net10.0"), new XElement("ImplicitUsings", "enable"), new XElement("Nullable", "enable"),
            new XElement("TreatWarningsAsErrors", "true"), new XElement("EnableNETAnalyzers", "true"),
            new XElement("Deterministic", "true"), new XElement("RestorePackagesWithLockFile", "true"),
            new XElement("RestoreConfigFile", Path.Combine(root, "NuGet.Config")), new XElement("UseSharedCompilation", "false"))).ToString());
        foreach (string file in new[] { "Directory.Build.targets", "Directory.Packages.props", "Directory.Solution.props", "Directory.Solution.targets" }) { Put(file, "<Project />\n"); }
        Put("Directory.Build.rsp", "");
        Put("NuGet.Config", new XElement("configuration", new XElement("packageSources", new XElement("clear"),
            new XElement("add", new XAttribute("key", "local"), new XAttribute("value", feed))),
            new XElement("fallbackPackageFolders", new XElement("clear")), new XElement("auditSources", new XElement("clear"))).ToString());
        StageConsumer("country-list", "examples/healthcheck/CountryList", "CountryList.csproj", "CIS.Healthcheck.Examples.CountryList", "Microsoft.NET.Sdk.Web");
        StageConsumer("probe", "tools/CisHealthProbe", "CisHealthProbe.csproj", "CisHealthProbe", "Microsoft.NET.Sdk");
        StageConsumer("http-host", "tests/fixtures/HealthcheckHttpHost", "HealthcheckHttpHost.csproj", "CIS.Healthcheck.HttpFixture", "Microsoft.NET.Sdk");
        report["originalInputs"] = JsonSerializer.SerializeToNode(originals);
        report["stagedInputs"] = JsonSerializer.SerializeToNode(staged);
    }

    private void StageConsumer(string name, string relative, string projectName, string assemblyName, string sdk)
    {
        string source = Path.Combine(language, relative), directory = Path.Combine(root, "consumers", name);
        Require(Directory.Exists(source), "Missing actual consumer source: " + relative);
        string originalProject = Path.Combine(source, projectName); originals.TryAdd(originalProject, Hash(originalProject));
        XElement original = XElement.Load(originalProject);
        Require(original.Descendants("ProjectReference").Count() == 1 && original.Descendants("ProjectReference").Single().Attribute("Include")!.Value.EndsWith("src/CIS.Core/CIS.Core.csproj", StringComparison.Ordinal), "Expected the real consumer's single Core source reference.");
        int count = 0;
        foreach (string file in Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories))
        {
            string path = Path.GetRelativePath(source, file);
            if (path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj")) { continue; }
            string hash = Hash(file), target = Path.Combine(directory, path); originals.TryAdd(file, hash); Copy(file, target, hash); PinStaged(target); count++;
        }
        Require(count > 0, "An empty consumer does not qualify actual application behavior.");
        var project = new XElement("Project", new XAttribute("Sdk", sdk),
            new XElement("PropertyGroup", new XElement("OutputType", "Exe"), new XElement("AssemblyName", assemblyName),
                new XElement("IsPackable", "false"), new XElement("UseAppHost", "false"), new XElement("RuntimeFrameworkVersion", "10.0.12"), new XElement("RollForward", "Disable")),
            new XElement("ItemGroup", new XElement("PackageReference", new XAttribute("Include", "CIS.Core"), new XAttribute("Version", "[" + version + "]"))));
        if (sdk != "Microsoft.NET.Sdk.Web") { project.Add(new XElement("ItemGroup", new XElement("FrameworkReference", new XAttribute("Include", "Microsoft.AspNetCore.App")))); }
        string projectPath = Path.Combine(directory, projectName);
        Write(projectPath, project.ToString()); PinStaged(projectPath);
        string observation = Path.Combine(directory, "PackageOriginEvidence.cs");
        Write(observation, OriginProgram.Replace("EXPECTED_CORE_HASH", coreHash, StringComparison.Ordinal)); PinStaged(observation);
        consumers.Add(name, new Consumer(name, source, projectPath, assemblyName, Path.Combine(directory, "bin", "Release", "net10.0")));
    }

    private async Task BuildConsumersAsync()
    {
        foreach (Consumer consumer in consumers.Values)
        {
            ProcessObservation restore = await RunChildAsync(consumer.Name + "-restore", ["restore", consumer.Project, "--configfile", Path.Combine(root, "NuGet.Config"), "--no-http-cache"], root).ConfigureAwait(false);
            Require(restore.ExitCode == 0 && !restore.TimedOut, consumer.Name + " exact-package restore failed.");
            ProcessObservation build = await RunChildAsync(consumer.Name + "-build", ["build", consumer.Project, "-c", "Release", "--no-restore", "-maxcpucount:1", "-nologo"], root).ConfigureAwait(false);
            Require(build.ExitCode == 0 && !build.TimedOut, consumer.Name + " exact-package build failed.");
            JsonObject assets = Read(Path.Combine(Path.GetDirectoryName(consumer.Project)!, "obj", "project.assets.json"));
            string[] folders = assets["packageFolders"]!.AsObject().Select(row => row.Key).ToArray();
            string[] sources = assets["project"]!["restore"]!["sources"]!.AsObject().Select(row => row.Key).ToArray();
            Require(folders.Length == 1 && SamePath(folders[0], Path.Combine(root, "packages")) && sources.Length == 1 && SamePath(sources[0], feed), "Consumer restore escaped the isolated feed/cache.");
            Require(assets["libraries"]!.AsObject().All(row => (string?)row.Value?["type"] == "package"), "A source project leaked into the package consumer graph.");
            Require(assets["libraries"]!["CIS.Core/" + version] is not null, "Consumer resolved a different Core package version.");
            string restoredPackage = Path.Combine(root, "packages", "cis.core", version.ToLowerInvariant(), "cis.core." + version.ToLowerInvariant() + ".nupkg");
            Require(Hash(restoredPackage) == packageHash, "NuGet cache contains different Core package bytes.");
            string deployedCore = Path.Combine(consumer.Output, "CIS.Core.dll");
            Require(Hash(deployedCore) == coreHash, "Consumer output contains a different Core DLL.");
            JsonObject deps = Read(Path.Combine(consumer.Output, consumer.Assembly + ".deps.json"));
            Require(deps["libraries"]!["CIS.Core/" + version] is not null, "Runtime dependency identity differs from exact Core version.");
            Require(!deps["libraries"]!.AsObject().Any(row => row.Key.StartsWith("CIS.TypeManager.Build/", StringComparison.Ordinal) || row.Key.StartsWith("Microsoft.Build", StringComparison.Ordinal))
                && !File.Exists(Path.Combine(consumer.Output, "CIS.TypeManager.Build.dll")), "Build tooling leaked into runtime output.");
            JsonNode runtime = Read(Path.Combine(consumer.Output, consumer.Assembly + ".runtimeconfig.json"))["runtimeOptions"]!;
            JsonArray frameworks = runtime["frameworks"]?.AsArray() ?? new JsonArray(runtime["framework"]!.DeepClone());
            Require((string?)runtime["rollForward"] == "Disable" && frameworks.Count == 2
                && frameworks.All(framework => (string?)framework!["version"] == "10.0.12")
                && frameworks.Select(framework => (string)framework!["name"]!).ToHashSet(StringComparer.Ordinal).SetEquals(["Microsoft.NETCore.App", "Microsoft.AspNetCore.App"]), "Consumer runtime/framework pins differ.");
            Passed(consumer.Name + "-exact-package-build", new JsonObject
            {
                ["projectSha256"] = Hash(consumer.Project),
                ["assetsSha256"] = Hash(Path.Combine(Path.GetDirectoryName(consumer.Project)!, "obj", "project.assets.json")),
                ["coreSha256"] = Hash(deployedCore),
                ["consumerAssemblySha256"] = Hash(Path.Combine(consumer.Output, consumer.Assembly + ".dll")),
                ["runtimeConfig"] = runtime.DeepClone()
            });
        }
    }

    private void Put(string relative, string text) { string path = Path.Combine(root, relative); Write(path, text); PinStaged(path); }
    private void PinStaged(string path) => staged.Add(path, Hash(path));
    private void Save() => WriteJson(Path.Combine(root, "results.json"), report);
    private void Passed(string name, JsonObject? details = null)
    {
        JsonObject row = details ?? new(); row["case"] = name; row["passed"] = true; report["results"]!.AsArray().Add(row); Save();
    }
    private static bool SamePath(string left, string right) => string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private const string OriginProgram = """
        using System.Reflection;
        using System.Runtime.CompilerServices;
        using System.Runtime.InteropServices;
        using System.Security.Cryptography;
        using System.Text.Json;
        internal static class PackageOriginEvidence
        {
            [ModuleInitializer]
            internal static void Observe()
            {
                Assembly core = typeof(CIS.Healthcheck.HealthcheckService).Assembly;
                string path = Path.GetFullPath(core.Location);
                string expectedPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "CIS.Core.dll"));
                string hash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
                bool exact = string.Equals(path, expectedPath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
                    && hash == "EXPECTED_CORE_HASH";
                Console.Error.WriteLine("CIS_HEALTHCHECK_PACKAGE_ORIGIN " + JsonSerializer.Serialize(new
                {
                    exact, path, sha256 = hash, assembly = core.FullName,
                    informationalVersion = core.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                    runtime = RuntimeInformation.FrameworkDescription,
                    architecture = RuntimeInformation.ProcessArchitecture.ToString()
                }));
                if (!exact) { throw new InvalidOperationException("The process loaded a different Core assembly."); }
            }
        }
        """;
}
