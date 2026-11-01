using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using CIS.TestSupport;
using static CIS.Verification.TypeManagerVerificationInputs;

namespace CIS.Verification;

// One prepared candidate; every required case executes freshly in a Fast or residual host.
internal static class RegressionTests
{
    private sealed class NativeDeadlineException() : TimeoutException("The shared native regression deadline expired before this project could launch.") { }
    private const int NativeTargetSeconds = 300;
    internal const string Filter = "TestCategory!=PerformanceObservation&TestCategory!=ExtendedStress";
    private static readonly string[] Projects =
    [
        "CIS.Core.UnitTests", "CIS.Core.IntegrationTests", "CIS.Core.ArchitectureTests",
        "CIS.Core.CompatibilityTests", "CIS.Verification.Tests", "CIS.Core.BusinessCases"
    ];
    internal static int MethodWorkers(string project) => FastTests.Projects.Contains(project, StringComparer.Ordinal)
        ? FastTests.Workers(project) : project == "CIS.Core.IntegrationTests" ? 8 : 4;

    internal static string WriteSettings(string language, string directory, string project)
    {
        if (FastTests.Projects.Contains(project, StringComparer.Ordinal)) { return FastTests.WriteSettings(language, directory, project); }
        string canonical = Path.Combine(language, "verification/regression.runsettings");
        if (project != "CIS.Core.IntegrationTests") { return canonical; }

        byte[] original = File.ReadAllBytes(canonical);
        var utf8 = new System.Text.UTF8Encoding(false, true);
        string text = utf8.GetString(original);
        XElement? parallel = XDocument.Load(canonical).Root?.Element("MSTest")?.Element("Parallelize");
        XElement[] workers = parallel?.Elements("Workers").ToArray() ?? [];
        Require(workers.Length == 1 && workers[0].Value == "4" && (string?)parallel?.Element("Scope") == "MethodLevel",
            "Integration settings require the canonical four-worker MethodLevel profile.");
        const string before = "<Workers>4</Workers>", after = "<Workers>8</Workers>";
        int at = text.IndexOf(before, StringComparison.Ordinal);
        Require(at >= 0 && text.IndexOf(before, at + before.Length, StringComparison.Ordinal) < 0,
            "Integration settings require one exact canonical worker element.");
        string changed = text.Replace(before, after, StringComparison.Ordinal);
        Require(changed.Replace(after, before, StringComparison.Ordinal) == text,
            "Integration settings may change only the canonical worker value.");
        string path = Path.Combine(directory, "integration.runsettings");
        File.WriteAllBytes(path, utf8.GetBytes(changed));
        Require(File.ReadAllBytes(canonical).SequenceEqual(original), "Canonical regression settings changed while copying the Integration profile.");
        return path;
    }

    private const string NativeInputsSchema = "cis.prepared-regression-native-inputs/v1";
    private static JsonObject OrdinaryBuildProperties() => new()
    {
        ["CisMutationTesting"] = "false",
        ["CisHealthcheckMutationCohort"] = "false",
        ["CisHealthcheckPilotCohort"] = "false",
        ["CisLinuxIdentityQualification"] = "false"
    };
    internal static readonly string[] NativeInputRoots = Projects.Select(project => $"implementations/csharp/tests/{project}/bin/Release")
        .Concat(["implementations/csharp/src/CIS.Core/bin/Release", "implementations/csharp/eng/CIS.DependencyChecker/bin/Release",
            "implementations/csharp/tests/CIS.Core.TestSupport/bin/Release"]).ToArray();

    internal static int CapturePreparedNativeInputs(string[] args)
    {
        var input = new TypeManagerVerificationInputs(args, ["repo-root", "source-inputs", "output-path"], ["include-fast"]);
        string repo = input.PathOf("repo-root"), source = input.PathOf("source-inputs"), output = input.PathOf("output-path");
        Require(!File.Exists(output) && !Directory.Exists(output), "Use a new prepared native-input manifest path.");
        byte[] sourceBytes = File.ReadAllBytes(source);
        string sourceSha = Digest(sourceBytes);
        SourceInventoryEntry[] sources = JsonSerializer.Deserialize<SourceInventoryEntry[]>(sourceBytes)!;
        Require(sources.Length > 0, "Preparation contains no maintained source inputs.");
        CheckMembership(repo, sources);
        SourceReplay.AssertInputs(repo, sources);
        SourceInventoryEntry[] files = CaptureNativeInputs(repo);
        CheckMembership(repo, sources);
        SourceReplay.AssertInputs(repo, sources);
        Require(Hash(source) == sourceSha, "Prepared source input manifest changed during native capture.");
        var manifest = new JsonObject
        {
            ["schema"] = NativeInputsSchema,
            ["configuration"] = "Release",
            ["buildProperties"] = OrdinaryBuildProperties(),
            ["sourceInventorySha256"] = sourceSha,
            ["roots"] = JsonSerializer.SerializeToNode(NativeInputRoots),
            ["files"] = JsonSerializer.SerializeToNode(files)
        };
        Require(bool.TryParse(input.Optional("include-fast") ?? "false", out bool includeFast), "include-fast must be true or false.");
        if (includeFast)
        {
            manifest["fastInputs"] = new JsonObject
            {
                ["sourceInventorySha256"] = sourceSha,
                ["roots"] = JsonSerializer.SerializeToNode(FastInputRoots),
                ["files"] = JsonSerializer.SerializeToNode(CaptureNativeInputs(repo, FastInputRoots))
            };
        }
        CheckMembership(repo, sources);
        SourceReplay.AssertInputs(repo, sources);
        Require(Hash(source) == sourceSha, "Prepared source input manifest changed during Fast capture.");
        WriteJson(output, manifest);
        return 0;
    }

    internal static SourceInventoryEntry[] VerifyPreparedNativeInputs(string repo, string path, string sourceManifest)
    {
        JsonObject manifest = Read(path);
        Require((string?)manifest["schema"] == NativeInputsSchema && (string?)manifest["configuration"] == "Release" &&
            JsonNode.DeepEquals(manifest["buildProperties"], OrdinaryBuildProperties()) &&
            (string?)manifest["sourceInventorySha256"] == Hash(sourceManifest), "Prepared native inputs identify another source inventory, configuration or test cohort.");
        Require(manifest["roots"] is JsonArray && manifest["files"] is JsonArray, "Prepared native input inventory is incomplete.");
        Require(manifest["roots"]!.Deserialize<string[]>()!.SequenceEqual(NativeInputRoots, StringComparer.Ordinal),
            "Prepared native inputs must cover all six test outputs and Core/checker/test-support outputs.");
        SourceInventoryEntry[] prepared = manifest["files"]!.Deserialize<SourceInventoryEntry[]>()!;
        Require(prepared.Length > 0 && prepared.SequenceEqual(CaptureNativeInputs(repo)),
            "Prepared native output membership or bytes changed; prepare the current candidate again before regression.");
        return prepared;
    }

