using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using static CIS.Verification.TypeManagerVerificationInputs;

namespace CIS.Verification;

internal sealed class TypeManagerPackageVerification
{
    private readonly TypeManagerVerificationInputs input;
    private readonly string root, language, feed, native, cross;
    private readonly JsonObject report;
    private readonly IReadOnlyDictionary<string, string?> environment;
    private readonly Dictionary<string, string> variantHashes = new(StringComparer.Ordinal);
    private string version = "", coreHash = "", helperHash = "";
    private TypeManagerPackageVerification(TypeManagerVerificationInputs input)
    {
        this.input = input; root = input.Output; language = input.Language; feed = Path.Combine(root, "feed");
        native = OperatingSystem.IsWindows() ? "win-x64" : "linux-x64"; cross = native == "win-x64" ? "linux-x64" : "win-x64";
        environment = HermeticEnvironment(root);
        report = new JsonObject
        {
            ["complete"] = false,
            ["nativeRid"] = native,
            ["packageSha256"] = Hash(input.PathOf("package")),
            ["verifierSha256"] = Hash(typeof(TypeManagerPackageVerification).Assembly.Location),
            ["results"] = new JsonArray()
        };
    }
    internal static async Task<int> RunAsync(string[] arguments)
    {
        var input = new TypeManagerVerificationInputs(arguments, ["repo-root", "package", "dependency-cache", "rid-package", "output-root"], []);
        Require(RuntimeInformation.ProcessArchitecture == Architecture.X64 && (OperatingSystem.IsWindows() || OperatingSystem.IsLinux()), "Supported package lanes are Windows x64 and Linux x64.");
        input.EmptyOutput(); return await new TypeManagerPackageVerification(input).Verify();
    }
    private void Save() => WriteJson(Path.Combine(root, "results.json"), report);
    private void Put(string relative, string text) => Write(Path.Combine(root, relative), text);
    private void Passed(string name, JsonObject? details = null)
    { JsonObject row = details ?? new(); row["case"] = name; row["passed"] = true; report["results"]!.AsArray().Add(row); Save(); }
    private async Task<string> Run(string name, string[] arguments, string? expectedError = null) =>
        (await input.Run(name, arguments, expectedError: expectedError, environment: environment)).StandardOutput.Trim();
    private Task<string> Property(string name, string key, params string[] extra) =>
        Run(name + "-property-" + key, ["msbuild", name + "/Host.csproj", "-p:Configuration=Release", "-getProperty:" + key, "-nologo", .. extra]);
    private static string EntryHash(ZipArchive archive, string name)
    { using Stream data = (archive.GetEntry(name) ?? throw new InvalidDataException("Missing package entry: " + name)).Open(); return Convert.ToHexStringLower(SHA256.HashData(data)); }

