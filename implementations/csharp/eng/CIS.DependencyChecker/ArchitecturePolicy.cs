using System.Reflection;
using System.Text.Json.Nodes;

namespace CIS.Verification;

// Versioned repository policy applied to the independently collected facts. No inspected code is executed.
internal static class ArchitecturePolicy
{
    internal static JsonObject Assert(string root, JsonObject report)
    {
        JsonObject policy = Read(root, "eng/project-boundaries.json");
        JsonObject runtime = Read(root, "eng/runtime-dependencies.json");
        JsonObject development = Read(root, "eng/development-dependencies.json");
        JsonObject toolchain = Read(root, "eng/toolchain.json");
        Dictionary<string, string> runtimePackages = Packages(runtime);
        Dictionary<string, string> developmentPackages = Packages(development);
        JsonObject[] projects = Objects(report["projects"]).ToArray();
        var registered = Objects(policy["projects"]).ToDictionary(item => Text(item, "path"), StringComparer.Ordinal);
        Same(projects.Select(item => Text(item, "project")), registered.Keys, "ARCH-PROJECT-INVENTORY", "projects");
        var compilerReferences = new Dictionary<string, Dictionary<string, AssemblyName>>(StringComparer.Ordinal);
        foreach (JsonObject project in projects)
        {
            string path = Text(project, "project");
            JsonObject rule = registered[path];
            string role = Text(rule, "role");
            Require(Text(project, "role") == role, "ARCH-PROJECT-ROLE", path);
            JsonObject assets = JsonNode.Parse(File.ReadAllText(Text(project["assets"]!, "path")))!.AsObject();
            foreach (JsonObject target in Objects(project["targets"]))
            {
                string context = path + " " + Text(target, "target");
                JsonNode properties = target["properties"]!, items = target["items"]!;
                Require(Text(properties, "TargetFramework") == Text(policy, "targetFramework"), "ARCH-FRAMEWORK", context);
                Require(Text(properties, "IsPackable").Equals(role == "shipping" ? "true" : "false", StringComparison.OrdinalIgnoreCase), "ARCH-PACKABILITY", context);
                if (role == "shipping")
                {
                    foreach ((string key, JsonNode? value) in policy["shippingIdentity"]!.AsObject())
                    { Require(Text(properties, key) == value!.GetValue<string>(), "ARCH-SHIPPING-IDENTITY", context + " " + key); }
                }
                string[] frameworks = new[] { Text(policy, "implicitFramework") }.Concat(Strings(rule["additionalImplicitFrameworkReferences"])).ToArray();
                Same(Objects(items["FrameworkReference"]).Select(item => Text(item, "Identity")), frameworks, "ARCH-FRAMEWORK", context);
                Require(Objects(items["FrameworkReference"]).All(item => Text(item, "IsImplicitlyDefined") == "true"), "ARCH-FRAMEWORK", context);
                JsonObject[] references = Objects(items["ProjectReference"]).ToArray();
                Same(references.Select(item => Relative(root, Path.GetFullPath(Text(item, "Identity"), Path.GetDirectoryName(Path.Combine(root, path))!))), Strings(rule["projectReferences"]), "ARCH-PROJECT-REFERENCE", context);
                foreach (JsonObject reference in references)
                {
                    string relative = Relative(root, Path.GetFullPath(Text(reference, "Identity"), Path.GetDirectoryName(Path.Combine(root, path))!));
                    if (Strings(rule["buildOnlyProjectReferences"]).Contains(relative, StringComparer.Ordinal))
                    {
                        Require(Text(reference, "ReferenceOutputAssembly") == "false" && Text(reference, "Private") == "false" && Text(reference, "PrivateAssets") == "all", "ARCH-BUILD-ONLY", context);
                    }
                }
                JsonObject[] direct = Objects(items["PackageReference"]).ToArray();
                Same(direct.Select(item => Text(item, "Identity")), Strings(rule["packageReferences"]), "ARCH-PACKAGE-REFERENCE", context);
                foreach (JsonObject reference in direct)
                {
                    if (role == "test" || Strings(rule["developmentPackageReferences"]).Contains(Text(reference, "Identity"), StringComparer.OrdinalIgnoreCase))
                    {
                        Require(Text(reference, "PrivateAssets") == "all", "ARCH-DEVELOPMENT-PRIVATE", context);
                    }
                }
                string[] restored = Strings(rule["restoredProjectReferences"] ?? rule["projectReferences"]).ToArray();
                var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (role == "shipping" || restored.Contains(Text(runtime, "shipping_project"), StringComparer.Ordinal))
                {
                    foreach (var package in runtimePackages) { expected.Add(package.Key, package.Value); }
                }
                foreach (string id in Strings(rule["frameworkProvidedRuntimePackages"]))
                {
                    Require(role == "example" && frameworks.Contains("Microsoft.AspNetCore.App", StringComparer.Ordinal) && expected.Remove(id), "ARCH-FRAMEWORK-PROVIDED", context + " " + id);
                }
                if (role == "test")
                {
                    foreach (JsonObject package in Objects(development["packages"]).Where(item => !Text(item, "role").EndsWith("-fixture", StringComparison.Ordinal)))
                    { expected.Add(Text(package, "id"), Text(package, "version")); }
                }
                foreach (string id in Strings(rule["developmentPackageReferences"])) { expected.Add(id, developmentPackages[id]); }
                JsonObject[] packages = Objects(target["packages"]).ToArray();
                Same(packages.Select(item => Text(item, "id")), expected.Keys, "ARCH-PACKAGE-CLOSURE", context, StringComparer.OrdinalIgnoreCase);
                foreach (JsonObject package in packages)
                {
                    string id = Text(package, "id");
                    Require(Text(package, "version") == expected[id], "ARCH-PACKAGE-VERSION", context + " " + id);
                }
                foreach (JsonObject reference in direct)
                {
                    string id = Text(reference, "Identity");
                    Require(expected.ContainsKey(id), "ARCH-PACKAGE-REFERENCE", context + " " + id);
                    foreach (string field in new[] { "Version", "VersionOverride" })
                    { Require(Text(reference, field).Length == 0 || Text(reference, field) == expected[id], "ARCH-PACKAGE-VERSION", context + " " + id); }
                }
                string projectDirectory = Path.GetDirectoryName(Text(project["input"]!, "path"))!;
                string[] restoredPaths = target["graph"]!.AsObject().Where(item => Text(item.Value!, "type") == "project")
                    .Select(item => Relative(root, Path.GetFullPath(Text(assets["libraries"]![item.Key]!, "msbuildProject"), projectDirectory))).ToArray();
                Same(restoredPaths, restored, "ARCH-RESTORED-PROJECT", context);
                CheckExplicitAssemblies(context, rule, items, properties, assets, development);
                if (role is "shipping" or "build-tool")
                { compilerReferences.Add(context, CompilerReferences(target, rule, runtimePackages, toolchain)); }
            }
            foreach (JsonObject framework in project["restoreFrameworks"]!.AsObject().Select(item => item.Value!.AsObject()))
            {
                foreach (JsonObject download in Objects(framework["downloadDependencies"]))
                {
                    JsonObject[] approved = Objects(toolchain["frameworkDownloads"]!["packages"]).Where(item => Text(item, "id") == Text(download, "name")).ToArray();
                    Require(approved.Length == 1 && Text(download, "version") == "[" + Text(approved[0], "version") + ", " + Text(approved[0], "version") + "]", "ARCH-FRAMEWORK-DOWNLOAD", Text(project, "project") + " " + Text(download, "name"));
                }
            }
        }
        JsonObject[] groups = Objects(report["packageDependencyGroups"]).ToArray();
        Require(groups.Length == 1 && Text(groups[0], "framework") == Text(runtime, "target_framework"), "ARCH-PACKAGE-GROUP", "Core");
        JsonObject[] directRuntime = Objects(runtime["packages"]).Where(item => Text(item, "role") == "direct").ToArray();
        Same(Objects(groups[0]["dependencies"]).Select(item => Text(item, "id")), directRuntime.Select(item => Text(item, "id")), "ARCH-PACKAGE-GROUP", "Core");
        foreach (JsonObject dependency in Objects(groups[0]["dependencies"]))
        { Require(Text(dependency, "version") == runtimePackages[Text(dependency, "id")], "ARCH-PACKAGE-VERSION", "Core nuspec"); }
        JsonObject[] assemblies = Objects(report["assemblies"]).ToArray();
        JsonObject[] packed = Objects(policy["packagedAssemblies"]).ToArray();
        Same(assemblies.Select(item => Text(item, "packageEntry")), packed.Select(item => Text(item, "path")), "ARCH-PACKED-ASSEMBLY", "Core");
        foreach (JsonObject assembly in assemblies)
        {
            JsonObject approved = packed.Single(item => Text(item, "path") == Text(assembly, "packageEntry"));
            Require(Text(assembly, "project") == Text(approved, "project"), "ARCH-PACKED-ORIGIN", Text(assembly, "packageEntry"));
            JsonObject origin = projects.Single(item => Text(item, "project") == Text(assembly, "project"));
            Require(Text(origin, "role") is "shipping" or "build-tool", "ARCH-PACKED-ORIGIN", Text(assembly, "packageEntry"));
            JsonObject target = Objects(origin["targets"]).Single(item => Text(item, "target") == Text(assembly, "targetFramework"));
            Dictionary<string, AssemblyName> allowed = compilerReferences[Text(origin, "project") + " " + Text(target, "target")];
            JsonNode facts = assembly["facts"]!;
            foreach (JsonObject reference in Objects(facts["references"]))
            {
                string name = Text(reference, "name");
                Require(allowed.TryGetValue(name, out AssemblyName? identity) && Text(reference, "version") == identity.Version?.ToString() && Text(reference, "culture") == (identity.CultureName ?? "") && Text(reference, "publicKeyOrToken") == Convert.ToHexStringLower(identity.GetPublicKeyToken() ?? []) && Text(reference, "flags") == "0", "ARCH-COMPILED-REFERENCE", Text(facts, "name") + " -> " + name);
            }
            foreach (JsonObject use in Objects(facts["typeUses"]))
            { Require(Text(use, "Assembly") == Text(facts, "name") || allowed.ContainsKey(Text(use, "Assembly")), "ARCH-TYPE-DEPENDENCY", Text(use, "Site") + " -> " + Text(use, "Assembly")); }
        }
        AssertTools(toolchain, Read(root, ".config/dotnet-tools.json"));
        return new JsonObject { ["schema"] = "cis.architecture-allowlists/v1", ["violations"] = 0, ["projects"] = projects.Length, ["targets"] = projects.Sum(project => Objects(project["targets"]).Count()), ["runtimePackages"] = runtimePackages.Count, ["packagedAssemblies"] = assemblies.Length };
    }

