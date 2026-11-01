using System.Collections;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.TestSupport;

namespace CIS.Verification;

internal static partial class CompilerChecks
{
    private sealed record PreparedCompiler(string Workspace, string Compiler, string CompilerSha256,
        string ResponseFile, string ResponseSha256, string OutputAssembly, string Capture, SourceInventoryEntry[] GeneratedInputs,
        JsonObject InputIdentity, JsonArray InputFiles);
    private sealed record CompilerReuse(string Root, JsonObject Report, JsonObject Inputs);
    private static readonly StringComparer CompilerPathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private const string CompilerInputsSchema = "cis.compiler-control-inputs/v1";

    // Capture the pinned SDK's complete real compiler command once for each
    // exact reference/consumer template. The SDK still supplies generated source,
    // defines, reference packs, analyzer configuration and analyzers. Each case
    // then runs a fresh compiler against its own actual source/reference bytes.
    private static async Task<PreparedCompiler> PrepareCompiler(string language, string output, JsonObject report,
        string kind, string workspace, string projectName, string logging, string? reference = null)
    {
        string project = Path.Combine(workspace, projectName);
        List<string> arguments = ["msbuild", project, "-restore", "-t:Compile", "-p:Configuration=Release",
            "-p:RestoreConfigFile=" + Path.Combine(language, "NuGet.config"), "-p:MicrosoftLoggingAbstractionsPath=" + logging,
            "-p:SkipCompilerExecution=true", "-p:ProvideCommandLineArgs=true", "-getTargetResult:CoreCompile",
            "-getProperty:NETCoreSdkVersion,MSBuildToolsPath,RoslynTargetsPath,CscToolPath,CscToolExe,MSBuildAllProjects", "-nologo", "-v:quiet"];
        // CoreCompile must return a current command even if this output root
        // previously held a successful control assembly. This affects only the
        // target's incremental scheduling, not a compiler option.
        arguments.Add("-p:NonExistentFile=" + Path.Combine(workspace, ".capture-" + Guid.NewGuid().ToString("N")));
        if (reference is not null) { arguments.Add("-p:CompatibilityReferencePath=" + reference); }
        var result = await Run(language, arguments);
        report["compilerPreparationCalls"] = ((int?)report["compilerPreparationCalls"] ?? 0) + 1;
        string capture = Path.Combine(workspace, "compiler-command.json");
        File.WriteAllText(capture, result.StandardOutput);
        File.WriteAllText(Path.Combine(workspace, "compiler-command.stderr.log"), result.StandardError);
        Require(result.ExitCode == 0 && result.OutputDrained, "Unable to prepare the exact SDK compiler command: " + capture);
        JsonObject prepared = JsonNode.Parse(result.StandardOutput)!.AsObject();
        JsonObject properties = prepared["Properties"]!.AsObject();
        Require(Text(properties, "NETCoreSdkVersion") == Text(Read(Path.Combine(language, "eng/toolchain.json")), "sdk"), "Pinned SDK mismatch.");
        Require(string.IsNullOrEmpty(Text(properties, "CscToolPath")) && string.IsNullOrEmpty(Text(properties, "CscToolExe")),
            "Compiler command preparation requires the pinned SDK's default managed C# compiler.");
        string compiler = Path.Combine(Text(properties, "RoslynTargetsPath"), "bincore/csc.dll");
        Require(File.Exists(compiler), "The selected SDK's managed C# compiler is missing.");
        JsonObject coreCompile = prepared["TargetResults"]!["CoreCompile"]!.AsObject();
        Require(Text(coreCompile, "Result") == "Success", "The SDK did not prepare its CoreCompile command successfully.");
        string[] compilerArguments = coreCompile["Items"]!.AsArray().Select(item => Text(item!, "Identity")).ToArray();
        Require(compilerArguments.Length > 0 && compilerArguments.All(argument => !argument.Contains('\n') && !argument.Contains('\r')),
            "The SDK produced an empty or multiline compiler argument.");
        Require(compilerArguments.Contains("/nullable:enable", StringComparer.Ordinal)
            && compilerArguments.Contains("/warnaserror+", StringComparer.Ordinal)
            && compilerArguments.Contains("/deterministic+", StringComparer.Ordinal), "The compiler command lost required nullable/warning/deterministic options.");
        string response = Path.Combine(workspace, "compiler.rsp");
        Require(compilerArguments.Count(argument => argument == "/noconfig") == 1, "The SDK compiler command must explicitly disable ambient compiler response configuration.");
        // Csc accepts /noconfig only on its command line, outside an @response.
        File.WriteAllText(response, string.Join(Environment.NewLine, compilerArguments.Where(argument => argument != "/noconfig")) + Environment.NewLine, new UTF8Encoding(false));
        string produced = ArgumentPath("/out:");
        foreach (string prefix in new[] { "/out:", "/refout:", "/pdb:" })
        {
            if (compilerArguments.Any(argument => argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            { Directory.CreateDirectory(Path.GetDirectoryName(ArgumentPath(prefix))!); }
        }
        SourceInventoryEntry[] generated = Directory.EnumerateFiles(Path.Combine(workspace, "obj"), "*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path) is ".cs" or ".editorconfig")
            .Order(StringComparer.Ordinal)
            .Select(path => new SourceInventoryEntry(Path.GetRelativePath(workspace, path), Hash(path))).ToArray();
        report["compilerPreparations"] ??= new JsonArray();
        report["compilerPreparations"]!.AsArray().Add(new JsonObject
        {
            ["kind"] = kind,
            ["scope"] = "Current exact SDK-generated command and inputs; the group summary distinguishes fresh compiler executions from explicitly reused original outcomes.",
            ["sdk"] = Text(properties, "NETCoreSdkVersion"),
            ["projectSha256"] = Hash(project),
            ["compiler"] = compiler,
            ["compilerSha256"] = Hash(compiler),
            ["argumentCount"] = compilerArguments.Length,
            ["generatedInputs"] = new JsonArray(generated.Select(input => (JsonNode)Reference(output,
                Path.Combine(workspace, input.Path), "csharp.generated-compiler-input/v1")).ToArray()),
            ["capture"] = Reference(output, capture, "msbuild.corecompile-command/v1"),
            ["responseFile"] = Reference(output, response, "csharp.compiler-response/v1")
        });
        var fixedFiles = new HashSet<string>(CompilerPathComparer) { project, compiler };
        foreach (string path in Text(properties, "MSBuildAllProjects").Split(';', StringSplitOptions.RemoveEmptyEntries))
        { fixedFiles.Add(Path.GetFullPath(path, workspace)); }
        foreach (string prefix in new[] { "/reference:", "/analyzer:", "/analyzerconfig:", "/additionalfile:" })
        {
            foreach (string argument in compilerArguments.Where(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                string path = Path.GetFullPath(argument[prefix.Length..].Trim('"'), workspace);
                // These reference assemblies are deliberate per-control inputs; their source/catalog and actual outputs are retained separately.
                if (!Inside(output, path)) { fixedFiles.Add(path); }
                else if (prefix is "/analyzer:" or "/analyzerconfig:" or "/additionalfile:") { fixedFiles.Add(path); }
            }
        }
        foreach (SourceInventoryEntry input in generated) { fixedFiles.Add(Path.Combine(workspace, input.Path)); }
        var inputFiles = new JsonArray(fixedFiles.Order(CompilerPathComparer).Select(path => (JsonNode)CompilerInput(output, path)).ToArray());
        var identity = new JsonObject
        {
            ["kind"] = kind,
            ["arguments"] = new JsonArray(compilerArguments.Select(value => (JsonNode)NormalizeCompilerPath(output, value)).ToArray()),
            ["files"] = new JsonArray(fixedFiles.Order(CompilerPathComparer).Select(path => (JsonNode)new JsonObject
            {
                ["path"] = NormalizeCompilerPath(output, path),
                ["sha256"] = Inside(output, path) && Path.GetExtension(path) is ".props" or ".targets" or ".editorconfig"
                    ? HashText(NormalizeCompilerPath(output, File.ReadAllText(path))) : Hash(path)
            }).ToArray())
        };
        return new PreparedCompiler(workspace, compiler, Hash(compiler), response, Hash(response), produced, capture, generated, identity, inputFiles);

        string ArgumentPath(string prefix)
        {
            string value = compilerArguments.Single(argument => argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))[prefix.Length..].Trim('"');
            string path = Path.GetFullPath(value, workspace);
            Require(path.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspace)) + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal), "The prepared compiler output escapes its owned workspace.");
            return path;
        }
    }

