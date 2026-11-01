using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CIS.TestSupport;

namespace CIS.Verification;

internal sealed partial class DependencyChecks
{
    private readonly string language, sdk;
    private readonly JsonObject policy, runtime, development;
    private readonly Dictionary<string, string> runtimeAllowed, developmentAllowed, testAllowed;
    private readonly TextWriter output;
    private readonly PreparedEvaluation? prepared;
    private EvaluationDeadline? deadline;
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private DependencyChecks(string language, TextWriter? output = null, PreparedEvaluation? prepared = null)
    {
        this.language = Path.GetFullPath(language);
        this.output = output ?? Console.Out;
        this.prepared = prepared;
        policy = Read(Path.Combine(language, "eng/project-boundaries.json"));
        runtime = Read(Path.Combine(language, "eng/runtime-dependencies.json"));
        development = Read(Path.Combine(language, "eng/development-dependencies.json"));
        sdk = Text(Read(Path.Combine(language, "global.json"))["sdk"]!, "version");
        Require(sdk == Text(Read(Path.Combine(language, "eng/toolchain.json")), "sdk"), "SDK pins disagree.");
        runtimeAllowed = Packages(runtime);
        developmentAllowed = Packages(development);
        testAllowed = Items(development, "packages").Where(item => !Text(item, "role").EndsWith("-fixture", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(item => Text(item, "id"), item => Text(item, "version"), StringComparer.OrdinalIgnoreCase);
    }

    internal static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Length != 5) { throw new ArgumentException("Usage: dependencies <language-root> <project|-> <assets|-> <assembly|-> <package|->"); }
            var check = new DependencyChecks(args[0]);
            await check.Verify(args[1], args[2], args[3], args[4]);
            return 0;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or JsonException or System.Xml.XmlException or BadImageFormatException or TimeoutException or System.ComponentModel.Win32Exception)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    private async Task Verify(string selected, string assetsPath, string assembly, string package)
    {
        JsonObject[] projects = Items(policy, "projects").ToArray();
        if (selected != "-")
        {
            projects = projects.Where(project => Equal(Text(project, "path"), selected)).ToArray();
            Require(projects.Length == 1, "Unknown project requested: " + selected);
        }
        else
        {
            var known = projects.Select(project => Absolute(Text(project, "path"))).ToHashSet(PathComparer);
            string? unknown = Directory.EnumerateFiles(language, "*.csproj", SearchOption.AllDirectories)
                .FirstOrDefault(path => !Path.GetRelativePath(language, path).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj" or ".vs") && !known.Contains(path));
            Require(unknown is null, "Unregistered C# project: " + unknown);
        }
        foreach (JsonObject project in projects)
        {
            string path = Absolute(Text(project, "path"));
            JsonObject evaluation = await Evaluate(path);
            CheckEvaluation(project, evaluation, path);
            CheckRestored(project, assetsPath != "-" && Text(project, "role") == "shipping" ? Path.GetFullPath(assetsPath) : Path.Combine(Path.GetDirectoryName(path)!, "obj/project.assets.json"));
        }
        if (assembly != "-" || package != "-")
        {
            JsonObject evaluation = await Evaluate(Absolute(Text(runtime, "shipping_project")), true);
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonObject reference in Items(evaluation["Items"]!, "ReferencePath"))
            {
                string id = Text(reference, "Identity");
                Require(ShippingFrameworks().Contains(Optional(reference, "FrameworkReferenceName"), StringComparer.OrdinalIgnoreCase) ||
                    Approved(runtimeAllowed, Optional(reference, "NuGetPackageId"), Optional(reference, "NuGetPackageVersion")), "Unapproved resolved compiler reference: " + id);
                allowed.Add(Path.GetFileNameWithoutExtension(id));
            }
            string reports = Path.GetFullPath(Path.Combine(language, "../../.artifacts/csharp/dependencies"));
            Directory.CreateDirectory(reports);
            File.WriteAllText(Path.Combine(reports, "reference-names-" + Guid.NewGuid().ToString("N") + ".json"), JsonSerializer.Serialize(allowed.Order(StringComparer.Ordinal)));
            if (assembly != "-") { CheckAssembly(assembly, allowed); }
            if (package != "-") { await CheckPackage(package, assembly, allowed, reports); }
        }
        output.WriteLine($"Dependency boundaries passed for {projects.Length} evaluated projects; Core runtime inventory: {runtimeAllowed.Count} approved packages.");
    }