    internal static void AssertTools(JsonObject toolchain, JsonObject manifest)
    {
        JsonObject[] tools = Objects(toolchain["tools"]).ToArray();
        Same(manifest["tools"]!.AsObject().Select(item => item.Key), tools.Select(item => Text(item, "id")), "ARCH-TOOL-MANIFEST", "dotnet tools", StringComparer.OrdinalIgnoreCase);
        foreach ((string id, JsonNode? entry) in manifest["tools"]!.AsObject())
        {
            JsonObject approved = tools.Single(tool => Text(tool, "id").Equals(id, StringComparison.OrdinalIgnoreCase));
            Require(Text(entry!, "version") == Text(approved, "version"), "ARCH-TOOL-VERSION", id);
            Same(Strings(entry!["commands"]), [Text(approved, "command")], "ARCH-TOOL-COMMAND", id);
            Require(!approved["shippingAllowed"]!.GetValue<bool>(), "ARCH-TOOL-SHIPPING", id);
        }
    }

    private static void CheckExplicitAssemblies(string context, JsonObject rule, JsonNode items, JsonNode properties, JsonObject assets, JsonObject development)
    {
        JsonObject[] references = Objects(items["Reference"]).ToArray();
        if (Text(rule, "role") == "build-tool")
        {
            Same(references.Select(item => Text(item, "Identity")), Strings(rule["sdkAssemblyReferences"]), "ARCH-SDK-REFERENCE", context);
            foreach (JsonObject reference in references)
            { Require(Text(reference, "Private") == "false" && SamePath(Text(reference, "HintPath"), Path.Combine(Text(properties, "MSBuildBinPath"), Text(reference, "Identity") + ".dll")), "ARCH-SDK-REFERENCE", context); }
            return;
        }
        foreach (JsonObject reference in references)
        {
            JsonObject[] approved = Objects(development["buildAssemblyReferences"]).Where(item => Text(item, "name") == Text(reference, "Identity")).ToArray();
            Require(Text(rule, "role") == "test" && approved.Length == 1, "ARCH-MANUAL-REFERENCE", context);
            string id = Text(approved[0], "packageId"), version = Packages(development)[id];
            Require(assets["packageFolders"]!.AsObject().Any(folder => Under(Text(reference, "HintPath"), Path.Combine(folder.Key, id.ToLowerInvariant(), version)) && Under(Text(reference, "DefiningProjectFullPath"), Path.Combine(folder.Key, id.ToLowerInvariant(), version))), "ARCH-MANUAL-ORIGIN", context);
        }
    }