    private static bool Inside(string root, string path) => Path.GetFullPath(path).StartsWith(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string NormalizeCompilerPath(string output, string value) => value.Replace(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(output)), "<compiler-group>", StringComparison.Ordinal).Replace('\\', '/');

    private static string HashText(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static JsonObject CompilerInput(string output, string path) => new()
    {
        ["path"] = Inside(output, path) ? Path.GetRelativePath(output, path).Replace('\\', '/') : Path.GetFullPath(path),
        ["external"] = !Inside(output, path),
        ["sha256"] = Hash(path)
    };

    private static JsonObject CompilerEnvironment(string output)
    {
        var environment = new JsonObject();
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables().Cast<DictionaryEntry>().OrderBy(item => (string)item.Key, StringComparer.Ordinal))
        {
            string name = (string)entry.Key;
            if (name.ToUpperInvariant() is "PATH" or "PATHEXT" or "HOME" or "USERPROFILE" or "APPDATA" or "LOCALAPPDATA" or "TMP" or "TEMP" or "TMPDIR"
                || name.StartsWith("DOTNET_", StringComparison.OrdinalIgnoreCase) || name.StartsWith("MSBUILD", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("NUGET_", StringComparison.OrdinalIgnoreCase) || name.StartsWith("ROSLYN_", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("CSC_", StringComparison.OrdinalIgnoreCase))
            { environment[name] = HashText(NormalizeCompilerPath(output, (string)entry.Value!)); }
        }
        return environment;
    }

    private static JsonObject CompilerInputs(string language, string output, string group, JsonObject report, params PreparedCompiler[] compilers)
    {
        string repository = Path.GetFullPath(Path.Combine(language, "../.."));
        string sdk = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(compilers[0].Compiler)!, "../.."));
        string runtime = RuntimeEnvironment.GetRuntimeDirectory();
        string hostFxr = Path.Combine(Path.GetFullPath(Path.Combine(sdk, "../..")), "host/fxr");
        string[] sdkFiles = DependencyChecks.SdkInputFiles(sdk, static () => { });
        string[] runtimeFiles = Directory.GetFiles(runtime).Order(CompilerPathComparer).ToArray();
        string[] hostFxrFiles = Directory.GetFiles(hostFxr, "*", SearchOption.AllDirectories).Order(CompilerPathComparer).ToArray();
        string[] maintained = ["eng/CIS.DependencyChecker/CompilerChecks.cs", "eng/CIS.DependencyChecker/CompilerChecks.Commands.cs",
            "tests/CIS.Core.TestSupport/ProcessRunner.cs", "global.json", "NuGet.config", "eng/toolchain.json", "eng/runtime-dependencies.json"];
        var files = new HashSet<string>(sdkFiles.Concat(runtimeFiles).Concat(hostFxrFiles), CompilerPathComparer);
        foreach (string relative in maintained) { files.Add(Path.Combine(language, relative)); }
        files.Add(Environment.ProcessPath ?? throw new InvalidDataException("The compiler-control host path is unavailable."));
        files.Add(typeof(ProcessRunner).Assembly.Location);
        var common = new JsonArray(files.Order(CompilerPathComparer).Select(path => (JsonNode)CompilerInput(output, path)).ToArray());
        var externalDirectories = new JsonArray(compilers.SelectMany(compiler => compiler.InputFiles)
            .Where(input => (bool?)input!["external"] == true).Select(input => Path.GetDirectoryName(Text(input!, "path"))!)
            .Where(path => !Inside(sdk, path) && !CompilerPathComparer.Equals(sdk, path))
            .Distinct(CompilerPathComparer).Order(CompilerPathComparer).Select(path => (JsonNode)new JsonObject
            {
                ["path"] = path,
                ["members"] = new JsonArray(CompilerDirectoryMembers(path).Select(name => (JsonNode)name).ToArray())
            }).ToArray());
        var sources = report["source"]!.DeepClone().AsArray();
        // Samples compile the startup surface as well as their own sample catalog.
        if (group == "samples")
        {
            foreach (JsonNode? input in Inputs(language, Path.Combine(language, "verification/contracts/startup"), true))
            { sources.Add(input!.DeepClone()); }
        }
        var identity = new JsonObject
        {
            ["group"] = group,
            ["sources"] = sources,
            ["approvedCompileReference"] = report["approvedCompileReference"]!.DeepClone(),
            ["baselineSha256"] = report["baselineReference"]?["sha256"]?.DeepClone(),
            ["commonInputs"] = common.DeepClone(),
            ["environment"] = CompilerEnvironment(output),
            ["processHelperSha256"] = Hash(typeof(ProcessRunner).Assembly.Location),
            ["externalDirectories"] = externalDirectories,
            ["preparations"] = new JsonArray(compilers.Select(compiler => (JsonNode)compiler.InputIdentity.DeepClone()).ToArray())
        };
        return new JsonObject
        {
            ["schema"] = CompilerInputsSchema,
            ["repository"] = repository,
            ["identity"] = identity,
            ["files"] = new JsonArray(common.Concat(compilers.SelectMany(compiler => compiler.InputFiles)).Select(node => node!.DeepClone()).ToArray()),
            ["sdkDirectory"] = sdk,
            ["sdkMembers"] = new JsonArray(sdkFiles.Select(path => (JsonNode)Path.GetRelativePath(sdk, path).Replace('\\', '/')).ToArray()),
            ["runtimeDirectory"] = runtime,
            ["runtimeMembers"] = new JsonArray(runtimeFiles.Select(path => (JsonNode)Path.GetFileName(path)).ToArray()),
            ["hostFxrDirectory"] = hostFxr,
            ["hostFxrMembers"] = new JsonArray(hostFxrFiles.Select(path => (JsonNode)Path.GetRelativePath(hostFxr, path).Replace('\\', '/')).ToArray())
        };
    }