    internal static readonly string[] FastInputRoots = FastTests.Projects.Select(project => $"implementations/csharp/tests/{project}/bin/Release").ToArray();

    internal static SourceInventoryEntry[] VerifyPreparedFastInputs(string repo, string path, string sourceManifest)
    {
        JsonObject manifest = Read(path);
        JsonObject fast = manifest["fastInputs"] as JsonObject ?? throw new InvalidDataException("Prepare the current Fast outputs before the split regression route.");
        Require((string?)fast["sourceInventorySha256"] == Hash(sourceManifest) &&
            fast["roots"]!.Deserialize<string[]>()!.SequenceEqual(FastInputRoots, StringComparer.Ordinal), "Prepared Fast roots/source differ.");
        SourceInventoryEntry[] files = fast["files"]!.Deserialize<SourceInventoryEntry[]>()!;
        Require(files.Length > 0 && files.SequenceEqual(CaptureNativeInputs(repo, FastInputRoots)), "Prepared Fast membership or bytes changed.");
        return files;
    }

    private static SourceInventoryEntry[] CaptureNativeInputs(string repo) => CaptureNativeInputs(repo, NativeInputRoots);

    private static SourceInventoryEntry[] CaptureNativeInputs(string repo, string[] roots)
    {
        var files = new List<string>();
        foreach (string relative in roots)
        {
            string directory = repo;
            foreach (string part in relative.Split('/'))
            {
                directory = Path.Combine(directory, part);
                Require((File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0, "Prepared native inputs cannot traverse a linked directory: " + directory);
            }
            Require(Directory.Exists(directory), "Missing prepared native output root: " + relative);
            string project = relative.Split('/')[3];
            Require(File.Exists(Path.Combine(directory, "net10.0", project + ".dll")), "Missing prepared native assembly: " + project);
            Walk(directory);
        }
        return files.Select(path => new SourceInventoryEntry(Path.GetRelativePath(repo, path).Replace('\\', '/'), Hash(path)))
            .OrderBy(pin => pin.Path, StringComparer.Ordinal).ToArray();

        void Walk(string directory)
        {
            foreach (string path in Directory.EnumerateFileSystemEntries(directory))
            {
                FileAttributes attributes = File.GetAttributes(path);
                Require((attributes & FileAttributes.ReparsePoint) == 0, "Prepared native inputs cannot contain a link: " + path);
                if ((attributes & FileAttributes.Directory) != 0) { Walk(path); }
                else { files.Add(path); }
            }
        }
    }

    internal const string SchedulingMethod = "CIS.Tests.TestAssemblySetup.SchedulingMatchesRequestedMode";
    internal sealed record TestMethodShape(string Type, string Name, string Signature, string Attributes, string TypeAttributes, bool CanPartition = true)
    {
        internal string Method => Type + "." + Name;
    }

    // Inspect metadata without loading test assemblies or executing discovery/product code.
    internal static TestMethodShape[] ReadTestMethods(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        var names = new SignatureNames();
        var methods = new List<TestMethodShape>();
        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
        {
            TypeDefinition type = reader.GetTypeDefinition(handle);
            if (!type.GetCustomAttributes().Any(attribute => AttributeType(reader, reader.GetCustomAttribute(attribute).Constructor).EndsWith(".TestClassAttribute", StringComparison.Ordinal))) { continue; }
            string typeName = names.GetTypeFromDefinition(reader, handle, 0);
            string typeAttributes = Attributes(type.GetCustomAttributes());
            bool ordinaryType = type.BaseType.Kind == HandleKind.TypeReference &&
                names.GetTypeFromReference(reader, (TypeReferenceHandle)type.BaseType, 0) == "System.Object";
            foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
            {
                MethodDefinition method = reader.GetMethodDefinition(methodHandle);
                if (!method.GetCustomAttributes().Any(attribute => AttributeType(reader, reader.GetCustomAttribute(attribute).Constructor) is
                    "Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute" or "Microsoft.VisualStudio.TestTools.UnitTesting.DataTestMethodAttribute")) { continue; }
                methods.Add(new(typeName, reader.GetString(method.Name), Signature(method.DecodeSignature(names, (object?)null)),
                    Attributes(method.GetCustomAttributes()), typeAttributes, ordinaryType));
            }
        }
        return methods.ToArray();

        string Attributes(CustomAttributeHandleCollection handles) => string.Join("\n", handles.Select(handle =>
        {
            CustomAttribute attribute = reader.GetCustomAttribute(handle);
            string constructor = attribute.Constructor.Kind == HandleKind.MemberReference
                ? Signature(reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).DecodeMethodSignature(names, (object?)null))
                : Signature(reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).DecodeSignature(names, (object?)null));
            return AttributeType(reader, attribute.Constructor) + ":" + constructor + ":" + Convert.ToHexString(reader.GetBlobBytes(attribute.Value));
        }));
    }

    private static string AttributeType(MetadataReader reader, EntityHandle constructor)
    {
        EntityHandle parent = constructor.Kind == HandleKind.MemberReference
            ? reader.GetMemberReference((MemberReferenceHandle)constructor).Parent
            : reader.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType();
        var names = new SignatureNames();
        return parent.Kind == HandleKind.TypeReference ? names.GetTypeFromReference(reader, (TypeReferenceHandle)parent, 0)
            : names.GetTypeFromDefinition(reader, (TypeDefinitionHandle)parent, 0);
    }

    private static string Signature(MethodSignature<string> signature) => signature.Header.RawValue + ":" + signature.GenericParameterCount + ":" +
        signature.ReturnType + "(" + string.Join(",", signature.ParameterTypes) + ")";

    private sealed class SignatureNames : ISignatureTypeProvider<string, object?>
    {
        public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[" + shape.Rank + ":" + string.Join(",", shape.Sizes) + ":" + string.Join(",", shape.LowerBounds) + "]";
        public string GetByReferenceType(string elementType) => elementType + "&";
        public string GetFunctionPointerType(MethodSignature<string> signature) => "fn:" + Signature(signature);
        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => genericType + "<" + string.Join(",", typeArguments) + ">";
        public string GetGenericMethodParameter(object? context, int index) => "!!" + index;
        public string GetGenericTypeParameter(object? context, int index) => "!" + index;
        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => (isRequired ? "required:" : "optional:") + modifier + ":" + unmodifiedType;
        public string GetPinnedType(string elementType) => "pinned:" + elementType;
        public string GetPointerType(string elementType) => elementType + "*";
        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            TypeDefinition type = reader.GetTypeDefinition(handle);
            return type.GetDeclaringType().IsNil ? Join(reader.GetString(type.Namespace), reader.GetString(type.Name))
                : GetTypeFromDefinition(reader, type.GetDeclaringType(), rawTypeKind) + "+" + reader.GetString(type.Name);
        }
        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            TypeReference type = reader.GetTypeReference(handle);
            return type.ResolutionScope.Kind == HandleKind.TypeReference
                ? GetTypeFromReference(reader, (TypeReferenceHandle)type.ResolutionScope, rawTypeKind) + "+" + reader.GetString(type.Name)
                : Join(reader.GetString(type.Namespace), reader.GetString(type.Name));
        }
        public string GetTypeFromSpecification(MetadataReader reader, object? context, TypeSpecificationHandle handle, byte rawTypeKind)
            => reader.GetTypeSpecification(handle).DecodeSignature(this, context);
        private static string Join(string space, string name) => space.Length == 0 ? name : space + "." + name;
    }

    // Differing/overloaded/duplicated methods remain in the original owner.
    // A partial class is never excluded just because its Fast copy lacks methods.
    internal static Dictionary<string, string[]> SelectFastMethods(TestMethodShape[] original, IReadOnlyDictionary<string, TestMethodShape[]> fast)
    {
        var full = original.GroupBy(method => method.Method, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var copies = fast.Values.SelectMany(methods => methods).Where(method => method.Method != SchedulingMethod)
            .GroupBy(method => method.Method, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        return fast.ToDictionary(owner => owner.Key, owner =>
        {
            Require(owner.Value.Count(method => method.Method == SchedulingMethod) == 1, "Each Fast host must retain its own scheduling/identity control.");
            return owner.Value.Where(method => method.Method == SchedulingMethod ||
                method.CanPartition && copies[method.Method] == 1 && full.TryGetValue(method.Method, out TestMethodShape[]? matches) && matches.Length == 1 && matches[0] == method)
                .Select(method => method.Method).Order(StringComparer.Ordinal).ToArray();
        }, StringComparer.Ordinal);
    }

    internal static string PartitionFilter(TestMethodShape[] methods, IEnumerable<string> selected, bool include)
    {
        var keys = selected.ToHashSet(StringComparer.Ordinal);
        Require(keys.IsSubsetOf(methods.Select(method => method.Method).ToHashSet(StringComparer.Ordinal)), "Partition names a method absent from the selected assembly.");
        var clauses = new List<string>();
        foreach (IGrouping<string, TestMethodShape> type in methods.GroupBy(method => method.Type, StringComparer.Ordinal))
        {
            string[] chosen = type.Select(method => method.Method).Distinct(StringComparer.Ordinal).Where(keys.Contains).Order(StringComparer.Ordinal).ToArray();
            if (chosen.Length == 0) { continue; }
            Require(chosen.All(name => name.All(character => char.IsLetterOrDigit(character) || character is '.' or '_' or '+')), "Unsupported method name in exact native filter.");
            string prefix = type.Key + ".";
            bool complete = type.All(method => method.CanPartition && keys.Contains(method.Method)) &&
                !methods.Any(method => method.Type != type.Key && method.Method.Contains(prefix, StringComparison.Ordinal));
            if (complete) { clauses.Add("FullyQualifiedName" + (include ? "~" : "!~") + prefix); }
            else { clauses.AddRange(chosen.Select(name => "FullyQualifiedName" + (include ? "=" : "!=") + name)); }
        }
        return clauses.Count == 0 ? Filter : Filter + "&(" + string.Join(include ? "|" : "&", clauses) + ")";
    }

    internal static async Task<int> RunAsync(string[] args)
    {
        var input = new TypeManagerVerificationInputs(args, ["repo-root", "output-root"],
            ["native-budget-seconds", "native-workers", "preparation-timeout-seconds", "preparation-root", "api-compat-tool", "api-dotnet", "powershell", "bootstrap-seconds"]);
        string repo = input.PathOf("repo-root"), language = input.Language, output = input.Output;
        string testSettings = Path.Combine(language, "verification/regression.runsettings");
        bool reusePreparation = input.Optional("preparation-root") is not null;
        string preparation = reusePreparation ? Path.GetFullPath(input.Optional("preparation-root")!) : Path.Combine(output, "preparation");
        if (reusePreparation)
        {
            string prefix = Path.TrimEndingDirectorySeparator(preparation) + Path.DirectorySeparatorChar;
            StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            Require(!output.Equals(preparation, comparison) && !output.StartsWith(prefix, comparison),
                "Regression output must not modify the selected prepared-input directory.");
        }
        Require(!File.Exists(Path.Combine(output, "summary.json")) && !Directory.Exists(Path.Combine(output, "preparation")) && !Directory.Exists(Path.Combine(output, "native")), "Use a fresh regression output directory.");
        Directory.CreateDirectory(output);
        Directory.CreateDirectory(Path.Combine(output, "logs"));
        int nativeBudget = Seconds("native-budget-seconds", NativeTargetSeconds), preparationBudget = Seconds("preparation-timeout-seconds", 1800);
        Require(int.TryParse(input.Optional("native-workers") ?? "1", NumberStyles.None, CultureInfo.InvariantCulture, out int nativeWorkers) && nativeWorkers is >= 1 and <= 4,
            "native-workers must be 1 to 4; the default is one project at a time.");
        string apiDotnet = input.Optional("api-dotnet") ?? Environment.GetEnvironmentVariable("CIS_COMPAT_API_DOTNET") ?? input.Dotnet;
        string apiTool = Path.GetFullPath(input.Optional("api-compat-tool") ?? Environment.GetEnvironmentVariable("CIS_COMPAT_API_TOOL") ??
            Path.Combine(Environment.GetEnvironmentVariable("NUGET_PACKAGES") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget/packages"),
                "microsoft.dotnet.apicompat.tool/10.0.401/tools/net8.0/any/Microsoft.DotNet.ApiCompat.Tool.dll"));
        var environment = new Dictionary<string, string?>
        {
            ["CIS_COMPAT_API_DOTNET"] = apiDotnet,
            ["CIS_COMPAT_API_TOOL"] = apiTool,
            ["CIS_ARCHITECTURE_INSPECTION"] = null,
            ["CIS_VERIFICATION_ROOT"] = null,
            ["CIS_VERIFICATION_REPOSITORY"] = null,
            ["CIS_VERIFICATION_PACKAGE"] = null,
            ["CisMutationTesting"] = null,
            ["CisHealthcheckMutationCohort"] = null,
            ["CisHealthcheckPilotCohort"] = null,
            ["CisHistoricalVerification"] = null,
            ["CisLinuxIdentityQualification"] = null,
            ["CIS_HEALTHCHECK_IDENTITY_MANIFEST"] = null,
            ["CIS_HEALTHCHECK_MUTATION_SESSION"] = null,
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
            ["VSTEST_TELEMETRY_OPTEDOUT"] = "1",
            ["MSBUILDDISABLENODEREUSE"] = "1",
            ["UseSharedCompilation"] = "false",
            ["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0"
        };
        environment["CIS_TEST_HOST_IDENTITY"] = "1";
        var clock = Stopwatch.StartNew();
        var nativeClock = new Stopwatch();
        var commands = new JsonArray();
        var commandsLock = new object();
        string[] hostProjects = Projects.Concat(FastTests.Projects).ToArray();
        var suites = new JsonArray(hostProjects.Select(project => (JsonNode)new JsonObject
        {
            ["project"] = project,
            ["logicalFamily"] = project == "CIS.Core.FastArchitectureTests" ? "CIS.Core.ArchitectureTests" : FastTests.Projects.Contains(project, StringComparer.Ordinal) ? "CIS.Core.UnitTests" : project,
            ["status"] = "NotRun",
            ["selected"] = null,
            ["passed"] = null,
            ["failed"] = null,
            ["skipped"] = null
        }).ToArray());
        var summary = new JsonObject
        {
            ["schema"] = "cis.nonmutation-regression/v1",
            ["platform"] = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            ["host"] = Environment.MachineName,
            ["startedUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["filter"] = Filter,
            ["nativeBudgetSeconds"] = nativeBudget,
            ["nativeTargetSeconds"] = NativeTargetSeconds,
            ["nativeTargetMet"] = false,
            ["correctnessComplete"] = false,
            ["nativeBudgetMet"] = false,
            ["commandTargetSeconds"] = NativeTargetSeconds,
            ["commandTargetMet"] = false,
            ["preparationBudgetSeconds"] = preparationBudget,
            ["preparationRoot"] = preparation,
            ["preparationReused"] = reusePreparation,
            ["nativeWorkers"] = nativeWorkers,
            ["defaultResidualTestWorkersPerProject"] = 4,
            ["residualMethodWorkerProfiles"] = JsonSerializer.SerializeToNode(Projects.ToDictionary(project => project, MethodWorkers, StringComparer.Ordinal)),
            ["fastMethodWorkerProfiles"] = JsonSerializer.SerializeToNode(FastTests.Projects.ToDictionary(project => project, FastTests.Workers, StringComparer.Ordinal)),
            ["testSettings"] = "verification/regression.runsettings",
            ["bootstrapSeconds"] = double.Parse(input.Optional("bootstrap-seconds") ?? "0", CultureInfo.InvariantCulture),
            ["projects"] = suites,
            ["commands"] = commands,
            ["preparation"] = "NotRun",
            ["coverageExecuted"] = false,
            ["mutationExecuted"] = false,
            ["releaseQualified"] = false,
            ["unexecutedRequiredScope"] = "PerformanceObservation, ExtendedStress, coverage, mutation and remaining release/platform obligations require explicit execution."
        };
        bool passed = false;
        try
        {
            Require(File.Exists(testSettings), "Missing native regression settings: " + testSettings);
            summary["testSettingsSha256"] = Hash(testSettings);
            string checker = typeof(RegressionTests).Assembly.Location;
            SourceInventoryEntry[] checkerInputs = new[] { checker, Path.ChangeExtension(checker, ".deps.json"), Path.ChangeExtension(checker, ".runtimeconfig.json"), typeof(ProcessRunner).Assembly.Location }
                .Select(path => new SourceInventoryEntry(path, Hash(path))).ToArray();
            summary["selectedChecker"] = JsonSerializer.SerializeToNode(checkerInputs);
            environment["CIS_VERIFICATION_CHECKER"] = checker;
            ProcessResult sdk = await Run("preflight-sdk", input.Dotnet, ["--version"], 30);
            Require(sdk.StandardOutput.Trim() == "10.0.401", "Regression requires SDK 10.0.401 selected by implementations/csharp/global.json; install/select the pinned SDK.");
            string testConsole = await NativeTestRunner.ResolveConsoleAsync(input.Dotnet, language, TimeSpan.FromSeconds(30));
            string testConsoleSha256 = Hash(testConsole);
            summary["nativeTestPlatform"] = new JsonObject { ["path"] = testConsole, ["sha256"] = testConsoleSha256, ["oneAssemblyPerInvocation"] = true };
            ProcessResult runtimes = await Run("preflight-subject-runtimes", input.Dotnet, ["--list-runtimes"], 30);
            foreach (string name in new[] { "Microsoft.NETCore.App", "Microsoft.AspNetCore.App" })
            { Require(HasRuntime(runtimes.StandardOutput, name, "10.0.12"), "Regression requires installed " + name + " 10.0.12 on the selected dotnet host; no preparation or test matrix started."); }
            Require(File.Exists(apiTool), "Missing pinned ApiCompat entry DLL: " + apiTool + ". Restore tool version 10.0.401 or select --api-compat-tool/CIS_COMPAT_API_TOOL.");
            Require(FileVersionInfo.GetVersionInfo(apiTool).ProductVersion?.Split('+')[0] == "10.0.401", "The selected ApiCompat DLL is not product version 10.0.401: " + apiTool);
            ProcessResult apiRuntimes = await Run("preflight-api-runtimes", apiDotnet, ["--list-runtimes"], 30);
            Require(HasRuntime(apiRuntimes.StandardOutput, "Microsoft.NETCore.App", "8.0.31"), "ApiCompat requires Microsoft.NETCore.App 8.0.31 on " + apiDotnet + ". Install the pinned runtime or select --api-dotnet/CIS_COMPAT_API_DOTNET; no preparation or test matrix started.");
            summary["apiCompatTool"] = new JsonObject { ["path"] = apiTool, ["sha256"] = Hash(apiTool), ["dotnet"] = apiDotnet };

            var preparationClock = Stopwatch.StartNew();
            summary["preparation"] = "Running";
            if (reusePreparation)
            {
                Require(Directory.Exists(preparation), "Explicit prepared inputs do not exist: " + preparation);
            }
            else
            {
                await Run("preparation", input.Optional("powershell") ?? "pwsh",
                    ["-NoProfile", "-File", Path.Combine(language, "eng/verify.ps1"), "-PrepareOnly", "-OutputRoot", preparation], preparationBudget);
            }
            summary["preparationSeconds"] = preparationClock.Elapsed.TotalSeconds;
            summary["preparationSecondsThisCommand"] = summary["preparationSeconds"]!.DeepClone();
            JsonObject prepared = Read(Path.Combine(preparation, "summary.json"));
            Require((string?)prepared["profile"] == "Preparation" && (string?)prepared["scope"] == "prepared-inputs" && (int?)prepared["exitCode"] == 0,
                "Preparation did not produce a successful explicit prepared-inputs summary.");
            summary["preparedCandidateRecordedSeconds"] = (DateTimeOffset.Parse((string)prepared["finishedUtc"]!, CultureInfo.InvariantCulture) -
                DateTimeOffset.Parse((string)prepared["startedUtc"]!, CultureInfo.InvariantCulture)).TotalSeconds;
            Require((string?)prepared["source"]?["inputScope"] == SourceInventory.VerificationScope,
                "Preparation must declare its maintained C# verification input scope.");
            Require(prepared["stages"]!.AsArray().All(stage => (string?)stage!["status"] == "Passed"), "A candidate preparation stage did not pass.");
            foreach (string stage in new[] { "restore", "project-boundaries", "format", "build", "startup-contract", "compatibility-samples", "log-value-consumers", "publication-ledger", "pack", "package-boundaries", "architecture-inspection" })
            { Require(prepared["stages"]!.AsArray().Count(item => (string?)item!["id"] == stage && (string?)item["status"] == "Passed") == 1, "Missing successful preparation stage: " + stage); }
            Require(prepared["candidatePackage"] is JsonObject && prepared["architectureInspection"] is JsonObject,
                "Preparation must identify its exact package and architecture inspection.");
            string package = CompilerChecks.VerifiedReference(preparation, prepared["candidatePackage"]!);
            string inspection = CompilerChecks.VerifiedReference(preparation, prepared["architectureInspection"]!);
            string core = Path.Combine(language, "src/CIS.Core/bin/Release/net10.0/CIS.Core.dll");
            using (ZipArchive archive = ZipFile.OpenRead(package))
            using (Stream packedCore = (archive.GetEntry("lib/net10.0/CIS.Core.dll") ?? throw new InvalidDataException("Prepared package lacks CIS.Core.dll.")).Open())
            { Require(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(packedCore)) == Hash(core), "Prepared package does not contain the current Release Core assembly."); }
            JsonObject architecture = Read(inspection);
            Require((string?)architecture["package"]?["sha256"] == Hash(package), "Prepared architecture inspection identifies another package.");
            foreach (string name in new[] { "startup", "compatibility-samples", "log-value-consumers" })
            {
                string path = Path.Combine(preparation, "contracts", name, "summary.json");
                JsonObject compiler = Read(path);
                Require((int?)compiler["exitCode"] == 0, "Missing/failed prepared compiler inputs: " + path);
                if (name is "startup" or "compatibility-samples")
                { CompilerChecks.ValidatePreparedGroup(language, Path.GetDirectoryName(path)!, name == "startup" ? "startup" : "samples"); }
                if (name == "log-value-consumers") { Require((string?)compiler["coreSha256"] == Hash(core), "Compiler controls identify another Core assembly."); }
            }
            string sourceManifest = CompilerChecks.VerifiedReference(preparation,
                prepared["source"]?["inputInventory"] ?? throw new InvalidDataException("Preparation omitted its source input manifest pin."));
            SourceInventoryEntry[] inputs = JsonSerializer.Deserialize<SourceInventoryEntry[]>(File.ReadAllText(sourceManifest))!;
            Require(inputs.Length > 0, "Preparation contains no maintained implementation/test/build input identity.");
            CheckMembership(repo, inputs);
            SourceReplay.AssertInputs(repo, inputs);
            string nativeManifest = CompilerChecks.VerifiedReference(preparation,
                prepared["nativeInputs"] ?? throw new InvalidDataException("Preparation omitted its native output manifest pin; prepare current inputs again."));
            string nativeManifestSha = Hash(nativeManifest);
            SourceInventoryEntry[] binaries = VerifyPreparedNativeInputs(repo, nativeManifest, sourceManifest);
            SourceInventoryEntry[] fastBinaries = VerifyPreparedFastInputs(repo, nativeManifest, sourceManifest);
            var metadata = new Dictionary<string, TestMethodShape[]>(StringComparer.Ordinal);
            foreach (string project in new[] { "CIS.Core.UnitTests", "CIS.Core.ArchitectureTests" }.Concat(FastTests.Projects))
            { metadata[project] = ReadTestMethods(Path.Combine(language, "tests", project, "bin", "Release", "net10.0", project + ".dll")); }
            var filters = hostProjects.ToDictionary(project => project, _ => Filter, StringComparer.Ordinal);
            var partitions = new JsonObject();
            var selectedFast = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var movedOriginal = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var observedLogicalOwners = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string original in new[] { "CIS.Core.UnitTests", "CIS.Core.ArchitectureTests" })
            {
                string[] fastOwners = original == "CIS.Core.UnitTests" ? FastTests.Projects.Where(project => project != "CIS.Core.FastArchitectureTests").ToArray() : ["CIS.Core.FastArchitectureTests"];
                Dictionary<string, string[]> selected = SelectFastMethods(metadata[original], fastOwners.ToDictionary(project => project, project => metadata[project], StringComparer.Ordinal));
                string[] moved = selected.Values.SelectMany(methods => methods).Where(method => method != SchedulingMethod).ToArray();
                movedOriginal[original] = moved.ToHashSet(StringComparer.Ordinal);
                filters[original] = PartitionFilter(metadata[original], moved, include: false);
                foreach (string owner in fastOwners)
                {
                    selectedFast[owner] = selected[owner].ToHashSet(StringComparer.Ordinal);
                    filters[owner] = PartitionFilter(metadata[owner], selected[owner], include: true);
                }
                partitions[original] = new JsonObject
                {
                    ["movedMethods"] = JsonSerializer.SerializeToNode(moved),
                    ["fastOwners"] = JsonSerializer.SerializeToNode(selected),
                    ["partialAndMismatchedMethodsRemainOriginal"] = true,
                    ["originalSchedulingControlRetained"] = true
                };
            }
            foreach (JsonObject suite in suites.OfType<JsonObject>())
            {
                string project = suite["project"]!.GetValue<string>();
                suite["filter"] = filters[project];
                suite["methodWorkers"] = MethodWorkers(project);
            }
            summary["layout"] = "fresh-Fast-plus-residual";
            summary["partition"] = partitions;
            summary["additionalFastHostIdentityControls"] = 4;
            summary["fastBinaryInputs"] = JsonSerializer.SerializeToNode(fastBinaries);
            summary["preparation"] = "Passed";
            summary["preparedSummarySha256"] = Hash(Path.Combine(preparation, "summary.json"));
            summary["sourceInventorySha256"] = Hash(sourceManifest);
            summary["maintainedInputCount"] = inputs.Length;
            summary["sourceInputScope"] = SourceInventory.VerificationScope;
            summary["preparedPackage"] = prepared["candidatePackage"]!.DeepClone();
            summary["preparedInspection"] = prepared["architectureInspection"]!.DeepClone();
            summary["preparedNativeInputs"] = prepared["nativeInputs"]!.DeepClone();
            summary["binaryInputs"] = JsonSerializer.SerializeToNode(binaries);
            environment["CIS_VERIFICATION_ROOT"] = preparation;
            environment["CIS_VERIFICATION_REPOSITORY"] = repo;
            environment["CIS_VERIFICATION_PACKAGE"] = package;
            environment["CIS_ARCHITECTURE_INSPECTION"] = inspection;
            // Ordinary launchers use the frozen runtime. Isolated bootstrap and
            // copied-reader controls explicitly clear the override themselves.
            environment["CIS_VERIFICATION_CHECKER"] = checker;

            nativeClock.Restart();
            summary["nativeDeadlineUtc"] = DateTimeOffset.UtcNow.AddSeconds(nativeBudget).ToString("O");
            int maximumActive = 0;
            string[] initial = ["CIS.Core.UnitTests", .. FastTests.Projects];
            bool unitFinished = false;
            var completedInitial = new HashSet<string>(StringComparer.Ordinal);
            string[] remainingOrder = ["CIS.Core.ArchitectureTests", "CIS.Core.IntegrationTests", "CIS.Verification.Tests", "CIS.Core.CompatibilityTests", "CIS.Core.BusinessCases"];
            string[] launchOrder = [.. initial, .. remainingOrder];
            summary["remainingHostsWaitForResidualUnit"] = true;
            summary["remainingHostsMayOverlapFastTail"] = true;
            await RunPool(suites.OfType<JsonObject>().OrderBy(suite => Array.IndexOf(launchOrder, suite["project"]!.GetValue<string>())).ToList());

            async Task RunPool(List<JsonObject> pending)
            {
                var active = new List<(Task Operation, JsonObject Suite)>();
                while (pending.Count > 0 || active.Count > 0)
                {
                    while (active.Count < nativeWorkers && nativeClock.Elapsed.TotalSeconds < nativeBudget && pending.Count > 0)
                    {
                        int nextIndex = unitFinished
                            ? pending.FindIndex(suite => suite["project"]!.GetValue<string>() is "CIS.Core.IntegrationTests" or "CIS.Verification.Tests")
                            : pending.FindIndex(suite => initial.Contains(suite["project"]!.GetValue<string>(), StringComparer.Ordinal));
                        if (nextIndex < 0 && unitFinished) { nextIndex = 0; }
                        if (nextIndex < 0) { break; }
                        JsonObject next = pending[nextIndex];
                        pending.RemoveAt(nextIndex);
                        active.Add((ExecuteSuiteAsync(next), next));
                        maximumActive = Math.Max(maximumActive, active.Count);
                    }
                    if (active.Count == 0) { break; }
                    Task completed = await Task.WhenAny(active.Select(work => work.Operation));
                    await completed;
                    int completedIndex = active.FindIndex(work => ReferenceEquals(work.Operation, completed));
                    JsonObject finished = active[completedIndex].Suite;
                    active.RemoveAt(completedIndex);
                    string project = finished["project"]!.GetValue<string>();
                    if (project == "CIS.Core.UnitTests")
                    {
                        unitFinished = true;
                        summary["residualUnitFinishedUtc"] = finished["finishedUtc"]?.DeepClone();
                        summary["heavyHostsReleasedUtc"] = DateTimeOffset.UtcNow.ToString("O");
                    }
                    if (initial.Contains(project, StringComparer.Ordinal) && completedInitial.Add(project) && completedInitial.Count == initial.Length)
                    { summary["initialPhaseFinishedUtc"] = DateTimeOffset.UtcNow.ToString("O"); }
                }
                foreach (JsonObject suite in pending) { suite["reason"] = "The same shared native deadline expired before this required host could launch."; }
            }
            nativeClock.Stop();
            summary["maximumActiveProjectTasks"] = maximumActive;
            summary["nativeSeconds"] = nativeClock.Elapsed.TotalSeconds;
            summary["nativeBudgetMet"] = nativeClock.Elapsed.TotalSeconds <= nativeBudget;
            Require(Hash(testConsole) == testConsoleSha256, "The selected SDK test platform changed during regression.");
            CheckMembership(repo, inputs);
            SourceReplay.AssertInputs(repo, inputs);
            Require(Hash(nativeManifest) == nativeManifestSha, "Prepared native input manifest changed during regression.");
            _ = VerifyPreparedNativeInputs(repo, nativeManifest, sourceManifest);
            _ = VerifyPreparedFastInputs(repo, nativeManifest, sourceManifest);
            Require(checkerInputs.All(pin => File.Exists(pin.Path) && Hash(pin.Path) == pin.Sha256), "The selected regression checker runtime changed during execution.");
            Require(Hash(apiTool) == (string?)summary["apiCompatTool"]?["sha256"], "ApiCompat changed during regression.");
            bool allSuitesPassed = suites.All(suite => (string?)suite!["status"] == "Passed");
            if (allSuitesPassed)
            {
                Require(suites.Select(suite => ((int)suite!["hostProcessId"]!, (string)suite["hostStartedUtc"]!)).Distinct().Count() == hostProjects.Length &&
                    suites.Select(suite => (int)suite!["hostProcessId"]!).Distinct().Count() == hostProjects.Length,
                    "Required suites must execute in distinct native test host instances.");
            }
            summary["allSuitesPassed"] = allSuitesPassed;
            summary["nativeTargetMet"] = allSuitesPassed && nativeClock.Elapsed.TotalSeconds <= NativeTargetSeconds;
            passed = allSuitesPassed && (bool?)summary["nativeBudgetMet"] == true;

            async Task ExecuteSuiteAsync(JsonObject suite)
            {
                string name = suite["project"]!.GetValue<string>();
                double remaining = nativeBudget - nativeClock.Elapsed.TotalSeconds;
                if (remaining <= 0) { suite["reason"] = "The shared native regression deadline expired before launch."; return; }
                string report = Path.Combine(output, "native", name, "report.trx");
                DateTimeOffset started = DateTimeOffset.UtcNow;
                var suiteClock = Stopwatch.StartNew();
                suite["startedUtc"] = started.ToString("O");
                suite["frozenChecker"] = checker;
                try
                {
                    string results = Path.GetDirectoryName(report)!;
                    Directory.CreateDirectory(results);
                    string assembly = Path.Combine(language, "tests", name, "bin", "Release", "net10.0", name + ".dll");
                    NativeTestRunner.ValidateAssembly(assembly);
                    Require(Hash(testConsole) == testConsoleSha256, "The selected SDK test platform changed before native launch.");
                    bool fast = FastTests.Projects.Contains(name, StringComparer.Ordinal);
                    string settings = WriteSettings(language, results, name);
                    suite["settingsPath"] = settings;
                    suite["settingsSha256"] = Hash(settings);
                    ProcessResult process = await Run(name, input.Dotnet,
                        [.. NativeTestRunner.Arguments(testConsole, assembly, settings, results, filters[name]), "/Logger:console;verbosity=normal"], remaining, requireSuccess: false);
                    Require(Hash(settings) == (string?)suite["settingsSha256"], "Native host settings changed during execution.");
                    ReadCounts(report, suite);
                    Require(process.ExitCode == 0 && process.OutputDrained, "Native suite failed or did not drain process output.");
                    FocusedTests.NativeCase[] cases = FocusedTests.ReadResults(report, started);
                    Require(cases.All(item => NativeTestRunner.SamePath(item.Source, assembly)), "Native suite CodeBase differs from its selected assembly.");
                    if (fast) { Require(cases.All(item => selectedFast[name].Contains(item.Method)), "Fast host ran a method assigned to its original owner."); }
                    else if (movedOriginal.TryGetValue(name, out HashSet<string>? moved))
                    { Require(cases.All(item => !moved.Contains(item.Method)), "Residual host reran a method assigned to Fast."); }
                    lock (commandsLock)
                    {
                        foreach (FocusedTests.NativeCase item in cases.Where(item => item.Method != SchedulingMethod))
                        {
                            string identity = suite["logicalFamily"]!.GetValue<string>() + "\0" + item.Method + "\0" + item.Name;
                            Require(!observedLogicalOwners.TryGetValue(identity, out string? owner) || owner == name, "The same logical occurrence was executed by two partition hosts.");
                            observedLogicalOwners[identity] = name;
                        }
                    }
                    NativeTestRunner.HostIdentity host = NativeTestRunner.ReadHostIdentity(report, assembly);
                    suite["hostProcessId"] = host.ProcessId;
                    suite["hostStartedUtc"] = host.StartedUtc;
                    suite["hostAssembly"] = host.Assembly;
                    suite["additionalHostIdentityCases"] = fast ? 1 : 0;
                    suite["logicalCases"] = cases.Length - (fast ? 1 : 0);
                    suite["status"] = "Passed";
                }
                catch (Exception error) when (Expected(error))
                {
                    suite["status"] = error is NativeDeadlineException ? "NotRun" : "Failed";
                    suite["reason"] = error.Message;
                    if (File.Exists(report)) { ReadCountsSafely(report, suite); }
                    suite["failureKind"] = error is NativeDeadlineException ? "NativeDeadlineBeforeLaunch"
                        : error is ProcessTimeoutException ? "TimeoutOrCleanupFailure"
                        : (int?)suite["failed"] > 0 ? "NativeFailedTests" : "ExecutionOrReportFailure";
                }
                suite["seconds"] = suiteClock.Elapsed.TotalSeconds;
                suite["finishedUtc"] = DateTimeOffset.UtcNow.ToString("O");
                if (File.Exists(report))
                {
                    try { suite["report"] = Path.GetRelativePath(output, report).Replace('\\', '/'); suite["reportSha256"] = Hash(report); }
                    catch (Exception error) when (Expected(error)) { suite["status"] = "Failed"; suite["reportIssue"] = error.Message; }
                }
            }
        }
        catch (Exception error) when (Expected(error)) { summary["failure"] = error.Message; Console.Error.WriteLine(error.Message); }
        nativeClock.Stop();
        if ((string?)summary["preparation"] == "Running") { summary["preparation"] = "Failed"; }
        summary["passed"] = passed;
        summary["finishedUtc"] = DateTimeOffset.UtcNow.ToString("O");
        summary["totalSecondsExcludingBootstrap"] = clock.Elapsed.TotalSeconds;
        RecordMeasurements(summary, (bool?)summary["allSuitesPassed"] == true,
            nativeClock.Elapsed.TotalSeconds, nativeBudget,
            clock.Elapsed.TotalSeconds + (double)summary["bootstrapSeconds"]!);
        summary["commandMeasurement"] = "Checker wall plus launcher bootstrap; the supported launcher records its complete invocation wall time after checker postflight.";
        summary["fullSolutionBuilds"] = reusePreparation ? 0 : (string?)summary["preparation"] == "Passed" ? 1 : (string?)summary["preparation"] == "NotRun" ? 0 : null;
        summary["nativeProjectInvocations"] = commands.Count(command => hostProjects.Contains(command!["phase"]!.GetValue<string>(), StringComparer.Ordinal));
        summary["counts"] = new JsonObject
        {
            ["selected"] = suites.Sum(suite => (int?)suite!["selected"] ?? 0),
            ["passed"] = suites.Sum(suite => (int?)suite!["passed"] ?? 0),
            ["failed"] = suites.Sum(suite => (int?)suite!["failed"] ?? 0),
            ["skipped"] = suites.Sum(suite => (int?)suite!["skipped"] ?? 0),
            ["complete"] = suites.All(suite => (string?)suite!["status"] == "Passed")
        };
        summary["logicalCounts"] = new JsonArray(Projects.Select(project => (JsonNode)new JsonObject
        {
            ["project"] = project,
            ["selected"] = suites.Where(suite => (string?)suite!["logicalFamily"] == project).Sum(suite => (int?)suite!["logicalCases"] ?? 0),
            ["complete"] = suites.Where(suite => (string?)suite!["logicalFamily"] == project).All(suite => (string?)suite!["status"] == "Passed")
        }).ToArray());
        WriteJson(Path.Combine(output, "summary.json"), summary);
        Console.WriteLine($"Non-mutation regression {(passed ? "passed" : "failed/incomplete")}; correctness complete: {summary["correctnessComplete"]}; native budget met: {summary["nativeBudgetMet"]}; native {NativeTargetSeconds}-second target met: {summary["nativeTargetMet"]}; command target met: {summary["commandTargetMet"]}. Summary: {Path.Combine(output, "summary.json")}");
        return passed ? 0 : 1;

        int Seconds(string key, int fallback)
        {
            Require(int.TryParse(input.Optional(key) ?? fallback.ToString(CultureInfo.InvariantCulture), NumberStyles.None, CultureInfo.InvariantCulture, out int value) && value is > 0 and <= 3600,
                key + " must be 1-3600 seconds.");
            return value;
        }

        async Task<ProcessResult> Run(string phase, string executable, string[] arguments, double timeout, bool requireSuccess = true)
        {
            if (hostProjects.Contains(phase, StringComparer.Ordinal))
            {
                double remaining = nativeBudget - nativeClock.Elapsed.TotalSeconds;
                if (remaining <= 0) { throw new NativeDeadlineException(); }
                timeout = Math.Min(timeout, remaining);
            }
            Console.WriteLine("Regression: " + phase);
            ProcessResult? result = null;
            bool timedOut = false;
            bool? cleanupCompleted = null;
            DateTimeOffset started = DateTimeOffset.UtcNow;
            var phaseClock = Stopwatch.StartNew();
            int captureBudget = hostProjects.Contains(phase, StringComparer.Ordinal) ? 4 * 1024 * 1024 : 65536;
            try
            { result = await ProcessRunner.RunAsync(executable, arguments, language, TimeSpan.FromSeconds(timeout), environment, maximumCapturedBytesPerStream: captureBudget, allowTruncatedOutput: true); }
            catch (ProcessTimeoutException error) { result = error.Result; timedOut = true; cleanupCompleted = error.CleanupCompleted; throw; }
            finally
            {
                lock (commandsLock)
                {
                    commands.Add(new JsonObject
                    {
                        ["phase"] = phase,
                        ["executable"] = executable,
                        ["arguments"] = JsonSerializer.SerializeToNode(arguments),
                        ["startedUtc"] = started.ToString("O"),
                        ["finishedUtc"] = DateTimeOffset.UtcNow.ToString("O"),
                        ["seconds"] = phaseClock.Elapsed.TotalSeconds,
                        ["exitCode"] = result?.ExitCode,
                        ["timedOut"] = timedOut,
                        ["cleanupCompleted"] = cleanupCompleted,
                        ["outputDrained"] = result?.OutputDrained,
                        ["captureBudgetBytesPerStream"] = captureBudget,
                        ["captureTruncated"] = result?.CaptureTruncated,
                        ["standardOutputTotalBytes"] = result?.StandardOutputTotalBytes,
                        ["standardErrorTotalBytes"] = result?.StandardErrorTotalBytes
                    });
                }
                if (result is not null)
                { FocusedTests.WriteDiagnosticLog(Path.Combine(output, "logs", phase + ".log"), result.StandardOutput, result.StandardError, result.CaptureTruncated); }
            }
            Require(result.OutputDrained && (!requireSuccess || result.ExitCode == 0), phase + " failed; see its bounded native log.");
            return result;
        }
    }

    internal static void RecordMeasurements(JsonObject summary, bool correctnessComplete, double nativeSeconds, int nativeBudgetSeconds, double commandSeconds)
    {
        Require(double.IsFinite(nativeSeconds) && nativeSeconds >= 0 && double.IsFinite(commandSeconds) && commandSeconds >= nativeSeconds,
            "Regression timings must be finite and command time must include native execution.");
        summary["correctnessComplete"] = correctnessComplete;
        summary["nativeSeconds"] = nativeSeconds;
        summary["nativeBudgetMet"] = nativeSeconds <= nativeBudgetSeconds;
        summary["nativeTargetSeconds"] = NativeTargetSeconds;
        summary["nativeTargetMet"] = correctnessComplete && nativeSeconds <= NativeTargetSeconds;
        summary["commandSeconds"] = commandSeconds;
        summary["commandTargetSeconds"] = NativeTargetSeconds;
        summary["commandTargetMet"] = correctnessComplete && commandSeconds <= NativeTargetSeconds;
    }

    private static bool HasRuntime(string text, string name, string version) => text.Split('\n').Any(line => line.StartsWith(name + " " + version + " [", StringComparison.Ordinal));

    private static void CheckMembership(string repo, SourceInventoryEntry[] prepared)
    {
        Require(SourceInventory.VerificationPaths(repo).SequenceEqual(prepared.Select(pin => pin.Path).Order(StringComparer.Ordinal), StringComparer.Ordinal),
            "Maintained source/test/build inputs were added or removed after preparation, or the prepared inventory omitted an active input.");
    }

    private static bool Expected(Exception error) => error is IOException or InvalidDataException or BadImageFormatException or UnauthorizedAccessException or ArgumentException
        or InvalidOperationException or FormatException or TimeoutException or System.Xml.XmlException or JsonException or System.ComponentModel.Win32Exception;

    private static void ReadCountsSafely(string path, JsonObject suite)
    { try { ReadCounts(path, suite); } catch (Exception error) when (Expected(error)) { suite["reportIssue"] = error.Message; } }

    private static void ReadCounts(string path, JsonObject suite)
    {
        Require(new FileInfo(path).Length <= FocusedTests.MaximumReportBytes, "Native report exceeds the supported result-reader budget; raw TRX remains unchanged.");
        XDocument report = XDocument.Load(path);
        XElement counters = report.Descendants().Single(element => element.Name.LocalName == "Counters");
        suite["selected"] = (int?)counters.Attribute("total");
        suite["passed"] = (int?)counters.Attribute("passed");
        suite["failed"] = (int?)counters.Attribute("failed");
        suite["skipped"] = (int?)counters.Attribute("notExecuted");
    }
}