    private void Setup()
    {
        using (ZipArchive archive = ZipFile.OpenRead(input.PathOf("package")))
        {
            using Stream spec = (archive.GetEntry("CIS.Core.nuspec") ?? throw new InvalidDataException("Missing Core nuspec.")).Open();
            version = XDocument.Load(spec).Descendants().Single(e => e.Name.LocalName == "metadata").Elements().Single(e => e.Name.LocalName == "version").Value;
            coreHash = EntryHash(archive, "lib/net10.0/CIS.Core.dll"); helperHash = EntryHash(archive, "tools/net10.0/CIS.TypeManager.Build.dll");
        }
        using (ZipArchive archive = ZipFile.OpenRead(input.PathOf("rid-package")))
        { foreach (string rid in new[] { "win-x64", "linux-x64" }) { variantHashes[rid] = EntryHash(archive, "runtimes/" + rid + "/lib/net10.0/CIS.TypeManager.RidAsset.dll"); } }
        report["packageVersion"] = version; report["coreSha256"] = coreHash; report["helperSha256"] = helperHash; report["ridPackageSha256"] = Hash(input.PathOf("rid-package"));
        Copy(Path.Combine(language, "global.json"), Path.Combine(root, "global.json"));
        foreach (string file in new[] { "Directory.Build.targets", "Directory.Packages.props", "Directory.Solution.props", "Directory.Solution.targets" }) { Put(file, "<Project />"); }
        Put("Directory.Build.rsp", "");
        Put("Directory.Build.props", new XElement("Project", new XElement("PropertyGroup", new XElement("TargetFramework", "net10.0"), new XElement("ImplicitUsings", "enable"), new XElement("Nullable", "enable"),
            new XElement("RestorePackagesWithLockFile", "true"), new XElement("BeforeMicrosoftNETSdkTargets", Path.Combine(root, "BeforeExisting.targets")), new XElement("RestoreConfigFile", Path.Combine(root, "NuGet.Config")))).ToString());
        Put("BeforeExisting.targets", "<Project><PropertyGroup><ExistingHookSeen>true</ExistingHookSeen></PropertyGroup></Project>");
        report["fixtureInputs"] = new JsonObject();
        foreach (string fixture in new[] { "Contracts", "Transitive", "Unused" })
        {
            foreach (string path in Directory.GetFiles(Path.Combine(language, "tests", "fixtures", "TypeManager", fixture)).Where(p => Path.GetExtension(p) is ".cs" or ".csproj" or ".resx"))
            { string relative = fixture + "/" + Path.GetFileName(path); Copy(path, Path.Combine(root, "fixtures", relative)); report["fixtureInputs"]![relative] = Hash(path); }
        }
        string unused = Path.Combine(root, "fixtures", "Unused", "Unused.csproj");
        Write(unused, File.ReadAllText(unused).Replace("</Project>", "<ItemGroup><PackageReference Include=\"CIS.Core\" Version=\"[" + version + "]\" /></ItemGroup></Project>", StringComparison.Ordinal));
        Directory.CreateDirectory(feed); Copy(input.PathOf("package"), Path.Combine(feed, "CIS.Core." + version + ".nupkg"));
        Copy(input.PathOf("rid-package"), Path.Combine(feed, "CIS.TypeManager.RidFixture.1.0.0.nupkg"));
        string policy = Path.Combine(language, "eng", "runtime-dependencies.json"); report["runtimeDependencyPolicySha256"] = Hash(policy); report["dependencyPackages"] = new JsonObject();
        foreach (JsonNode? dependency in Read(policy)["packages"]!.AsArray())
        {
            string name = ((string)dependency!["id"]!).ToLowerInvariant(), depVersion = (string)dependency["version"]!;
            string path = Path.Combine(input.PathOf("dependency-cache"), name, depVersion, name + "." + depVersion + ".nupkg");
            Copy(path, Path.Combine(feed, Path.GetFileName(path))); report["dependencyPackages"]![Path.GetFileName(path)] = Hash(path);
        }
        Put("NuGet.Config", new XElement("configuration", new XElement("packageSources", new XElement("clear"), new XElement("add", new XAttribute("key", "local"), new XAttribute("value", feed))),
            new XElement("fallbackPackageFolders", new XElement("clear")), new XElement("auditSources", new XElement("clear"))).ToString());
    }
    private const string HostProgram = """
        using CIS.TypeManagement;
        using CIS.TypeManagement.Fixtures.Contracts;
        using System.Text.Json;
        if (AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "CIS.TypeManager.Fixture.Unused"))
            throw new InvalidOperationException("The unused discovery library was preloaded.");
        var manager = await TypeManager.CreateAsync(new TypeManagerOptions(typeof(Program).Assembly) { PrecomputedContracts = [typeof(IDiscoveryMarker)] });
        var withoutUnused = args.Contains("--without-unused");
        var implementation = manager.GetImplementations<IDiscoveryMarker>().SingleOrDefault();
        if (implementation?.FullName != (withoutUnused ? null : "CIS.TypeManagement.Fixtures.Unused.DiscoveredSettings"))
            throw new InvalidOperationException("Incorrect discovered implementation.");
        if (withoutUnused && AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name is "CIS.TypeManager.Fixture.Unused" or "CIS.TypeManager.Fixture.Transitive"))
            throw new InvalidOperationException("A removed dependency was loaded from stale output.");
        var variant = manager.Types.Single(t => t.FullName == "InventoryFixture.Variant");
        Console.WriteLine(JsonSerializer.Serialize(new { selected = manager.Assemblies.Count, implementation = implementation?.FullName, variant = (string)variant.GetProperty("Value")!.GetValue(null)! }));
        """;
    private void Host(string name, string sdk = "Microsoft.NET.Sdk", bool central = false, bool ordinary = false, bool duplicate = false)
    {
        var project = new XElement("Project", new XAttribute("Sdk", sdk));
        if (!ordinary) { project.Add(new XElement("Sdk", new XAttribute("Name", "CIS.Core"), new XAttribute("Version", version))); }
        project.Add(new XElement("PropertyGroup", new XElement("OutputType", "Exe"), new XElement("AssemblyName", "TypeManagerPackageHost"), new XElement("UseAppHost", "false"), new XElement("CisTypeManagerEnabled", "true")));
        var fixture = new XElement("PackageReference", new XAttribute("Include", "CIS.TypeManager.RidFixture")); if (!central) { fixture.Add(new XAttribute("Version", "1.0.0")); }
        var items = new XElement("ItemGroup", new XElement("ProjectReference", new XAttribute("Include", "../fixtures/Unused/Unused.csproj")), new XElement("ProjectReference", new XAttribute("Include", "../fixtures/Contracts/Contracts.csproj")), fixture);
        if (ordinary || duplicate) { items.Add(new XElement("PackageReference", new XAttribute("Include", "CIS.Core"), new XAttribute("Version", version))); }
        project.Add(items); Put(name + "/Host.csproj", project.ToString()); Put(name + "/Program.cs", HostProgram);
        if (central) { Put(name + "/Directory.Packages.props", """<Project><PropertyGroup><ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally></PropertyGroup><ItemGroup><PackageVersion Include="CIS.TypeManager.RidFixture" Version="1.0.0" /></ItemGroup></Project>"""); }
    }