    private static void RecordCompilerInputs(string output, JsonObject report, JsonObject inputs)
    {
        Write(Path.Combine(output, "compiler-inputs.json"), inputs);
        report["compilerInputs"] = Reference(output, Path.Combine(output, "compiler-inputs.json"), CompilerInputsSchema);
    }

    // Both supported groups have a closed control inventory. This API is also used by the current regression receiver.
    internal static void ValidatePreparedGroup(string language, string output, string group)
    {
        Require(group is "startup" or "samples", "Only startup and sample compiler groups support reuse.");
        JsonObject report = Read(Path.Combine(output, "summary.json"));
        Require((string?)report["status"] == "Passed" && (int?)report["exitCode"] == 0, "The compiler group did not pass.");
        JsonObject inputs = Read(VerifiedReference(output, report["compilerInputs"] ?? throw new InvalidDataException("The compiler group lacks prospective dependency inputs.")));
        Require((string?)inputs["identity"]?["group"] == group, "The compiler dependency group differs.");
        Require((string?)inputs["identity"]?["processHelperSha256"] == Hash(typeof(ProcessRunner).Assembly.Location), "The current compiler process helper differs.");
        ValidateCompilerInputFiles(language, output, inputs);
        JsonObject payload = Read(VerifiedReference(output, report["compilerOutputs"] ?? throw new InvalidDataException("The compiler group lacks retained output identities.")));
        ValidateCompilerPayload(output, payload);
        (string Id, string? Diagnostic)[] expected = ExpectedCompilerControls(language, group);
        JsonArray controls = report["controls"]!.AsArray();
        Require(controls.Count == expected.Length && controls.Count == (group == "startup" ? 25 : 22), "The compiler control inventory is incomplete.");
        for (int index = 0; index < expected.Length; index++)
        {
            JsonNode control = controls[index]!;
            string? diagnostic = (string?)control["expectedDiagnostic"];
            Require(Text(control, "id") == expected[index].Id && diagnostic == expected[index].Diagnostic && (bool?)control["matched"] == true
                && Matches(control["actualExitCode"]!.GetValue<int>(), File.ReadAllText(VerifiedReference(output, control["log"]!)), diagnostic),
                "A retained native compiler control differs from its actual required outcome.");
        }
        if ((bool?)report["compilerReuse"]?["reused"] == true)
        {
            string origin = VerifiedReference(output, report["compilerReuse"]!["originalSummary"]!);
            string originRoot = Path.GetDirectoryName(origin)!;
            JsonObject original = Read(origin);
            ValidateCompilerPayload(originRoot, Read(VerifiedReference(originRoot, original["compilerOutputs"]!)));
            Require((string?)original["status"] == "Passed" && (int?)original["exitCode"] == 0
                && (bool?)original["compilerReuse"]?["reused"] == false
                && (int?)report["compilerReuse"]?["compilerCallsThisPreparation"] == 0
                && (int?)report["compilerReuse"]?["reusedCompilerControls"] == expected.Length
                && CompilerReuseMatches(Read(VerifiedReference(originRoot, original["compilerInputs"]!)), inputs)
                && JsonNode.DeepEquals(RewriteCompilerReferences(original["controls"]!, "retained/"), report["controls"]),
                "Reused compiler outcomes differ from their retained original execution.");
        }
        else
        {
            Require((bool?)report["compilerReuse"]?["reused"] == false
                && (int?)report["compilerReuse"]?["compilerCallsThisPreparation"] == expected.Length
                && (int?)report["compilerReuse"]?["reusedCompilerControls"] == 0, "Fresh compiler execution accounting is missing or inconsistent.");
        }
    }