    private static Dictionary<string, AssemblyName> CompilerReferences(JsonObject target, JsonObject rule, Dictionary<string, string> runtime, JsonObject toolchain)
    {
        JsonNode properties = target["properties"]!;
        string sdk = Text(properties, "MSBuildBinPath"), framework = Text(properties, "TargetFramework");
        Require(Text(properties, "NETCoreSdkVersion") == Text(toolchain, "sdk"), "ARCH-SDK-VERSION", sdk);
        string dotnet = Directory.GetParent(Directory.GetParent(sdk)!.FullName)!.FullName;
        string frameworkRoot = Path.Combine(dotnet, "packs", "Microsoft.NETCore.App.Ref", Text(toolchain, "targetRuntime"), "ref", framework);
        var allowed = new Dictionary<string, AssemblyName>(StringComparer.Ordinal);
        foreach (JsonObject reference in Objects(target["items"]!["ReferencePath"]))
        {
            string path = Text(reference, "Identity"), id = Text(reference, "NuGetPackageId"), version = Text(reference, "NuGetPackageVersion");
            bool approved = Text(reference, "FrameworkReferenceName") == "Microsoft.NETCore.App" && Under(path, frameworkRoot) && version == Text(toolchain, "targetRuntime");
            if (!approved && Text(rule, "role") == "shipping" && runtime.TryGetValue(id, out string? expected) && expected == version)
            {
                // The resolved origin must be one of NuGet's restored package roots, with the exact selected asset.
                JsonObject assets = JsonNode.Parse(File.ReadAllText(Text(properties, "ProjectAssetsFile")))!.AsObject();
                string asset = Text(reference, "PathInPackage");
                approved = target["graph"]?[id + "/" + version]?["compile"] is JsonObject compile && compile.ContainsKey(asset) && asset.Length > 0 && !Path.IsPathRooted(asset) && assets["packageFolders"]!.AsObject().Any(folder => SamePath(path, Path.Combine(folder.Key, id.ToLowerInvariant(), version, asset)) && Under(path, Path.Combine(folder.Key, id.ToLowerInvariant(), version)));
            }
            if (!approved && Text(rule, "role") == "build-tool")
            { approved = Strings(rule["sdkAssemblyReferences"]).Any(name => SamePath(path, Path.Combine(sdk, name + ".dll"))) && Text(reference, "Private") == "false"; }
            Require(approved, "ARCH-COMPILER-ORIGIN", path);
            AssemblyName identity = AssemblyName.GetAssemblyName(path);
            Require(identity.Name is not null && allowed.TryAdd(identity.Name, identity), "ARCH-COMPILER-IDENTITY", path);
        }
        Require(allowed.Count > 0, "ARCH-COMPILER-IDENTITY", Text(rule, "path"));
        return allowed;
    }