    private async Task Validate(string name, string mode, string directory, string rid, bool execute = true, bool withoutUnused = false, string hostName = "TypeManagerPackageHost", bool record = true)
    {
        string manifestPath = Path.Combine(directory, hostName + ".types.json"); Require(File.Exists(manifestPath), name + "/" + mode + " did not produce a current manifest");
        JsonObject manifest = Read(manifestPath); JsonArray members = manifest["assemblies"]!.AsArray();
        var entries = members.ToDictionary(m => ((string)m!["assemblyName"]!).Split(',')[0], m => m!, StringComparer.Ordinal);
        var expected = new HashSet<string>([hostName, "CIS.Core", "Microsoft.Extensions.Logging.Abstractions", "Microsoft.Extensions.DependencyInjection.Abstractions", "CIS.TypeManager.Fixture.Contracts", "CIS.TypeManager.Fixture.Transitive", "CIS.TypeManager.Fixture.Unused", "CIS.TypeManager.RidAsset"], StringComparer.Ordinal);
        if (name == "web") { expected.ExceptWith(["Microsoft.Extensions.Logging.Abstractions", "Microsoft.Extensions.DependencyInjection.Abstractions"]); }
        if (withoutUnused) { expected.ExceptWith(["CIS.TypeManager.Fixture.Unused", "CIS.TypeManager.Fixture.Transitive"]); }
        Require(entries.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(expected) && members.Count == expected.Count && manifest["schemaVersion"]!.GetValue<int>() == 1, "Incorrect package consumer inventory: " + string.Join(", ", entries.Keys));
        Require((string?)manifest["hostAssembly"] == (string?)entries[hostName]["assemblyName"], "Incorrect manifest host identity");
        Require(Hash(Path.Combine(directory, "CIS.Core.dll")) == coreHash, "Consumer is not using the exact packed Core DLL");
        Require(Hash(Path.Combine(root, "packages", "cis.core", version.ToLowerInvariant(), "tools", "net10.0", "CIS.TypeManager.Build.dll")) == helperHash, "Consumer is not using the exact packed inventory helper");
        Require(Hash(Path.Combine(directory, (string)entries["CIS.TypeManager.RidAsset"]["relativePath"]!)) == variantHashes[rid], "Wrong RID-selected managed asset");
        Require(File.Exists(Path.Combine(directory, "cistm-native.dll")), "Native exclusion fixture was not deployed");
        if (!withoutUnused) { Require(File.Exists(Path.Combine(directory, "fr", "CIS.TypeManager.Fixture.Unused.resources.dll")), "Satellite exclusion fixture was not deployed"); }
        JsonObject deps = Read(Path.Combine(directory, hostName + ".deps.json"));
        Require(!deps["libraries"]!.AsObject().Any(p => p.Key.Contains("CIS.TypeManager.Build", StringComparison.Ordinal) || p.Key.Contains("Microsoft.Build", StringComparison.Ordinal)), "Build tooling leaked into runtime dependencies");
        Require(!File.Exists(Path.Combine(directory, "CIS.TypeManager.Build.dll")), "Build helper leaked into deployment");
        JsonNode? runtime = null;
        if (execute)
        {
            runtime = JsonNode.Parse(await Run(name + "-" + mode + "-run", [Path.Combine(directory, hostName + ".dll"), .. withoutUnused ? new[] { "--without-unused" } : Array.Empty<string>()]));
            Require(JsonNode.DeepEquals(new JsonObject { ["selected"] = expected.Count, ["implementation"] = withoutUnused ? null : "CIS.TypeManagement.Fixtures.Unused.DiscoveredSettings", ["variant"] = rid }, runtime), "Unexpected prepared runtime catalog");
        }
        Copy(manifestPath, Path.Combine(root, name + "-" + mode + ".types.json"));
        JsonObject assets = Read(Path.Combine(root, name, "obj", "project.assets.json"));
        string[] folders = assets["packageFolders"]!.AsObject().Select(p => Path.GetFullPath(p.Key)).ToArray(), sources = assets["project"]!["restore"]!["sources"]!.AsObject().Select(p => Path.GetFullPath(p.Key)).ToArray();
        Require(folders.Length == 1 && SamePath(folders[0], Path.Combine(root, "packages")) && sources.Length == 1 && SamePath(sources[0], feed), "Consumer restore escaped its private package cache or feed");
        if (record) { Passed(name + "-" + mode, new JsonObject { ["selected"] = new JsonArray(entries.Keys.Order(StringComparer.Ordinal).Select(n => (JsonNode)JsonValue.Create(n)!).ToArray()), ["rid"] = rid, ["runtime"] = runtime, ["manifestSha256"] = Hash(manifestPath) }); }
    }
    private static bool SamePath(string left, string right) => string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static void DeletePrevious(string path) { try { File.Delete(path); } catch (DirectoryNotFoundException) { } }
    private static (string Hash, long Ticks) Stamp(string path) => (Hash(path), File.GetLastWriteTimeUtc(path).Ticks);
    private async Task<string> Published(string name) => Path.GetFullPath(Path.Combine(root, name, (await Property(name, "PublishDir")).Replace('\\', Path.DirectorySeparatorChar)));
    private async Task Lifecycle(string name)
    {
        string project = Path.Combine(root, name, "Host.csproj"); byte[] original = File.ReadAllBytes(project);
        string directory = await Property(name, "TargetDir"), manifest = Path.Combine(directory, "TypeManagerPackageHost.types.json");
        byte[] initialManifest = File.ReadAllBytes(manifest);
        string[] staleNames = ["CIS.TypeManager.Fixture.Unused.dll", "CIS.TypeManager.Fixture.Transitive.dll", "fr/CIS.TypeManager.Fixture.Unused.resources.dll"];
        var stale = staleNames.ToDictionary(n => n, n => File.ReadAllBytes(Path.Combine(directory, n)), StringComparer.Ordinal);
        XElement parsed = XElement.Parse(System.Text.Encoding.UTF8.GetString(original));
        XElement[] references = parsed.Elements("ItemGroup").Elements("ProjectReference").Where(e => (string?)e.Attribute("Include") == "../fixtures/Unused/Unused.csproj").ToArray();
        Require(references.Length == 1, "The lifecycle control must remove exactly one project reference"); references[0].Remove();
        var inject = new XElement("Target", new XAttribute("Name", "PlantLifecycleStaleAssets"), new XAttribute("BeforeTargets", "CisWriteBuildTypeInventory;CisWritePublishTypeInventory"), new XAttribute("Condition", "'$(LifecycleStaleAssets)' == 'true'"));
        foreach ((string filename, byte[] content) in stale)
        {
            string seed = Path.Combine(root, "stale-seeds", name, filename); Directory.CreateDirectory(Path.GetDirectoryName(seed)!); File.WriteAllBytes(seed, content);
            string destination = "$(LifecycleDestination)/" + filename;
            inject.Add(new XElement("Copy", new XAttribute("SourceFiles", seed), new XAttribute("DestinationFiles", destination)), new XElement("Error", new XAttribute("Condition", "!Exists('" + destination + "')"), new XAttribute("Text", "Stale fixture was not planted")));
        }
        inject.Add(new XElement("Message", new XAttribute("Importance", "High"), new XAttribute("Text", "LIFECYCLE_STALE_FILES_PRESENT"))); parsed.Add(inject);
        try
        {
            Write(project, parsed.ToString());
            string output = await Run(name + "-remove-reference", ["build", project, "-c", "Release", "-m:1", "--nologo", "-p:LifecycleStaleAssets=true", "-p:LifecycleDestination=" + directory]);
            Require(output.Contains("LIFECYCLE_STALE_FILES_PRESENT", StringComparison.Ordinal), "The build stale-output generation probe did not execute");
            Plant(directory); await Validate(name, "removed-reference-build", directory, native, withoutUnused: true);
            byte[] current = File.ReadAllBytes(manifest); File.WriteAllBytes(manifest, initialManifest);
            try
            {
                InvalidDataException? rejection = null;
                try { await Validate(name, "stale-manifest-negative-control", directory, native, withoutUnused: true, record: false); } catch (InvalidDataException error) { rejection = error; }
                Require(rejection is not null && rejection.Message.StartsWith("Incorrect package consumer inventory:", StringComparison.Ordinal), "The selection assertion accepted the old manifest after reference removal");
            }
            finally { File.WriteAllBytes(manifest, current); }
            Passed(name + "-selection-assertion-rejects-stale-manifest");
            string published = await Published(name);
            output = await Run(name + "-removed-reference-publish", ["publish", project, "-c", "Release", "--no-build", "--no-restore", "-m:1", "--nologo", "-p:LifecycleStaleAssets=true", "-p:LifecycleDestination=" + published]);
            Require(output.Contains("LIFECYCLE_STALE_FILES_PRESENT", StringComparison.Ordinal), "The publish stale-output generation probe did not execute");
            Plant(published); await Validate(name, "removed-reference-publish", published, native, withoutUnused: true);
            File.WriteAllBytes(project, original); await Run(name + "-readd-reference", ["build", project, "-c", "Release", "-m:1", "--nologo"]);
            await Validate(name, "readded-reference-build", directory, native);
            const string renamed = "TypeManagerRenamedHost"; parsed = XElement.Parse(System.Text.Encoding.UTF8.GetString(original)); parsed.Element("PropertyGroup")!.Element("AssemblyName")!.Value = renamed;
            Write(project, parsed.ToString()); await Run(name + "-rename-host", ["build", project, "-c", "Release", "-m:1", "--nologo"]);
            await Validate(name, "renamed-host-build", directory, native, hostName: renamed);
            await Run(name + "-renamed-host-publish", ["publish", project, "-c", "Release", "--no-build", "--no-restore", "-m:1", "--nologo"]);
            await Validate(name, "renamed-host-publish", published, native, hostName: renamed);
            string renamedManifest = Path.Combine(directory, renamed + ".types.json");
            Require(Directory.GetFiles(Path.Combine(root, name, "obj"), "*.FileListAbsolute.txt", SearchOption.AllDirectories).Any(p => File.ReadAllText(p).Contains(renamedManifest, StringComparison.Ordinal)), "The generated manifest is not registered for clean");
            await Run(name + "-clean", ["clean", project, "-c", "Release", "-m:1", "--nologo"]);
            Require(!File.Exists(renamedManifest) && !File.Exists(manifest), "Clean left a generated build manifest behind");
            Passed(name + "-clean-removes-registered-manifest", new JsonObject { ["obsoletePublishedManifestRetained"] = File.Exists(Path.Combine(published, Path.GetFileName(manifest))) });
        }
        finally { File.WriteAllBytes(project, original); }
        Require(!File.Exists(manifest), "Rebuild must start without its output manifest");
        await Run(name + "-rebuild-after-clean", ["build", project, "-c", "Release", "-m:1", "--nologo"]); await Validate(name, "rebuild-after-clean", directory, native);
        string helper = Path.Combine(root, "packages", "cis.core", version.ToLowerInvariant(), "tools", "net10.0", "CIS.TypeManager.Build.dll");
        byte[] helperBytes = File.ReadAllBytes(helper); var before = Stamp(manifest); File.Delete(helper);
        try
        {
            await Run(name + "-design-time", ["msbuild", project, "-t:Compile", "-p:Configuration=Release", "-p:DesignTimeBuild=true", "-p:SkipCompilerExecution=true", "-p:ProvideCommandLineArgs=true", "-p:BuildingProject=false", "-nologo"]);
            Require(Stamp(manifest) == before, "Design-time compile changed the manifest"); Passed(name + "-design-time-evaluation-and-compile-without-helper");
            foreach (string target in new[] { "CisWriteBuildTypeInventory", "CisWritePublishTypeInventory" })
            {
                await Run(name + "-design-time-" + target, ["msbuild", project, "-t:" + target, "-p:Configuration=Release", "-p:DesignTimeBuild=true", "-nologo"]);
                Require(Stamp(manifest) == before, "Explicit design-time inventory target changed the manifest");
                await Run(name + "-missing-helper-control-" + target, ["msbuild", project, "-t:" + target, "-p:Configuration=Release", "-nologo"], "CISTM005: The Type Manager build helper is missing");
                Passed(name + "-design-time-skip-and-missing-helper-control-" + target);
            }
        }
        finally { File.WriteAllBytes(helper, helperBytes); }
        void Plant(string destination) { foreach ((string filename, byte[] bytes) in stale) { string path = Path.Combine(destination, filename); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes); } }
    }

    private async Task<int> Verify()
    {
        Save();
        try
        {
            Setup(); report["sdk"] = await Run("sdk-version", ["--version"]);
            Require((string?)report["sdk"] == (string?)Read(Path.Combine(root, "global.json"))["sdk"]!["version"], "Consumer SDK differs from the pinned version");
            Host("console"); Host("web", "Microsoft.NET.Sdk.Web");
            Put("Consumers.slnx", """<Solution><Folder Name="/console/"><Project Path="console/Host.csproj" /></Folder><Folder Name="/web/"><Project Path="web/Host.csproj" /></Folder><Folder Name="/fixtures/"><Project Path="fixtures/Contracts/Contracts.csproj" /><Project Path="fixtures/Transitive/Transitive.csproj" /><Project Path="fixtures/Unused/Unused.csproj" /></Folder></Solution>""");
            await Run("parallel-clean-restore-build", ["build", "Consumers.slnx", "-c", "Release", "-m:2", "--nologo"]);
            foreach (string name in new[] { "console", "web" })
            { Require(await Property(name, "NETCoreSdkRuntimeIdentifier") == native, "SDK native RID differs from the qualification lane"); await Validate(name, "parallel-clean-build", await Property(name, "TargetDir"), native); }
            foreach (string name in new[] { "console", "web" })
            {
                string project = name + "/Host.csproj"; DeletePrevious(Path.Combine(await Property(name, "TargetDir"), "TypeManagerPackageHost.types.json"));
                await Run(name + "-build", ["build", project, "-c", "Release", "-m:1", "--nologo"]);
                string directory = await Property(name, "TargetDir"); await Validate(name, "build", directory, native);
                Require(await Property(name, "ExistingHookSeen") == "true", "SDK import replaced an existing early SDK hook"); Passed(name + "-preserves-existing-sdk-hook");
                string manifest = Path.Combine(directory, "TypeManagerPackageHost.types.json"), assets = Path.Combine(root, name, "obj", "project.assets.json");
                var before = Stamp(manifest); var beforeAssets = Stamp(assets);
                await Run(name + "-no-restore", ["build", project, "-c", "Release", "--no-restore", "-m:1", "--nologo"]);
                Require(before == Stamp(manifest) && beforeAssets == Stamp(assets), "Incremental no-restore build rewrote unchanged assets or manifest"); Passed(name + "-incremental-no-restore-unchanged");
                string published = await Published(name); DeletePrevious(Path.Combine(published, Path.GetFileName(manifest)));
                await Run(name + "-publish-no-build", ["publish", project, "-c", "Release", "--no-build", "--no-restore", "-m:1", "--nologo"]);
                await Validate(name, "publish-no-build", published, native);
                string crossOutput = Path.Combine(root, "published", name, cross);
                await Run(name + "-publish-cross", ["publish", project, "-c", "Release", "-r", cross, "-o", crossOutput, "-m:1", "--nologo"]);
                await Validate(name, "publish-cross", crossOutput, cross, execute: false);
                await Run(name + "-cross-then-native-no-restore", ["build", project, "-c", "Release", "--no-restore", "-m:1", "--nologo"], "NETSDK1047"); Passed(name + "-cross-rid-requires-native-restore");
                await Run(name + "-restore-native", ["restore", project, "--nologo"]);
            }
            foreach (string name in new[] { "console", "web" }) { DeletePrevious(Path.Combine(await Property(name, "TargetDir"), "TypeManagerPackageHost.types.json")); }
            await Run("parallel-no-restore", ["build", "Consumers.slnx", "-c", "Release", "--no-restore", "-m:2", "--nologo"]);
            foreach (string name in new[] { "console", "web" }) { await Validate(name, "parallel-no-restore", await Property(name, "TargetDir"), native); }
            string offline = Path.Combine(root, "offline-feed");
            Require(SamePath(Path.GetDirectoryName(Path.GetFullPath(feed))!, root) && SamePath(Path.GetDirectoryName(Path.GetFullPath(offline))!, root) && (File.GetAttributes(feed) & FileAttributes.ReparsePoint) == 0, "Offline feed move escaped its workspace");
            Directory.Move(feed, offline);
            try
            {
                string[] locks = new[] { "console", "web", "fixtures/Unused", "fixtures/Transitive", "fixtures/Contracts" }.Select(name => Path.Combine(root, name, "packages.lock.json")).ToArray();
                Require(locks.All(File.Exists), "Offline qualification requires existing lock files"); string[] hashes = locks.Select(Hash).ToArray();
                await Run("offline-no-restore", ["build", "Consumers.slnx", "-c", "Release", "--no-restore", "-m:2", "--nologo"]);
                await Run("offline-locked-restore", ["restore", "Consumers.slnx", "--force", "--locked-mode", "--nologo"]);
                Require(hashes.SequenceEqual(locks.Select(Hash)), "Offline locked restore changed a lock file");
            }
            finally
            {
                Require(SamePath(Path.GetDirectoryName(Path.GetFullPath(offline))!, root) && SamePath(Path.GetDirectoryName(Path.GetFullPath(feed))!, root) && (File.GetAttributes(offline) & FileAttributes.ReparsePoint) == 0, "Offline feed restoration escaped its workspace");
                Directory.Move(offline, feed);
            }
            Passed("offline-no-restore-and-locked-restore");
            foreach (string name in new[] { "console", "web" }) { await Lifecycle(name); }
            Host("central", central: true); await Run("central-build", ["build", "central/Host.csproj", "-c", "Release", "-m:1", "--nologo"]);
            await Validate("central", "build", await Property("central", "TargetDir"), native);
            string centralPath = Path.Combine(root, "central", "Directory.Packages.props"), centralText = File.ReadAllText(centralPath);
            Write(centralPath, centralText.Replace("</ItemGroup>", "<PackageVersion Include=\"CIS.Core\" Version=\"" + version + "\" /></ItemGroup>", StringComparison.Ordinal));
            JsonObject evaluated = JsonNode.Parse(await Run("central-exact-version", ["msbuild", "central/Host.csproj", "-getItem:PackageVersion", "-nologo"]))!.AsObject();
            string[] versions = evaluated["Items"]!["PackageVersion"]!.AsArray().Where(n => string.Equals((string?)n!["Identity"], "cis.core", StringComparison.OrdinalIgnoreCase)).Select(n => (string)n!["Version"]!).ToArray();
            Require(versions.SequenceEqual(new[] { "[" + version + "]" }), "Matching central version is not constrained to the exact SDK runtime");
            await Run("central-matching-restore", ["restore", "central/Host.csproj", "--nologo"]); Passed("central-matching-version-pinned-exactly");
            Write(centralPath, centralText.Replace("</ItemGroup>", "<PackageVersion Include=\"CIS.Core\" Version=\"99.0.0\" /></ItemGroup>", StringComparison.Ordinal));
            await Run("central-version-mismatch", ["restore", "central/Host.csproj", "--nologo"], "CISTM006: The central CIS.Core package version"); Passed("central-version-mismatch-rejected");
            Host("duplicate", duplicate: true); await Run("duplicate-runtime-reference", ["restore", "duplicate/Host.csproj", "--nologo"], "CISTM006: The CIS.Core SDK supplies"); Passed("duplicate-runtime-reference-rejected");
            Host("ordinary", ordinary: true); await Run("ordinary-package-reference", ["build", "ordinary/Host.csproj", "-c", "Release", "-m:1", "--nologo"], "CISTM007: Packaged Type Manager requires"); Passed("ordinary-reference-needs-sdk");
            foreach (string setting in new[] { "PublishSingleFile=true", "PublishTrimmed=true", "PublishAot=true", "SelfContained=true", "CopyLocalLockFileAssemblies=false" })
            {
                foreach (string mode in new[] { "build", "publish" })
                {
                    string destination = Path.Combine(root, "rejected", setting.Split('=')[0], mode);
                    var arguments = new List<string> { mode, "console/Host.csproj", "-c", "Release", "--no-restore" };
                    if (mode == "publish") { arguments.AddRange(["--no-build", "-o", destination]); }
                    if (setting == "SelfContained=true") { arguments.Add("-p:UseAppHost=true"); }
                    arguments.AddRange(["-p:" + setting, "-m:1", "--nologo"]);
                    await Run("reject-" + mode + "-" + setting.Split('=')[0], arguments.ToArray(), "CISTM003: Type Manager requires");
                    Require(!Directory.Exists(destination) || !Directory.EnumerateFileSystemEntries(destination).Any(), "Unsupported profile wrote publish files"); Passed("reject-" + mode + "-" + setting);
                }
            }
            await Run("reject-sdk-version", ["build", "console/Host.csproj", "--no-restore", "-p:NETCoreSdkVersion=10.0.999", "--nologo"], "CISTM009: This Type Manager package requires"); Passed("reject-unqualified-sdk");
            Host("replaced-hook"); string hook = Path.Combine(root, "replaced-hook", "Host.csproj");
            Write(hook, File.ReadAllText(hook).Replace("</PropertyGroup>", "<BeforeMicrosoftNETSdkTargets>../BeforeExisting.targets</BeforeMicrosoftNETSdkTargets></PropertyGroup>", StringComparison.Ordinal));
            await Run("reject-replaced-hook", ["restore", "replaced-hook/Host.csproj", "--nologo"], "CISTM009: The CIS.Core early SDK hook was replaced"); Passed("reject-replaced-sdk-hook");
            await Run("disabled-build", ["build", "console/Host.csproj", "-c", "Release", "-p:CisTypeManagerEnabled=false", "-m:1", "--nologo"]);
            string disabled = await Property("console", "TargetDir", "-p:CisTypeManagerEnabled=false");
            Require(!File.Exists(Path.Combine(disabled, "TypeManagerPackageHost.types.json")) && string.IsNullOrEmpty(await Property("console", "RuntimeIdentifier", "-p:CisTypeManagerEnabled=false")), "Disabled integration changed the portable host output"); Passed("disabled-integration-preserves-portable-build");
            Require(Hash(input.PathOf("package")) == (string?)report["packageSha256"] && Hash(input.PathOf("rid-package")) == (string?)report["ridPackageSha256"], "Package inputs changed during verification");
            report["complete"] = true; Save(); return 0;
        }
        catch (Exception error) { report["error"] = error.Message; throw; }
        finally { Save(); }
    }
}