    private static (string Id, string? Diagnostic)[] ExpectedCompilerControls(string language, string group)
    {
        var expected = new List<(string Id, string? Diagnostic)>();
        if (group == "startup")
        {
            foreach (string id in new[] { "reference-only-specification", "positive-consumer", "event-consumer", "runtime-consumer", "provider-consumer" }) { expected.Add((id, null)); }
            foreach (JsonNode? item in JsonNode.Parse(File.ReadAllText(Path.Combine(language, "verification/contracts/startup/negative-cases.json")))!.AsArray())
            { expected.Add((Text(item!, "id"), Text(item!, "expectedDiagnostic"))); }
        }
        else
        {
            JsonObject catalog = Read(Path.Combine(language, "verification/compatibility/samples/catalog.json"));
            foreach (JsonNode? item in catalog["sources"]!.AsArray()) { expected.Add((Text(item!, "id"), null)); }
            foreach (JsonNode? item in catalog["apiSamples"]!.AsArray())
            { expected.Add((Text(item!, "id") + "-reference", null)); expected.Add((Text(item!, "id"), Text(item!, "expectedDiagnostic"))); }
        }
        return expected.ToArray();
    }

    internal static void ValidateCompilerInputFiles(string language, string output, JsonObject inputs)
    {
        Require((string?)inputs["schema"] == CompilerInputsSchema, "Unsupported compiler input identity.");
        JsonObject identity = inputs["identity"]!.AsObject();
        string group = Text(identity, "group");
        JsonArray sources = Inputs(language, Path.Combine(language, group == "startup" ? "verification/contracts/startup" : "verification/compatibility/samples"), true);
        if (group == "samples")
        {
            foreach (JsonNode? input in Inputs(language, Path.Combine(language, "verification/contracts/startup"), true)) { sources.Add(input!.DeepClone()); }
        }
        Require(group is "startup" or "samples" && JsonNode.DeepEquals(sources, identity["sources"]), "Compiler source/catalog/template inputs changed.");
        foreach (JsonNode? input in inputs["files"]!.AsArray())
        {
            string path = Text(input!, "path");
            if ((bool?)input!["external"] != true)
            {
                path = Path.GetFullPath(path, output);
                Require(Inside(output, path), "A compiler input escapes its owned group.");
            }
            else { Require(Path.IsPathFullyQualified(path), "An external compiler input must have an absolute path."); }
            Require(Hash(path) == Text(input!, "sha256"), "A compiler import, tool, reference, analyzer or generated input changed: " + path);
        }
        string sdk = Text(inputs, "sdkDirectory"), runtime = Text(inputs, "runtimeDirectory");
        Require(Sequence(inputs["sdkMembers"]!).SequenceEqual(DependencyChecks.SdkInputFiles(sdk, static () => { }).Select(path => Path.GetRelativePath(sdk, path).Replace('\\', '/')), StringComparer.Ordinal),
            "Pinned SDK compiler/import/analyzer membership changed.");
        Require(Sequence(inputs["runtimeMembers"]!).SequenceEqual(Directory.GetFiles(runtime).Order(CompilerPathComparer).Select(Path.GetFileName), StringComparer.Ordinal), "Compiler host runtime membership changed.");
        string hostFxr = Text(inputs, "hostFxrDirectory");
        Require(Sequence(inputs["hostFxrMembers"]!).SequenceEqual(Directory.GetFiles(hostFxr, "*", SearchOption.AllDirectories).Order(CompilerPathComparer)
            .Select(path => Path.GetRelativePath(hostFxr, path).Replace('\\', '/')), StringComparer.Ordinal), "Compiler host resolver membership changed.");
        foreach (JsonNode? directory in identity["externalDirectories"]!.AsArray())
        {
            Require(Sequence(directory!["members"]!).SequenceEqual(CompilerDirectoryMembers(Text(directory, "path")), StringComparer.Ordinal),
                "Compiler reference/analyzer/import directory membership changed.");
        }
    }