    private void CheckEvaluation(JsonObject project, JsonObject evaluation, string path)
    {
        string relative = Text(project, "path"), role = Text(project, "role");
        JsonNode properties = evaluation["Properties"]!, items = evaluation["Items"]!;
        Require(Equal(Text(properties, "TargetFramework"), Text(policy, "targetFramework")), "Unexpected target framework: " + relative);
        Require(Equal(Text(properties, "IsPackable"), role == "shipping" ? "true" : "false"), "Unexpected packability: " + relative);
        if (role == "shipping")
        {
            foreach (string name in new[] { "AssemblyName", "PackageId", "RootNamespace" })
            { Require(Equal(Text(properties, name), Text(policy["shippingIdentity"]!, name)), "Unexpected shipping identity " + name + " for " + relative); }
        }
        Require(role != "test" || Equal(Text(properties, "IsTestProject"), "true"), "Test project is not enabled: " + relative);
        Require(role != "process-fixture" || (Equal(Text(properties, "IsTestProject"), "false") && Equal(Text(properties, "OutputType"), "Exe")), "Process fixture must be a non-test executable: " + relative);
        var expectedProjects = Strings(project, "projectReferences").Select(Absolute).ToHashSet(PathComparer);
        var buildOnly = Strings(project, "buildOnlyProjectReferences").Select(Absolute).ToHashSet(PathComparer);
        var actualProjects = new HashSet<string>(PathComparer);
        foreach (JsonObject reference in Items(items, "ProjectReference"))
        {
            string id = Text(reference, "Identity"), resolved = FullPath(id, Path.GetDirectoryName(path)!);
            Require(expectedProjects.Contains(resolved), "Forbidden project reference from " + relative + ": " + id);
            Require(!buildOnly.Contains(resolved) || (Equal(Optional(reference, "ReferenceOutputAssembly"), "false") && Equal(Optional(reference, "Private"), "false") && Equal(Optional(reference, "PrivateAssets"), "all")), "Build-only project must not become a runtime reference: " + id);
            Require(actualProjects.Add(resolved), "Duplicate project reference: " + resolved);
        }
        Require(actualProjects.SetEquals(expectedProjects), "Project reference inventory mismatch: " + relative);
        var frameworks = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Text(policy, "implicitFramework") };
        if (role == "example" && project.ContainsKey("additionalImplicitFrameworkReferences"))
        { frameworks.UnionWith(Strings(project, "additionalImplicitFrameworkReferences")); }
        var explicitFrameworks = Strings(project, "additionalExplicitFrameworkReferences").ToHashSet(StringComparer.OrdinalIgnoreCase);
        Require(!frameworks.Overlaps(explicitFrameworks), "Framework reference has both implicit and explicit approval: " + relative);
        var actualFrameworks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonObject reference in Items(items, "FrameworkReference"))
        {
            string id = Text(reference, "Identity");
            bool implicitReference = Equal(Optional(reference, "IsImplicitlyDefined"), "true");
            bool explicitReference = Optional(reference, "IsImplicitlyDefined") is "" or "false";
            Require((frameworks.Contains(id) && implicitReference) || (explicitFrameworks.Contains(id) && explicitReference), "Forbidden framework reference from " + relative + ": " + id);
            Require(actualFrameworks.Add(id), "Duplicate framework reference: " + id);
        }
        frameworks.UnionWith(explicitFrameworks);
        Require(actualFrameworks.SetEquals(frameworks), "Framework reference inventory mismatch: " + relative);
        JsonObject[] manual = Items(items, "Reference").ToArray();
        foreach (JsonObject reference in manual)
        {
            string id = Text(reference, "Identity");
            if (role == "build-tool")
            {
                string bundled = Text(properties, "NETCoreSdkBundledVersionsProps"), sdkRoot = Text(properties, "NetCoreRoot");
                Require(Path.IsPathFullyQualified(bundled) && Path.IsPathFullyQualified(sdkRoot), "Missing absolute selected SDK facts.");
                string expectedSdk = FullPath("sdk/" + sdk, sdkRoot), selectedSdk = Path.GetDirectoryName(Path.GetFullPath(bundled))!;
                Require(Path.GetFileName(bundled) == "Microsoft.NETCoreSdk.BundledVersions.props" && selectedSdk.Equals(expectedSdk, PathComparison), "SDK origin does not match its root and version.");
                string expected = Path.Combine(expectedSdk, id + ".dll");
                Require(Strings(project, "sdkAssemblyReferences").Contains(id, StringComparer.OrdinalIgnoreCase) && Optional(reference, "Private") == "false" &&
                    Equal(Text(properties, "NETCoreSdkVersion"), sdk) && Path.GetFullPath(Text(reference, "HintPath")).Equals(expected, PathComparison) && File.Exists(expected),
                    "Unapproved pinned-SDK build-tool reference: " + id);
                continue;
            }
            JsonObject[] approved = Items(development, "buildAssemblyReferences").Where(item => Equal(Text(item, "name"), id)).ToArray();
            Require(role == "test" && approved.Length == 1, "Manual assembly references are forbidden: " + relative);
            string packageId = Text(approved[0], "packageId");
            JsonObject restored = Read(Path.Combine(Path.GetDirectoryName(path)!, "obj/project.assets.json"));
            bool origin = restored["packageFolders"]!.AsObject().Any(folder =>
            {
                string prefix = FullPath(packageId.ToLowerInvariant() + "/" + developmentAllowed[packageId], folder.Key) + Path.DirectorySeparatorChar;
                return Path.GetFullPath(Text(reference, "HintPath")).StartsWith(prefix, PathComparison) && Path.GetFullPath(Text(reference, "DefiningProjectFullPath")).StartsWith(prefix, PathComparison);
            });
            Require(origin, "Unapproved origin for test-tool assembly reference: " + id);
        }
        Require(role != "build-tool" || manual.Length == Strings(project, "sdkAssemblyReferences").Length, "Build-tool SDK reference inventory mismatch: " + relative);
        var expectedPackages = Strings(project, "packageReferences").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var actualPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonObject reference in Items(items, "PackageReference"))
        {
            string id = Text(reference, "Identity");
            Require(expectedPackages.Contains(id), "Unapproved explicit package from " + relative + ": " + id);
            Require(role != "test" || Equal(Optional(reference, "PrivateAssets"), "all"), "Test package must have PrivateAssets=all: " + id);
            Require(role != "process-fixture" || !Strings(project, "developmentPackageReferences").Contains(id, StringComparer.OrdinalIgnoreCase) || Equal(Optional(reference, "PrivateAssets"), "all"), "Fixture development package must have PrivateAssets=all: " + id);
            Require(actualPackages.Add(id), "Duplicate package reference: " + id);
        }
        Require(actualPackages.SetEquals(expectedPackages), "Package reference inventory mismatch: " + relative);
    }

    private void CheckRestored(JsonObject project, string path)
    {
        Require(File.Exists(path), "Restore assets are missing: " + path);
        JsonObject assets = Read(path);
        string relative = Text(project, "path"), role = Text(project, "role");
        JsonObject? target = assets["targets"]?[Text(policy, "targetFramework")] as JsonObject;
        Require(target is not null, "Missing restored target for " + relative + ": " + Text(policy, "targetFramework"));
        string[] expected = Strings(project, project.ContainsKey("restoredProjectReferences") ? "restoredProjectReferences" : "projectReferences").Select(Absolute).ToArray();
        var resolvedProjects = new HashSet<string>(PathComparer);
        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string identity, JsonNode? entry) in target!)
        {
            string[] parts = identity.Split('/', 2);
            Require(parts.Length == 2, "Unexpected restored dependency identity: " + identity);
            string id = parts[0], version = parts[1];
            if (Text(entry!, "type") == "project")
            {
                string projectAsset = FullPath(Text(assets["libraries"]![identity]!, "msbuildProject"), Path.GetDirectoryName(Text(assets["project"]!["restore"]!, "projectPath"))!);
                Require(expected.Contains(projectAsset, PathComparer), "Unapproved restored project dependency for " + relative + ": " + id);
                resolvedProjects.Add(projectAsset);
                continue;
            }
            Require(Text(entry!, "type") == "package", "Unexpected restored dependency type: " + identity);
            bool allowed = role is "shipping" or "test" or "example" or "process-fixture" && Approved(runtimeAllowed, id, version);
            if (role == "test") { allowed |= Approved(testAllowed, id, version); }
            if (role == "process-fixture" && Strings(project, "developmentPackageReferences").Contains(id, StringComparer.OrdinalIgnoreCase))
            { allowed |= Approved(developmentAllowed, id, version); }
            Require(allowed, "Unapproved " + role + " package dependency: " + identity);
            resolved[id] = version;
        }
        foreach (string id in Strings(project, "packageReferences")) { Require(resolved.ContainsKey(id), "Required restored package is missing: " + id); }
        foreach (string reference in expected) { Require(resolvedProjects.Contains(reference), "Required restored project is missing: " + reference); }
        Require(role != "shipping" || resolved.Count == runtimeAllowed.Count, "Core runtime closure differs from the approved inventory.");
    }

    private async Task CheckPackage(string path, string assembly, HashSet<string> allowed, string reports)
    {
        using ZipArchive archive = ZipFile.OpenRead(path);
        var assets = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["buildSupport/CIS.TypeManager.targets"] = "build/type-manager/CIS.TypeManager.targets",
            ["Sdk/Sdk.props"] = "build/type-manager/sdk/Sdk.props",
            ["Sdk/Sdk.targets"] = "build/type-manager/sdk/Sdk.targets",
            ["Sdk/BeforeSdk.targets"] = "build/type-manager/sdk/BeforeSdk.targets",
            ["buildTransitive/CIS.Core.targets"] = "build/type-manager/package/CIS.Core.targets"
        };
        string[] required = [.. assets.Keys, "Sdk/PackageVersion.props", "tools/net10.0/CIS.TypeManager.Build.dll"];
        var entries = new HashSet<string>(["lib/net10.0/CIS.Core.dll", "lib/net10.0/CIS.Core.xml", "README.md", "CIS.Core.nuspec", "[Content_Types].xml", "_rels/.rels", .. required], StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            Require(names.Add(entry.FullName), "Duplicate packed asset: " + entry.FullName);
            Require(entries.Contains(entry.FullName) || Regex.IsMatch(entry.FullName, @"^package/services/metadata/core-properties/[^/]+\.psmdcp$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)), "Unexpected packed asset: " + entry.FullName);
        }
        Require(archive.GetEntry("lib/net10.0/CIS.Core.dll") is not null, "Core DLL target asset is missing.");
        foreach (string name in required) { Require(archive.GetEntry(name) is not null, "Type Manager package asset is missing or duplicated: " + name); }
        foreach ((string name, string source) in assets)
        { Require(EntryHash(archive.GetEntry(name)!) == Hash(Absolute(source)), "Packed Type Manager build asset differs from source: " + name); }
        string helper;
        if (prepared is not null) { helper = prepared.Helper; }
        else
        {
            ProcessResult result = await Run(["msbuild", "eng/CIS.TypeManager.Build/CIS.TypeManager.Build.csproj", "-p:Configuration=Release", "-getProperty:TargetPath", "-nologo"]);
            helper = result.StandardOutput.Trim();
            Require(result.ExitCode == 0, "Build the Release Type Manager helper before inspecting its package.");
        }
        Require(File.Exists(helper), "Build the Release Type Manager helper before inspecting its package.");
        Require(EntryHash(archive.GetEntry("tools/net10.0/CIS.TypeManager.Build.dll")!) == Hash(helper), "Packed Type Manager helper differs from the checked Release tool.");
        ZipArchiveEntry? specEntry = archive.GetEntry("CIS.Core.nuspec");
        Require(specEntry is not null, "Core package manifest is missing.");
        XDocument spec = Xml(specEntry!), props = Xml(archive.GetEntry("Sdk/PackageVersion.props")!);
        Require(props.Descendants("_CisCoreSdkPackageVersion").Single().Value == spec.Descendants().Single(item => item.Name.LocalName == "metadata").Elements().Single(item => item.Name.LocalName == "version").Value, "Type Manager SDK and runtime package versions differ.");
        XElement[] groups = spec.Descendants().Where(item => item.Name.LocalName == "dependencies").SelectMany(item => item.Elements().Where(child => child.Name.LocalName == "group")).ToArray();
        Require(groups.Length == 1 && Equal((string?)groups[0].Attribute("targetFramework"), "net10.0"), "Unexpected package dependency groups.");
        JsonObject[] direct = Items(runtime, "packages").Where(item => Text(item, "role") == "direct").ToArray();
        XElement[] dependencies = groups[0].Elements().Where(item => item.Name.LocalName == "dependency").ToArray();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (XElement item in dependencies) { Require(ids.Add((string?)item.Attribute("id") ?? ""), "Duplicate packed dependency: " + (string?)item.Attribute("id")); }
        Require(dependencies.Length == direct.Length, "Packed direct dependency inventory mismatch.");
        foreach (XElement item in dependencies)
        {
            JsonObject[] matches = direct.Where(candidate => Equal(Text(candidate, "id"), (string?)item.Attribute("id"))).ToArray();
            Require(matches.Length == 1 && Equal((string?)item.Attribute("version"), Text(matches[0], "version")), "Unapproved packed dependency: " + (string?)item.Attribute("id") + "/" + (string?)item.Attribute("version"));
        }
        Require(ids.SetEquals(direct.Select(item => Text(item, "id"))), "Packed direct dependency set mismatch.");
        XElement[] frameworkGroups = spec.Descendants().Where(item => item.Name.LocalName == "frameworkReferences").SelectMany(item => item.Elements()).ToArray();
        Require(frameworkGroups.Length == 1 && frameworkGroups[0].Name.LocalName == "group" &&
            Equal((string?)frameworkGroups[0].Attribute("targetFramework"), Text(policy, "targetFramework")), "Packed framework reference group mismatch.");
        XElement[] frameworkReferences = frameworkGroups[0].Elements().ToArray();
        var frameworkNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (XElement reference in frameworkReferences)
        {
            string name = (string?)reference.Attribute("name") ?? "";
            Require(reference.Name.LocalName == "frameworkReference" && frameworkNames.Add(name), "Invalid or duplicate packed framework reference: " + name);
        }
        string[] expectedFrameworks = Strings(Items(policy, "projects").Single(project => Text(project, "role") == "shipping"), "additionalExplicitFrameworkReferences");
        Require(frameworkNames.SetEquals(expectedFrameworks), "Packed framework reference inventory mismatch.");
        string packed = Path.Combine(reports, "CIS.Core-" + Guid.NewGuid().ToString("N") + ".dll");
        using (Stream input = archive.GetEntry("lib/net10.0/CIS.Core.dll")!.Open())
        using (Stream output = File.Create(packed)) { input.CopyTo(output); }
        CheckAssembly(packed, allowed);
        Require(assembly == "-" || Hash(assembly) == Hash(packed), "Packed Core DLL differs from the checked Release assembly.");
    }

    private void CheckAssembly(string path, HashSet<string> allowed)
    {
        Require(AssemblyName.GetAssemblyName(path).Name == "CIS.Core", "Unexpected compiled shipping assembly identity.");
        IReadOnlyList<string> unexpected = AssemblyReferences.FindUnexpected(path, allowed);
        Require(unexpected.Count == 0, "Unapproved compiled assembly reference: " + string.Join(", ", unexpected));
        output.WriteLine(JsonSerializer.Serialize(new { Assembly = path, References = AssemblyReferences.Read(path) }));
    }
    private async Task<JsonObject> Evaluate(string path, bool resolve = false)
    {
        if (prepared is not null) { return prepared.Evaluation(path, resolve); }
        string[] arguments = resolve
            ? ["msbuild", path, "-t:ResolveReferences", "-p:BuildProjectReferences=false", "-getItem:ReferencePath", "-getProperty:MSBuildAllProjects"]
            : ["msbuild", path, "-getItem:ProjectReference,PackageReference,FrameworkReference,Reference", "-getProperty:TargetFramework,IsPackable,IsTestProject,AssemblyName,PackageId,RootNamespace,OutputType,MSBuildBinPath,MSBuildSDKsPath,NETCoreSdkBundledVersionsProps,NetCoreRoot,NETCoreSdkVersion,MSBuildAllProjects"];
        ProcessResult result = await Run(arguments);
        Require(result.ExitCode == 0, "Project evaluation failed: " + path + Environment.NewLine + result.StandardOutput + result.StandardError);
        return JsonNode.Parse(result.StandardOutput)!.AsObject();
    }
    private Task<ProcessResult> Run(IEnumerable<string> args) => ProcessRunner.RunAsync("dotnet", args, language,
        deadline?.Remaining() ?? TimeSpan.FromMinutes(2), null, null, 1048576, false, null, deadline?.Token ?? CancellationToken.None);
    private IEnumerable<string> ShippingFrameworks() =>
        new[] { Text(policy, "implicitFramework") }.Concat(Strings(
            Items(policy, "projects").Single(project => Text(project, "role") == "shipping"),
            "additionalExplicitFrameworkReferences"));
    private string Absolute(string path) => FullPath(path, language);
    private static string FullPath(string path, string root) => Path.GetFullPath(path.Replace('\\', Path.DirectorySeparatorChar), root);
    private static JsonObject Read(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    private static IEnumerable<JsonObject> Items(JsonNode node, string key) => node[key]!.AsArray().Select(item => item!.AsObject());
    private static string[] Strings(JsonNode node, string key) => node[key] is JsonArray items ? items.Select(item => item!.GetValue<string>()).ToArray() : [];
    private static string Text(JsonNode node, string key) => node[key]!.GetValue<string>();
    private static string Optional(JsonNode node, string key) => node[key]?.GetValue<string>() ?? "";
    private static bool Equal(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private static Dictionary<string, string> Packages(JsonObject policy) => Items(policy, "packages").ToDictionary(item => Text(item, "id"), item => Text(item, "version"), StringComparer.OrdinalIgnoreCase);
    private static bool Approved(Dictionary<string, string> packages, string id, string version) => packages.TryGetValue(id, out string? expected) && Equal(expected, version);
    private static string Hash(string path) { using Stream stream = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(stream)); }
    private static string EntryHash(ZipArchiveEntry entry) { using Stream stream = entry.Open(); return Convert.ToHexStringLower(SHA256.HashData(stream)); }
    private static XDocument Xml(ZipArchiveEntry entry) { using Stream stream = entry.Open(); return XDocument.Load(stream); }
    private static void Require(bool condition, string detail) { if (!condition) { throw new InvalidDataException(detail); } }
}
