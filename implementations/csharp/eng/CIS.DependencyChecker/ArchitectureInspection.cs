using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace CIS.Verification;

internal static class ArchitectureInspection
{
    internal static async Task Write(string languageRoot, string packagePath, string outputRoot, bool prepareProjectFacts = false)
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
        Require(discovered.Length == registered.Count, "ARCH-PROJECT-INVENTORY: Discovered project count differs from the registered inventory.");
        var projects = new List<ProjectFact>();
        foreach (string path in discovered)
        {
            string relative = Path.GetRelativePath(languageRoot, path).Replace('\\', '/');
            JsonObject[] matches = registered.OfType<JsonObject>().Where(item => Text(item, "path") == relative).ToArray();
            Require(matches.Length == 1, "ARCH-PROJECT-INVENTORY: Unregistered project: " + relative);
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
                        $"ARCH-LOCK-CONTENT: Lock/assets mismatch: {relative} {targetName} {identity}");
                    packages.Add(new { id, version, contentHash = lockedEntry!["contentHash"], lockType = lockedEntry["type"], assets = entry });
                }
                Require(effectiveLock.Count(item => item.Value!["type"]!.GetValue<string>() != "Project") == packages.Count, $"Locked package inventory mismatch: {relative} {targetName}");
                JsonObject context = await Evaluate(languageRoot, path, framework, rid);
                if (role is "shipping" or "build-tool"
                    || role == "process-fixture" && matches[0]["frameworkProvidedRuntimePackages"] is JsonArray { Count: > 0 })
                {
                    JsonObject resolved = await Evaluate(languageRoot, path, framework, rid, resolve: true);
                    context["Items"]!["ReferencePath"] = resolved["Items"]!["ReferencePath"]!.DeepClone();
                }
                targets.Add(new(targetName, context["Properties"]!, context["Items"]!, packages, effectiveLock,
                    graph.Where(item => item.Value!["type"]!.GetValue<string>() == "project").Select(item => item.Key).ToArray(), graph));
            }
            projects.Add(new(relative, role, Reference(path), properties, evaluation["Items"]!, Reference(assetsPath), Reference(lockPath), targets, assets["project"]!["frameworks"]!));
        }
        var phase = Stopwatch.StartNew();
        TraceStage("package-pe", "started", 0);
        JsonObject packageFacts = InspectPackage(packagePath, outputRoot, projects.Select(project => new PackageProject(project.project, project.role, project.properties)).ToArray());
        TraceStage("package-pe", "completed", phase.Elapsed.TotalSeconds);
        JsonObject? prepared = null;
        if (prepareProjectFacts)
        {
            phase.Restart();
            TraceStage("prepare-project-inputs", "started", 0);
            prepared = BindProjectInputs(languageRoot, projects);
            TraceStage("prepare-project-inputs", "completed", phase.Elapsed.TotalSeconds);
        }
        string[] inputNames = ["Directory.Build.props", "Directory.Packages.props", "global.json", "NuGet.config", "eng/project-boundaries.json", "eng/runtime-dependencies.json", "eng/development-dependencies.json", "eng/toolchain.json", ".config/dotnet-tools.json"];
        var report = new
        {
            schema = "cis.architecture-inspection/v1",
            sdk = sdk.Trim(),
            repositoryHeadCommit = HasRepositoryMetadata(languageRoot) ? (await Run(languageRoot, "git", ["rev-parse", "HEAD"])).Trim() : null,
            sourceIdentityScope = "Optional repository HEAD observation; source-only materializations have no Git metadata. Does not attest working-tree source identity. Package, inspected binaries and listed policy inputs are hashed separately; current preparation binds its explicit source inventory.",
            package = Reference(packagePath),
            inspectionInputs = inputNames.Select(name => Reference(Path.Combine(languageRoot, name))).ToArray(),
            inspector = Reference(typeof(ArchitectureInspection).Assembly.Location),
            preparedProjectFacts = prepared,
            projects,
            packageAssets = packageFacts["packageAssets"],
            packageDependencyGroups = packageFacts["packageDependencyGroups"],
            packageFrameworkReferences = packageFacts["packageFrameworkReferences"],
            assemblies = packageFacts["assemblies"],
            levels = new[] { "evaluated-project-framework-references", "all-locked-target-graphs", "package-groups-and-assets", "compiled-references-and-public-api", "internal-type-namespace-uses" },
            scope = "Observed static architecture facts and locked-input consistency. Exact amended dependency allowlists execute here. Ownership, separation and package guards execute here; isolated source/package/binary negative controls execute through the normal checker in WP07-S02-T04. Not a full release qualification."
        };
        JsonObject observed = JsonSerializer.SerializeToNode(report)!.AsObject();
        if (!prepareProjectFacts) { observed.Remove("preparedProjectFacts"); }
        phase.Restart();
        TraceStage("policies", "started", 0);
        observed["allowlists"] = ArchitecturePolicy.Assert(languageRoot, observed);
        observed["ownership"] = ArchitectureOwnershipPolicy.Assert(languageRoot, observed);
        TraceStage("policies", "completed", phase.Elapsed.TotalSeconds);
        File.WriteAllText(reportPath, observed.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Architecture inspection completed: {projects.Count} projects, {projects.Sum(project => project.targets.Count)} target graphs, {packageFacts["assemblies"]!.AsArray().Count} packaged assemblies. Report: {reportPath}");
    }

    // One passing native inspection supplies project/compiler facts. Reuse is
    // limited to the exact bound inputs, with only repository-root relocation.
    internal static void WritePrepared(string languageRoot, string packagePath, string outputRoot, string baselinePath, string baselineSha256)
    {
        languageRoot = Path.GetFullPath(languageRoot);
        packagePath = Path.GetFullPath(packagePath);
        outputRoot = Path.GetFullPath(outputRoot);
        Directory.CreateDirectory(outputRoot);
        string reportPath = Path.Combine(outputRoot, "inspection.json");
        File.Delete(reportPath);
        var phase = Stopwatch.StartNew();
        TraceStage("prepared-input-validation", "started", 0);
        Require(Reference(baselinePath).sha256 == baselineSha256, "Prepared architecture facts changed; run a fresh full inspection.");
        JsonObject baseline = Read(baselinePath);
        Require(Text(baseline, "schema") == "cis.architecture-inspection/v1" && (int?)baseline["allowlists"]?["violations"] == 0
            && (int?)baseline["ownership"]?["violations"] == 0 && baseline["projectFactsReused"] is null,
            "Prepared facts require an actual passing full architecture inspection.");
        Require(Text(baseline["inspector"]!, "sha256") == Reference(typeof(ArchitectureInspection).Assembly.Location).sha256,
            "Prepared facts belong to another checker binary; run a fresh full inspection.");
        JsonObject binding = baseline["preparedProjectFacts"]?.AsObject() ?? throw new InvalidDataException("The full inspection did not bind prepared project inputs.");
        string originalLanguage = Text(binding, "languageRoot"), originalRepo = RepositoryRoot(originalLanguage), currentRepo = RepositoryRoot(languageRoot);
        Require(JsonNode.DeepEquals(binding["environment"], EvaluationEnvironment()), "Project-evaluation environment changed; run a fresh full inspection.");
        Require(Text(binding, "runtime") == System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            "The inspection runtime changed; run a fresh full inspection.");
        ValidateInputs();
        TraceStage("prepared-input-validation", "completed", phase.Elapsed.TotalSeconds);
        JsonObject observed = baseline.DeepClone().AsObject();
        Rebase(observed["projects"]!, originalRepo, currentRepo);
        foreach (JsonObject project in observed["projects"]!.AsArray().OfType<JsonObject>())
        {
            // Logical asset paths relocate, so refresh their real raw-file pin.
            project["assets"] = JsonSerializer.SerializeToNode(Reference(Text(project["assets"]!, "path")));
        }
        phase.Restart();
        TraceStage("package-pe", "started", 0);
        JsonObject package = InspectPackage(packagePath, outputRoot, observed["projects"]!.AsArray().OfType<JsonObject>()
            .Select(project => new PackageProject(Text(project, "project"), Text(project, "role"), project["properties"]!)).ToArray());
        TraceStage("package-pe", "completed", phase.Elapsed.TotalSeconds);
        foreach ((string key, JsonNode? value) in package) { observed[key] = value?.DeepClone(); }
        observed["package"] = JsonSerializer.SerializeToNode(Reference(packagePath));
        observed["inspectionInputs"] = new JsonArray(baseline["inspectionInputs"]!.AsArray().Select(pin =>
        {
            string path = RebasePath(Text(pin!, "path"), originalRepo, currentRepo);
            return JsonSerializer.SerializeToNode(Reference(path));
        }).ToArray());
        observed["preparedFrom"] = JsonSerializer.SerializeToNode(Reference(baselinePath));
        observed["projectFactsReused"] = true;
        observed["sourceIdentityScope"] = "Project/compiler inputs match the passing prepared inspection under proven root relocation. Current package, packed PE and Release output bytes were re-read; repository HEAD remains the prepared origin's observation.";
        phase.Restart();
        TraceStage("policies", "started", 0);
        observed["allowlists"] = ArchitecturePolicy.Assert(languageRoot, observed);
        observed["ownership"] = ArchitectureOwnershipPolicy.Assert(languageRoot, observed);
        TraceStage("policies", "completed", phase.Elapsed.TotalSeconds);
        ValidateInputs();
        Require(Reference(baselinePath).sha256 == baselineSha256, "The immutable prepared inspection changed during inspection.");
        File.WriteAllText(reportPath, observed.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Architecture package/PE inspection completed using unchanged prepared project facts. Report: " + reportPath);

        void ValidateInputs()
        {
            string[] imported = binding["repositoryImports"]!.AsArray().Select(value => value!.GetValue<string>()).ToArray();
            Require(JsonNode.DeepEquals(binding["inputs"], ProjectInputPins(languageRoot, imported)),
                "Prepared project/configuration/lock/restored graph inputs are stale; run a fresh full inspection.");
            // Check the immutable origin and the SDK/import/compiler bytes too.
            Require(JsonNode.DeepEquals(binding["inputs"], ProjectInputPins(originalLanguage, imported)), "The immutable prepared input tree changed.");
            foreach (JsonObject pin in binding["externalInputs"]!.AsArray().OfType<JsonObject>())
            { Require(Reference(Text(pin, "path")).sha256 == Text(pin, "sha256"), "Prepared SDK/import/compiler reference changed: " + Text(pin, "path")); }
        }
    }

    private static JsonObject BindProjectInputs(string language, List<ProjectFact> projects)
    {
        string repository = RepositoryRoot(language);
        var external = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var imported = new HashSet<string>(StringComparer.Ordinal);
        foreach (ProjectFact project in projects)
        {
            foreach (JsonNode properties in project.targets.Select(target => target.properties).Prepend(project.properties))
            {
                foreach (string path in Split(Text(properties, "MSBuildAllProjects")).Append(Text(properties, "NETCoreSdkBundledVersionsProps")))
                {
                    if (!Path.IsPathFullyQualified(path)) { continue; }
                    if (Under(path, repository)) { imported.Add(Path.GetRelativePath(repository, path).Replace('\\', '/')); }
                    else { external.Add(path); }
                }
                string msbuild = Text(properties, "MSBuildBinPath");
                Require(Path.IsPathFullyQualified(msbuild), "Prepared facts require the actual evaluated MSBuild tool directory.");
                foreach (string name in new[] { "MSBuild.dll", "MSBuild.deps.json", "MSBuild.runtimeconfig.json",
                    "Roslyn/bincore/csc.dll", "Roslyn/bincore/csc.deps.json", "Roslyn/bincore/csc.runtimeconfig.json" })
                { external.Add(Path.Combine(msbuild, name)); }
                foreach (string path in Directory.EnumerateFiles(msbuild, "Microsoft.Build*.dll")
                    .Concat(Directory.EnumerateFiles(Path.Combine(msbuild, "Roslyn/bincore"), "Microsoft.CodeAnalysis*.dll")))
                { external.Add(path); }
            }
            foreach (TargetFact target in project.targets)
            {
                foreach (JsonObject reference in target.items["ReferencePath"]!.AsArray().OfType<JsonObject>())
                { string path = Text(reference, "Identity"); if (Path.IsPathFullyQualified(path) && !Under(path, repository)) { external.Add(path); } }
            }
        }
        // These are the actual evaluator/compiler binaries and current checker
        // runtime dependencies, not a claim to retain the whole SDK distribution.
        string checker = typeof(ArchitectureInspection).Assembly.Location;
        foreach (string path in new[] { checker, Path.ChangeExtension(checker, ".deps.json"), Path.ChangeExtension(checker, ".runtimeconfig.json"),
            typeof(CIS.TestSupport.ProcessRunner).Assembly.Location, typeof(object).Assembly.Location })
        { external.Add(path); }
        return new JsonObject
        {
            ["languageRoot"] = language,
            ["runtime"] = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            ["toolCurrencyScope"] = "Actual MSBuild/Microsoft.Build and Roslyn csc/CodeAnalysis files and manifests, loaded runtime CoreLib, checker DLL/manifests/TestSupport, evaluated imports and compiler references; not the complete SDK/runtime distribution.",
            ["environment"] = EvaluationEnvironment(),
            ["inputs"] = ProjectInputPins(language, imported),
            ["repositoryImports"] = JsonSerializer.SerializeToNode(imported.Order(StringComparer.Ordinal).ToArray()),
            ["externalInputs"] = JsonSerializer.SerializeToNode(external.Order(StringComparer.Ordinal).Select(Reference).ToArray())
        };
    }

    private static JsonObject EvaluationEnvironment() => new(Environment.GetEnvironmentVariables().Keys.Cast<string>()
        .Where(name => name is not ("CIS_ARCHITECTURE_WORK_COUNTERS" or "CIS_ARCHITECTURE_PHASE_TRACE" or "CIS_ARCHITECTURE_SCENARIO"))
        .Order(StringComparer.Ordinal).Select(name => new KeyValuePair<string, JsonNode?>(name, JsonValue.Create(Digest(Environment.GetEnvironmentVariable(name) ?? "")))));

    private static JsonArray ProjectInputPins(string language, IEnumerable<string> imports)
    {
        string repository = RepositoryRoot(language);
        var files = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        Walk(language);
        foreach (string relative in imports)
        {
            string path = Path.GetFullPath(relative, repository);
            Require(Under(path, repository), "Prepared repository imports must remain inside the same repository.");
            files.Add(path);
        }
        foreach (string root in new[] { language, Path.GetDirectoryName(language)!, repository })
        {
            foreach (string name in new[] { ".editorconfig", ".globalconfig", "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "global.json", "NuGet.config", "NuGet.Config" })
            { string path = Path.Combine(root, name); if (File.Exists(path)) { files.Add(path); } }
        }
        foreach (string relative in new[] { "eng/project-boundaries.json", "eng/runtime-dependencies.json", "eng/development-dependencies.json", "eng/toolchain.json", ".config/dotnet-tools.json" })
        { files.Add(Path.Combine(language, relative)); }
        return new JsonArray(files.Order(StringComparer.Ordinal).Select(path =>
        {
            string relative = Path.GetRelativePath(repository, path).Replace('\\', '/');
            bool graph = Path.GetFileName(path) is "project.assets.json" or "project.nuget.cache" || path.EndsWith(".nuget.dgspec.json", StringComparison.Ordinal);
            bool generated = path.EndsWith(".nuget.g.props", StringComparison.Ordinal) || path.EndsWith(".nuget.g.targets", StringComparison.Ordinal);
            string hash = graph ? Digest(Canonical(Read(path), repository).ToJsonString()) : generated
                ? Digest(File.ReadAllText(path).Replace(repository + Path.DirectorySeparatorChar, "$REPOSITORY" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                    .Replace(repository.Replace('\\', '/') + "/", "$REPOSITORY/", StringComparison.Ordinal)) : Reference(path).sha256;
            return (JsonNode)new JsonObject { ["path"] = relative, ["sha256"] = hash, ["kind"] = graph ? "logical-json" : generated ? "relocated-generated-msbuild" : "exact-bytes" };
        }).ToArray());

        void Walk(string directory)
        {
            Require((File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0, "Prepared inputs cannot contain linked directories.");
            foreach (string path in Directory.EnumerateFiles(directory))
            {
                string name = Path.GetFileName(path);
                if (Path.GetExtension(path) is ".csproj" or ".props" or ".targets" or ".rsp" || name is "project.assets.json" or "packages.lock.json" or "project.nuget.cache"
                    || name is ".editorconfig" or ".globalconfig" or "NuGet.config" or "NuGet.Config"
                    || name.EndsWith(".nuget.dgspec.json", StringComparison.Ordinal)) { files.Add(path); }
            }
            foreach (string child in Directory.EnumerateDirectories(directory))
            { if (Path.GetFileName(child) is not ("bin" or ".git" or ".vs" or ".artifacts" or "TestResults" or "StrykerOutput")) { Walk(child); } }
        }
    }

    private static JsonNode Canonical(JsonNode node, string repository) => node switch
    {
        JsonObject obj => new JsonObject(obj.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair =>
            new KeyValuePair<string, JsonNode?>(RebasePath(pair.Key, repository, "$REPOSITORY"), pair.Value is null ? null : Canonical(pair.Value, repository)))),
        JsonArray array => new JsonArray(array.Select(value => value is null ? null : Canonical(value, repository)).ToArray()),
        JsonValue value when value.TryGetValue(out string? text) && text is not null => JsonValue.Create(RebasePath(text, repository, "$REPOSITORY"))!,
        _ => node.DeepClone()
    };

    private static void Rebase(JsonNode node, string before, string after)
    {
        if (node is JsonObject obj)
        {
            foreach (string key in obj.Select(pair => pair.Key).ToArray())
            {
                if (obj[key] is JsonValue value && value.TryGetValue(out string? text) && text is not null) { obj[key] = RebasePath(text, before, after); }
                else if (obj[key] is JsonNode child) { Rebase(child, before, after); }
                string newKey = RebasePath(key, before, after);
                if (newKey != key) { JsonNode? entry = obj[key]; obj.Remove(key); obj.Add(newKey, entry); }
            }
        }
        else if (node is JsonArray array)
        {
            for (int index = 0; index < array.Count; index++)
            {
                if (array[index] is JsonValue value && value.TryGetValue(out string? text) && text is not null) { array[index] = RebasePath(text, before, after); }
                else if (array[index] is JsonNode child) { Rebase(child, before, after); }
            }
        }
    }

    private static string RepositoryRoot(string language) => Path.GetFullPath("../..", language);
    private static bool HasRepositoryMetadata(string language)
    {
        string path = Path.Combine(RepositoryRoot(language), ".git");
        return File.Exists(path) || Directory.Exists(path);
    }
    private static bool Under(string path, string root) => Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static string RebasePath(string text, string before, string after) => string.Join(';', text.Split(';').Select(part =>
        Path.IsPathFullyQualified(part) && Under(part, before) ? after == "$REPOSITORY" ? "$REPOSITORY/" + Path.GetRelativePath(before, part).Replace('\\', '/')
            : Path.GetFullPath(Path.GetRelativePath(before, part), after) : part));
    private static string Digest(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static JsonObject InspectPackage(string packagePath, string outputRoot, PackageProject[] projects)
    {
        PackageProject[] packable = projects.Where(project => Text(project.properties, "IsPackable").Equals("true", StringComparison.OrdinalIgnoreCase)).ToArray();
        Require(packable.Length == 1, "Expected the selected single packable Core project.");
        var assemblyFacts = new List<object>();
        var packageAssets = new List<object>();
        object[] dependencyGroups, frameworkReferences;
        using (ZipArchive archive = ZipFile.OpenRead(packagePath))
        {
            Require(archive.Entries.Select(entry => entry.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count() == archive.Entries.Count, "ARCH-PACKAGE-ASSET: Duplicate package asset paths.");
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
            frameworkReferences = metadata.Elements().Where(element => element.Name.LocalName == "frameworkReferences").SelectMany(element => element.Elements())
                .Select(group => (object)new
                {
                    targetFramework = group.Name.LocalName == "group" ? (string?)group.Attribute("targetFramework") ?? "" : "",
                    names = group.Elements().Select(element => element.Name.LocalName == "frameworkReference" ? (string?)element.Attribute("name") ?? "" : "").ToArray(),
                }).ToArray();
            foreach (ZipArchiveEntry entry in archive.Entries.Where(entry => entry.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
            {
                string binary = Path.Combine(outputRoot, $"assembly-{assemblyFacts.Count}.dll"), inspectionPath = Path.Combine(outputRoot, $"assembly-{assemblyFacts.Count}.json");
                using (Stream input = entry.Open())
                using (FileStream output = File.Create(binary)) { input.CopyTo(output); }
                AssemblyInspection.Write(binary, inspectionPath);
                JsonObject compiled = Read(inspectionPath);
                PackageProject[] matches = projects.Where(project => Text(project.properties, "AssemblyName") == Text(compiled, "name")).ToArray();
                Require(matches.Length == 1, "ARCH-PACKED-ORIGIN: Packed assembly has no unique evaluated project origin: " + Text(compiled, "name"));
                PackageProject origin = matches[0];
                FileFact built = Reference(Text(origin.properties, "TargetPath"));
                Require(built.sha256 == Text(compiled, "sha256"), "ARCH-PACKED-CONTENT: Packed assembly differs from evaluated Release output: " + Text(compiled, "name"));
                assemblyFacts.Add(new { packageEntry = entry.FullName, project = origin.project, role = origin.role, targetFramework = Text(origin.properties, "TargetFramework"), builtAssembly = built, inspection = Reference(inspectionPath), facts = compiled });
            }
        }
        Require(assemblyFacts.Count > 0, "No compiled shipping assembly was inspected.");
        return new JsonObject
        {
            ["packageAssets"] = JsonSerializer.SerializeToNode(packageAssets),
            ["packageDependencyGroups"] = JsonSerializer.SerializeToNode(dependencyGroups),
            ["packageFrameworkReferences"] = JsonSerializer.SerializeToNode(frameworkReferences),
            ["assemblies"] = JsonSerializer.SerializeToNode(assemblyFacts)
        };
    }
    private static object Dependency(XElement element) => new { id = (string?)element.Attribute("id"), version = (string?)element.Attribute("version") };
    private static JsonObject Read(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    private static string Text(JsonNode value, string key) => value[key]?.GetValue<string>() ?? "";
    private static string[] Split(string value) => value.Split(';', StringSplitOptions.RemoveEmptyEntries);
    private static FileFact Reference(string path) => new(Path.GetFullPath(path), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));
    private static void Require(bool condition, string message) { if (!condition) { throw new InvalidDataException(message); } }

    private static Task<JsonObject> Evaluate(string root, string path, string framework = "", string rid = "", bool resolve = false)
    {
        var arguments = new List<string> { "msbuild", path, "-nologo", "-verbosity:quiet", "-p:Configuration=Release", "-p:BuildProjectReferences=false",
            "-getProperty:TargetFramework,TargetFrameworks,TargetPath,AssemblyName,PackageId,RootNamespace,IsPackable,RuntimeIdentifier,RuntimeIdentifiers,ProjectAssetsFile,NuGetLockFilePath,MSBuildAllProjects,NETCoreSdkVersion,MSBuildBinPath,MSBuildSDKsPath,NETCoreSdkBundledVersionsProps,NetCoreRoot",
            "-getItem:ProjectReference,PackageReference,FrameworkReference,Reference,ReferencePath" };
        if (framework.Length > 0) { arguments.Add("-p:TargetFramework=" + framework); }
        if (rid.Length > 0) { arguments.Add("-p:RuntimeIdentifier=" + rid); }
        if (resolve) { arguments.Add("-target:ResolveReferences"); }
        return EvaluateCore();
        async Task<JsonObject> EvaluateCore()
        {
            var clock = Stopwatch.StartNew();
            TraceEvaluation("started", path, resolve, 0);
            try
            {
                JsonObject result = JsonNode.Parse(await Run(root, "dotnet", arguments))!.AsObject();
                if (Environment.GetEnvironmentVariable("CIS_ARCHITECTURE_WORK_COUNTERS") == "true") { Console.WriteLine("CIS-ARCH-MSBUILD-COMPLETED"); }
                TraceEvaluation("completed", path, resolve, clock.Elapsed.TotalSeconds);
                return result;
            }
            catch { TraceEvaluation("failed", path, resolve, clock.Elapsed.TotalSeconds); throw; }
        }
    }

    private static void TraceEvaluation(string outcome, string project, bool resolve, double seconds) =>
        TraceStage("msbuild-evaluation", outcome, seconds, project, resolve);

    private static void TraceStage(string phase, string outcome, double seconds, string? project = null, bool? resolve = null)
    {
        string? path = Environment.GetEnvironmentVariable("CIS_ARCHITECTURE_PHASE_TRACE");
        if (string.IsNullOrWhiteSpace(path)) { return; }
        try
        {
            if (File.Exists(path) && new FileInfo(path).Length >= 256 * 1024) { return; }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.AppendAllText(path, JsonSerializer.Serialize(new
            {
                schema = "cis.architecture-work-phase/v1",
                phase,
                outcome,
                project,
                resolve,
                seconds,
                scenario = Environment.GetEnvironmentVariable("CIS_ARCHITECTURE_SCENARIO"),
                utc = DateTimeOffset.UtcNow
            }) + "\n");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        { Console.Error.WriteLine("Architecture phase trace unavailable: " + error.Message); }
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
    private sealed record PackageProject(string project, string role, JsonNode properties);
    private sealed record ProjectFact(string project, string role, FileFact input, JsonObject properties, JsonNode items, FileFact assets, FileFact @lock, List<TargetFact> targets, JsonNode restoreFrameworks);
    private sealed record TargetFact(string target, JsonNode properties, JsonNode items, List<object> packages, Dictionary<string, JsonNode?> lockedGraph, string[] restoredProjects, JsonObject graph);
}