    private static JsonObject Read(string root, string path) => JsonNode.Parse(File.ReadAllText(Path.Combine(root, path)))!.AsObject();
    private static Dictionary<string, string> Packages(JsonObject value) => Objects(value["packages"]).ToDictionary(item => Text(item, "id"), item => Text(item, "version"), StringComparer.OrdinalIgnoreCase);
    private static IEnumerable<JsonObject> Objects(JsonNode? node) => node?.AsArray().Select(item => item!.AsObject()) ?? [];
    private static IEnumerable<string> Strings(JsonNode? node) => node?.AsArray().Select(item => item!.GetValue<string>()) ?? [];
    private static string Text(JsonNode value, string key) => value[key]?.GetValue<string>() ?? "";
    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
    private static bool SamePath(string left, string right) => Path.GetFullPath(left).Equals(Path.GetFullPath(right), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static bool Under(string path, string root) => Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static void Same(IEnumerable<string> actual, IEnumerable<string> expected, string code, string context, StringComparer? comparer = null)
    {
        comparer ??= StringComparer.Ordinal;
        string[] values = actual.ToArray(), required = expected.ToArray();
        Require(values.Length == values.Distinct(comparer).Count() && values.Length == required.Length && values.ToHashSet(comparer).SetEquals(required), code, context);
    }
    private static void Require(bool condition, string code, string context)
    { if (!condition) { throw new InvalidDataException(code + ": " + context); } }
}
