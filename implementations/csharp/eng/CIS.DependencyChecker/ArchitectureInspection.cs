using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace CIS.Verification;

internal static class ArchitectureInspection
{
    internal static async Task Write(string languageRoot, string packagePath, string outputRoot)
    {
        languageRoot = Path.GetFullPath(languageRoot);
        packagePath = Path.GetFullPath(packagePath);
        outputRoot = Path.GetFullPath(outputRoot);
        Directory.CreateDirectory(outputRoot);
        string reportPath = Path.Combine(outputRoot, "inspection.json");
        File.Delete(reportPath);
        JsonObject policy = Read(Path.Combine(languageRoot, "eng/project-boundaries.json"));
        JsonObject toolchain = Read(Path.Combine(languageRoot, "eng/toolchain.json"));
        string sdk = await Run(languageRoot, "dotnet", ["--version"]);
        Require(sdk.Trim() == Text(toolchain, "sdk"), "SDK does not match the selected pin.");
        JsonArray registered = policy["projects"]!.AsArray();
        string[] discovered = Directory.EnumerateFiles(languageRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(languageRoot, path).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj" or ".vs"))
            .Order(StringComparer.Ordinal).ToArray();
        Require(discovered.Length == registered.Count, "Discovered project count differs from the registered inventory.");
        var projects = new List<ProjectFact>();
        foreach (string path in discovered)
        {
            string relative = Path.GetRelativePath(languageRoot, path).Replace('\\', '/');
            JsonObject[] matches = registered.OfType<JsonObject>().Where(item => Text(item, "path") == relative).ToArray();
            Require(matches.Length == 1, "Unregistered project: " + relative);
            string role = Text(matches[0], "role");
            JsonObject evaluation = await Evaluate(languageRoot, path);
            JsonObject properties = evaluation["Properties"]!.AsObject();
            string assetsPath = Text(properties, "ProjectAssetsFile");
            string lockPath = Text(properties, "NuGetLockFilePath");
            if (lockPath.Length == 0) { lockPath = Path.Combine(Path.GetDirectoryName(path)!, "packages.lock.json"); }
            if (!Path.IsPathRooted(lockPath)) { lockPath = Path.Combine(Path.GetDirectoryName(path)!, lockPath); }
            JsonObject assets = Read(assetsPath), locked = Read(lockPath);
            Require(assets["version"]!.GetValue<int>() is 3 or 4 && locked["version"]!.GetValue<int>() is 1 or 2, "Unsupported graph schema: " + relative);
            Require(Path.GetFullPath(assets["project"]!["restore"]!["projectPath"]!.GetValue<string>()) == Path.GetFullPath(path), "Restored graph belongs to another project: " + relative);
            JsonObject restoredTargets = assets["targets"]!.AsObject(), lockTargets = locked["dependencies"]!.AsObject();
            string[] frameworks = Split(Text(properties, "TargetFrameworks"));
            if (frameworks.Length == 0) { frameworks = [Text(properties, "TargetFramework")]; }
            string[] rids = Split(Text(properties, "RuntimeIdentifiers")).Concat(Split(Text(properties, "RuntimeIdentifier"))).Distinct(StringComparer.Ordinal).ToArray();
            string[] expectedTargets = frameworks.SelectMany(tfm => new[] { tfm }.Concat(rids.Select(rid => tfm + "/" + rid))).ToArray();
            Require(expectedTargets.ToHashSet(StringComparer.Ordinal).SetEquals(restoredTargets.Select(item => item.Key)), "Declared/restored target inventory differs: " + relative);
            Require(restoredTargets.Select(item => item.Key).ToHashSet(StringComparer.Ordinal).SetEquals(lockTargets.Select(item => item.Key)), "Locked target inventory differs: " + relative);
            var targets = new List<TargetFact>();
            foreach ((string targetName, JsonNode? node) in restoredTargets.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                string[] parts = targetName.Split('/', 2);
                string framework = parts[0], rid = parts.Length == 2 ? parts[1] : "";
                JsonObject graph = node!.AsObject();
                // NuGet RID lock sections contain deltas over the base framework graph.
                var effectiveLock = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
                foreach ((string id, JsonNode? entry) in lockTargets[framework]!.AsObject()) { effectiveLock[id] = entry; }
                foreach ((string id, JsonNode? entry) in lockTargets[targetName]!.AsObject()) { effectiveLock[id] = entry; }
                var packages = new List<object>();
                foreach ((string identity, JsonNode? entry) in graph.OrderBy(item => item.Key, StringComparer.Ordinal))
                {
                    string type = entry!["type"]!.GetValue<string>();
                    if (type == "project") { continue; }
                    Require(type == "package", "Unknown restored entry type: " + identity);
                    string[] identityParts = identity.Split('/', 2);
                    string id = identityParts[0], version = identityParts[1];
                    Require(effectiveLock.TryGetValue(id, out JsonNode? lockedEntry) && lockedEntry is not null &&
                        lockedEntry["resolved"]?.GetValue<string>() == version && lockedEntry["contentHash"]?.GetValue<string>() == assets["libraries"]![identity]!["sha512"]?.GetValue<string>(),
                        $"Lock/assets mismatch: {relative} {targetName} {identity}");
                    packages.Add(new { id, version, contentHash = lockedEntry!["contentHash"], lockType = lockedEntry["type"], assets = entry });
                }
                Require(effectiveLock.Count(item => item.Value!["type"]!.GetValue<string>() != "Project") == packages.Count, $"Locked package inventory mismatch: {relative} {targetName}");
                JsonObject context = await Evaluate(languageRoot, path, framework, rid);
                if (role is "shipping" or "build-tool")
                {
                    JsonObject resolved = await Evaluate(languageRoot, path, framework, rid, resolve: true);
                    context["Items"]!["ReferencePath"] = resolved["Items"]!["ReferencePath"]!.DeepClone();
                }
                targets.Add(new(targetName, context["Properties"]!, context["Items"]!, packages, effectiveLock,
                    graph.Where(item => item.Value!["type"]!.GetValue<string>() == "project").Select(item => item.Key).ToArray(), graph));
            }
            projects.Add(new(relative, role, Reference(path), properties, evaluation["Items"]!, Reference(assetsPath), Reference(lockPath), targets, assets["project"]!["frameworks"]!));
        }
        ProjectFact[] packable = projects.Where(project => Text(project.properties, "IsPackable").Equals("true", StringComparison.OrdinalIgnoreCase)).ToArray();
        Require(packable.Length == 1, "Expected the selected single packable Core project.");
        var assemblyFacts = new List<object>();
        var packageAssets = new List<object>();
        object[] dependencyGroups;
        using (ZipArchive archive = ZipFile.OpenRead(packagePath))
        {
            Require(archive.Entries.Select(entry => entry.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count() == archive.Entries.Count, "Duplicate package asset paths.");
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                using Stream stream = entry.Open();
                packageAssets.Add(new { path = entry.FullName, bytes = entry.Length, sha256 = Convert.ToHexStringLower(SHA256.HashData(stream)) });
            }
            ZipArchiveEntry[] nuspecs = archive.Entries.Where(entry => entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase)).ToArray();
            Require(nuspecs.Length == 1, "Expected one package manifest.");
            XElement metadata;
            using (Stream stream = nuspecs[0].Open()) { metadata = XDocument.Load(stream).Descendants().Single(element => element.Name.LocalName == "metadata"); }
            Require(metadata.Elements().Single(element => element.Name.LocalName == "id").Value == Text(packable[0].properties, "PackageId"), "Package identity differs from evaluated packable project.");
            dependencyGroups = metadata.Elements().Where(element => element.Name.LocalName == "dependencies").SelectMany(element => element.Elements())
                .Select(group => group.Name.LocalName == "group" ? (object)new { framework = (string?)group.Attribute("targetFramework") ?? "", dependencies = group.Elements().Select(Dependency).ToArray() } :
                    new { framework = "", dependencies = new[] { Dependency(group) } }).ToArray();
            foreach (ZipArchiveEntry entry in archive.Entries.Where(entry => entry.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
            {
                string binary = Path.Combine(outputRoot, $"assembly-{assemblyFacts.Count}.dll"), inspectionPath = Path.Combine(outputRoot, $"assembly-{assemblyFacts.Count}.json");
                using (Stream input = entry.Open())
                using (FileStream output = File.Create(binary)) { input.CopyTo(output); }
                AssemblyInspection.Write(binary, inspectionPath);
                JsonObject compiled = Read(inspectionPath);
                ProjectFact[] matches = projects.Where(project => Text(project.properties, "AssemblyName") == Text(compiled, "name")).ToArray();
                Require(matches.Length == 1, "Packed assembly has no unique evaluated project origin: " + Text(compiled, "name"));
                ProjectFact origin = matches[0];
                FileFact built = Reference(Text(origin.properties, "TargetPath"));
                Require(built.sha256 == Text(compiled, "sha256"), "Packed assembly differs from evaluated Release output: " + Text(compiled, "name"));
                assemblyFacts.Add(new { packageEntry = entry.FullName, origin.project, origin.role, targetFramework = Text(origin.properties, "TargetFramework"), builtAssembly = built, inspection = Reference(inspectionPath), facts = compiled });
            }
        }
        Require(assemblyFacts.Count > 0, "No compiled shipping assembly was inspected.");
        string[] inputNames = ["Directory.Build.props", "Directory.Packages.props", "global.json", "NuGet.config", "eng/project-boundaries.json", "eng/runtime-dependencies.json", "eng/development-dependencies.json", "eng/toolchain.json", ".config/dotnet-tools.json"];
        var report = new
        {
            schema = "cis.architecture-inspection/v1",
            sdk = sdk.Trim(),
            sourceCommit = (await Run(languageRoot, "git", ["rev-parse", "HEAD"])).Trim(),
            package = Reference(packagePath),
            inspectionInputs = inputNames.Select(name => Reference(Path.Combine(languageRoot, name))).ToArray(),
            inspector = Reference(typeof(ArchitectureInspection).Assembly.Location),
            projects,
            packageAssets,
            packageDependencyGroups = dependencyGroups,
            assemblies = assemblyFacts,
            levels = new[] { "evaluated-project-framework-references", "all-locked-target-graphs", "package-groups-and-assets", "compiled-references-and-public-api", "internal-type-namespace-uses" },
            scope = "Observed static architecture facts and locked-input consistency. Exact amended dependency allowlists execute here. Ownership, separation and comprehensive negative qualification remain WP07-S02-T03/T04. Not a full release qualification."
        };
        JsonObject observed = JsonSerializer.SerializeToNode(report)!.AsObject();
        observed["allowlists"] = ArchitecturePolicy.Assert(languageRoot, observed);
        File.WriteAllText(reportPath, observed.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Architecture inspection completed: {projects.Count} projects, {projects.Sum(project => project.targets.Count)} target graphs, {assemblyFacts.Count} packaged assemblies. Report: {reportPath}");
    }

    private static object Dependency(XElement element) => new { id = (string?)element.Attribute("id"), version = (string?)element.Attribute("version") };
    private static JsonObject Read(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    private static string Text(JsonObject value, string key) => value[key]?.GetValue<string>() ?? "";
    private static string[] Split(string value) => value.Split(';', StringSplitOptions.RemoveEmptyEntries);
    private static FileFact Reference(string path) => new(Path.GetFullPath(path), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));
    private static void Require(bool condition, string message) { if (!condition) { throw new InvalidDataException(message); } }

    private static Task<JsonObject> Evaluate(string root, string path, string framework = "", string rid = "", bool resolve = false)
    {
        var arguments = new List<string> { "msbuild", path, "-nologo", "-verbosity:quiet", "-p:Configuration=Release", "-p:BuildProjectReferences=false",
            "-getProperty:TargetFramework,TargetFrameworks,TargetPath,AssemblyName,PackageId,RootNamespace,IsPackable,RuntimeIdentifier,RuntimeIdentifiers,ProjectAssetsFile,NuGetLockFilePath,NETCoreSdkVersion,MSBuildBinPath",
            "-getItem:ProjectReference,PackageReference,FrameworkReference,Reference,ReferencePath" };
        if (framework.Length > 0) { arguments.Add("-p:TargetFramework=" + framework); }
        if (rid.Length > 0) { arguments.Add("-p:RuntimeIdentifier=" + rid); }
        if (resolve) { arguments.Add("-target:ResolveReferences"); }
        return EvaluateCore();
        async Task<JsonObject> EvaluateCore() => JsonNode.Parse(await Run(root, "dotnet", arguments))!.AsObject();
    }

    private static async Task<string> Run(string root, string executable, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(executable) { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        start.Environment["UseSharedCompilation"] = "false";
        start.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
        foreach (string argument in arguments) { start.ArgumentList.Add(argument); }
        using Process process = Process.Start(start) ?? throw new IOException("Could not start " + executable);
        Task<string> output = process.StandardOutput.ReadToEndAsync(), error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw new IOException("Inspection command timed out: " + executable); }
        await Task.WhenAll(output, error);
        Require(process.ExitCode == 0, executable + " failed: " + await output + await error);
        return await output;
    }

    private sealed record FileFact(string path, string sha256);
    private sealed record ProjectFact(string project, string role, FileFact input, JsonObject properties, JsonNode items, FileFact assets, FileFact @lock, List<TargetFact> targets, JsonNode restoreFrameworks);
    private sealed record TargetFact(string target, JsonNode properties, JsonNode items, List<object> packages, Dictionary<string, JsonNode?> lockedGraph, string[] restoredProjects, JsonObject graph);
}