    private static string[] CompilerDirectoryMembers(string directory) => Directory.GetFiles(directory)
        .Where(path => Path.GetExtension(path).ToLowerInvariant() is ".dll" or ".exe" or ".json" or ".props" or ".targets" or ".rsp" or ".tasks" or ".overridetasks" or ".editorconfig" or ".ruleset")
        .Select(path => Path.GetFileName(path)).Order(StringComparer.Ordinal).ToArray();

    internal static bool CompilerReuseMatches(JsonObject recorded, JsonObject current) =>
        (string?)recorded["schema"] == CompilerInputsSchema && (string?)current["schema"] == CompilerInputsSchema
        && JsonNode.DeepEquals(recorded["identity"], current["identity"]);

    private static CompilerReuse? TryReadCompilerReuse(string language, string output, string? prior, string group, JsonObject current)
    {
        if (prior is null) { return null; }
        Require(!Inside(prior, output) && !Inside(output, prior) && !CompilerPathComparer.Equals(output, prior), "Compiler reuse needs separate prior and current roots.");
        try
        {
            ValidatePreparedGroup(language, prior, group);
            JsonObject report = Read(Path.Combine(prior, "summary.json"));
            string root = (bool?)report["compilerReuse"]?["reused"] == true
                ? Path.GetDirectoryName(VerifiedReference(prior, report["compilerReuse"]!["originalSummary"]!))! : prior;
            ValidatePreparedGroup(language, root, group);
            report = Read(Path.Combine(root, "summary.json"));
            JsonObject inputs = Read(VerifiedReference(root, report["compilerInputs"]!));
            if (!JsonNode.DeepEquals(current["source"], report["source"]) || !JsonNode.DeepEquals(current["approvedCompileReference"], report["approvedCompileReference"])) { return null; }
            return new(root, report, inputs);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or InvalidOperationException or ArgumentException)
        { return null; } // Legacy, missing and stale evidence executes the unchanged fresh controls.
    }

