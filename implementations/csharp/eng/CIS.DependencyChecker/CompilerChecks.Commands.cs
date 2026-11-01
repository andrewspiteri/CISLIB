using System.Text;
using System.Text.Json.Nodes;

namespace CIS.Verification;

internal static partial class CompilerChecks
{
    private sealed record PreparedCompiler(string Workspace, string Compiler, string CompilerSha256,
        string ResponseFile, string ResponseSha256, string OutputAssembly, string Capture, SourceInventoryEntry[] GeneratedInputs);

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
            "-getProperty:NETCoreSdkVersion,MSBuildToolsPath,RoslynTargetsPath,CscToolPath,CscToolExe", "-nologo", "-v:quiet"];
        // CoreCompile must return a current command even if this output root
        // previously held a successful control assembly. This affects only the
        // target's incremental scheduling, not a compiler option.
        arguments.Add("-p:NonExistentFile=" + Path.Combine(workspace, ".capture-" + Guid.NewGuid().ToString("N")));
        if (reference is not null) { arguments.Add("-p:CompatibilityReferencePath=" + reference); }
        var result = await Run(language, arguments);
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
            ["scope"] = "One exact SDK-generated command and generated inputs; each existing source/reference/diagnostic control still executes a fresh real compiler.",
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
        return new PreparedCompiler(workspace, compiler, Hash(compiler), response, Hash(response), produced, capture, generated);

        string ArgumentPath(string prefix)
        {
            string value = compilerArguments.Single(argument => argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))[prefix.Length..].Trim('"');
            string path = Path.GetFullPath(value, workspace);
            Require(path.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspace)) + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal), "The prepared compiler output escapes its owned workspace.");
            return path;
        }
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