    private static bool TryReuseCompilerControls(string output, JsonObject report, JsonObject inputs, CompilerReuse reuse)
    {
        if (!CompilerReuseMatches(reuse.Inputs, inputs)) { return false; }
        JsonObject payload = Read(VerifiedReference(reuse.Root, reuse.Report["compilerOutputs"]!));
        ValidateCompilerPayload(reuse.Root, payload);
        string retained = Path.Combine(output, "retained");
        Require(!Directory.Exists(retained), "Use a fresh retained compiler payload directory.");
        Directory.CreateDirectory(retained);
        foreach (string relative in Sequence(payload["directories"]!)) { Directory.CreateDirectory(OwnedCompilerPath(retained, relative)); }
        foreach (JsonNode? file in payload["files"]!.AsArray())
        {
            string relative = Text(file!, "path"), destination = OwnedCompilerPath(retained, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(VerifiedReference(reuse.Root, file!), destination);
        }
        File.Copy(Path.Combine(reuse.Root, "compiler-outputs.json"), Path.Combine(retained, "compiler-outputs.json"));
        File.Copy(Path.Combine(reuse.Root, "summary.json"), Path.Combine(retained, "summary.json"));
        ValidateCompilerPayload(retained, payload);
        foreach (string field in new[] { "controls", "referenceAssembly", "baselineReference", "mutations" })
        { if (reuse.Report[field] is JsonNode value) { report[field] = RewriteCompilerReferences(value, "retained/"); } }
        report["compilerReuse"] = new JsonObject
        {
            ["reused"] = true,
            ["compilerCallsThisPreparation"] = 0,
            ["reusedCompilerControls"] = reuse.Report["controls"]!.AsArray().Count,
            ["originalSummary"] = Reference(output, Path.Combine(retained, "summary.json"), Text(reuse.Report, "schema")),
            ["scope"] = "Original native compiler outcomes retained unchanged; two current SDK command preparations and current dependency validation, not new compiler executions."
        };
        RecordCompilerInputs(output, report, inputs);
        return true;
    }

    private static JsonNode RewriteCompilerReferences(JsonNode value, string prefix)
    {
        JsonNode clone = value.DeepClone();
        Visit(clone); return clone;
        void Visit(JsonNode node)
        {
            if (node is JsonObject obj)
            {
                if (obj["path"] is JsonValue && obj["sha256"] is JsonValue && obj["schema"] is JsonValue)
                { obj["path"] = prefix + Text(obj, "path"); }
                foreach (JsonNode? child in obj.Select(pair => pair.Value).ToArray()) { if (child is not null) { Visit(child); } }
            }
            else if (node is JsonArray array) { foreach (JsonNode? child in array) { if (child is not null) { Visit(child); } } }
        }
    }

    private static void RecordCompilerOutputs(string language, string output, JsonObject report)
    {
        ValidateCompilerInputFiles(language, output, Read(VerifiedReference(output, report["compilerInputs"]!)));
        report["compilerReuse"] ??= new JsonObject { ["reused"] = false, ["compilerCallsThisPreparation"] = report["controls"]!.AsArray().Count, ["reusedCompilerControls"] = 0 };
        var payload = new JsonObject
        {
            ["schema"] = "cis.compiler-control-outputs/v1",
            ["files"] = new JsonArray(CompilerPayloadFiles(output).Select(path => (JsonNode)Reference(output, path, "compiler-control-payload/v1")).ToArray()),
            ["directories"] = new JsonArray(Directory.GetDirectories(output, "*", SearchOption.AllDirectories).Select(path => (JsonNode)Path.GetRelativePath(output, path).Replace('\\', '/')).OrderBy(node => node.GetValue<string>(), StringComparer.Ordinal).ToArray())
        };
        Write(Path.Combine(output, "compiler-outputs.json"), payload);
        report["compilerOutputs"] = Reference(output, Path.Combine(output, "compiler-outputs.json"), "cis.compiler-control-outputs/v1");
    }

    private static IEnumerable<string> CompilerPayloadFiles(string output)
    {
        string[] files = Directory.GetFiles(output, "*", SearchOption.AllDirectories);
        Require(files.Concat(Directory.GetDirectories(output, "*", SearchOption.AllDirectories))
            .All(path => (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0), "A retained compiler payload contains a linked member.");
        return files.Where(path => path != Path.Combine(output, "summary.json") && path != Path.Combine(output, "compiler-outputs.json")).Order(StringComparer.Ordinal);
    }

    private static string[] Sequence(JsonNode node) => node.AsArray().Select(item => item!.GetValue<string>()).ToArray();

    private static string OwnedCompilerPath(string output, string relative)
    {
        Require(!Path.IsPathRooted(relative), "A retained compiler payload path must be relative.");
        string path = Path.GetFullPath(relative, output);
        Require(Inside(output, path), "A retained compiler payload escapes its group.");
        return path;
    }

    internal static void ValidateCompilerPayload(string output, JsonObject payload)
    {
        Require((string?)payload["schema"] == "cis.compiler-control-outputs/v1", "Unsupported compiler output identity.");
        JsonArray files = payload["files"]!.AsArray();
        foreach (JsonNode? file in files) { _ = VerifiedReference(output, file!); }
        string[] actual = CompilerPayloadFiles(output).Select(path => Path.GetRelativePath(output, path).Replace('\\', '/')).ToArray();
        Require(files.Select(file => Text(file!, "path")).SequenceEqual(actual, StringComparer.Ordinal), "Retained compiler payload membership changed.");
        string[] directories = Directory.GetDirectories(output, "*", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(output, path).Replace('\\', '/')).Order(StringComparer.Ordinal).ToArray();
        Require(Sequence(payload["directories"]!).SequenceEqual(directories, StringComparer.Ordinal), "Retained compiler directory membership changed.");
    }

    private static async Task CompilePrepared(string language, string output, JsonObject report, PreparedCompiler compiler,
        string id, string directory, string[] sources, string? diagnostic = null, string? reference = null)
    {
        Require(Hash(compiler.Compiler) == compiler.CompilerSha256 && Hash(compiler.ResponseFile) == compiler.ResponseSha256,
            "The prepared compiler or exact SDK argument file changed.");
        Require(compiler.GeneratedInputs.All(input => Hash(Path.Combine(compiler.Workspace, input.Path)) == input.Sha256),
            "An SDK-generated source or analyzer configuration input changed.");
        var sourcePins = new JsonArray();
        foreach (string source in sources)
        {
            string original = Path.Combine(directory, source), staged = Path.Combine(compiler.Workspace, source);
            File.Copy(original, staged, true);
            Require(Hash(original) == Hash(staged), "A compiler control's actual source differs from its staged compiler input.");
            sourcePins.Add(Reference(output, original, "csharp.source/v1"));
        }
        // An expected failure must never inherit a preceding successful output.
        File.Delete(compiler.OutputAssembly);
        JsonObject entry = await Compile(language, output, report, id, directory,
            [compiler.Compiler, "/noconfig", "@" + compiler.ResponseFile], diagnostic, workingDirectory: compiler.Workspace);
        entry["compilerCommand"] = Reference(output, compiler.Capture, "msbuild.corecompile-command/v1");
        entry["sourceInputs"] = sourcePins;
        if (reference is not null) { entry["referenceAssembly"] = Reference(output, reference, "dotnet.reference-assembly/v1"); }
        if (entry["actualExitCode"]!.GetValue<int>() == 0)
        {
            Require(File.Exists(compiler.OutputAssembly), "Successful compilation did not produce the actual control assembly.");
            string destination = Path.Combine(directory, "bin/Release/net10.0", Path.GetFileName(compiler.OutputAssembly));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(compiler.OutputAssembly, destination, true);
            // Startup's SDK consumer hint path resolves this sibling reference.
            string staged = Path.Combine(compiler.Workspace, "bin/Release/net10.0", Path.GetFileName(compiler.OutputAssembly));
            Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
            File.Copy(compiler.OutputAssembly, staged, true);
            entry["assembly"] = Reference(output, destination, "dotnet.compiler-control/v1");
        }
        foreach (string source in sources)
        { Require(Hash(Path.Combine(directory, source)) == Hash(Path.Combine(compiler.Workspace, source)), "Compiler source inputs changed during execution."); }
        Require(Hash(compiler.Compiler) == compiler.CompilerSha256 && Hash(compiler.ResponseFile) == compiler.ResponseSha256,
            "The prepared compiler or exact SDK argument file changed during execution.");
        Require(compiler.GeneratedInputs.All(input => Hash(Path.Combine(compiler.Workspace, input.Path)) == input.Sha256),
            "An SDK-generated source or analyzer configuration input changed during execution.");
    }
}
