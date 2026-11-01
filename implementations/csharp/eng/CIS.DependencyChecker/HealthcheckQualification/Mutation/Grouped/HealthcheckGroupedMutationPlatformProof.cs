using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using static CIS.Verification.TypeManagerVerificationInputs;

namespace CIS.Verification;

internal static partial class HealthcheckGroupedMutation
{
    internal sealed record PlatformProofReference(string Path, string Sha256);
    internal sealed record PlatformClaim(string DefinitionIdentity, string WitnessSource, string WitnessName, string Requirement);
    internal sealed record PlatformTool(string OriginalPath, string Entry, string Sha256, long Bytes);
    internal sealed record PlatformAdmission(string DefinitionIdentity, string ProofSha256, string OriginCandidateIdentity,
        string Platform, string Host, string WitnessSource, string WitnessName);
    internal sealed record PlatformProof(string Schema, string OriginCandidateIdentity, string CommonInputsSha256, string Platform,
        string Host, JsonObject ExecutionIdentity, string Group, Input[] Files, PlatformTool[] Tools, PlatformClaim[] Claims,
        string ProducerSha256, string HelperSha256, DateTimeOffset CreatedUtc);

    // Actual execution context, not an operator-supplied platform/identity label.
    internal static JsonObject ObserveExecutionIdentity()
    {
        if (OperatingSystem.IsWindows())
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return new JsonObject
            {
                ["operatingSystem"] = "windows",
                ["userSid"] = identity.User?.Value,
                ["ownerSid"] = identity.Owner?.Value,
                ["administratorToken"] = principal.IsInRole(WindowsBuiltInRole.Administrator)
            };
        }
        Require(OperatingSystem.IsLinux(), "Platform execution identity is supported only on the declared Windows/Linux lanes.");
        return new JsonObject
        {
            ["operatingSystem"] = "linux",
            ["uid"] = PlatformGetUid(),
            ["effectiveUid"] = PlatformGetEffectiveUid(),
            ["gid"] = PlatformGetGid(),
            ["effectiveGid"] = PlatformGetEffectiveGid()
        };
    }

    [DllImport("libc", EntryPoint = "getuid")] private static extern uint PlatformGetUid();
    [DllImport("libc", EntryPoint = "geteuid")] private static extern uint PlatformGetEffectiveUid();
    [DllImport("libc", EntryPoint = "getgid")] private static extern uint PlatformGetGid();
    [DllImport("libc", EntryPoint = "getegid")] private static extern uint PlatformGetEffectiveGid();

    private static string CommonInputs(Candidate candidate) => Digest(JsonSerializer.SerializeToUtf8Bytes(candidate.Inputs, Json));

    private static async Task<int> ExportPlatformProofAsync(string[] args)
    {
        var input = new TypeManagerVerificationInputs(args, ["repo-root", "candidate-root", "claims", "output-root"], []);
        string root = input.PathOf("candidate-root"), repo = input.PathOf("repo-root"), output = input.Output;
        Require(OperatingSystem.IsWindows() && RuntimeInformation.FrameworkDescription == ".NET 10.0.12" && RuntimeInformation.ProcessArchitecture == Architecture.X64,
            "Export actual Windows x64 proof on pinned subject runtime10.0.12.");
        Require(AtOrUnder(root, output) && !AtOrUnder(Path.Combine(root, "workspace"), output), "Keep the portable proof in its owning candidate's retained scope, outside source inputs.");
        using var lease = new FileStream(Path.Combine(root, "active.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        Candidate candidate = CandidateAt(root); VerifyInputs(repo, candidate); ValidatePilot(root, candidate);
        Require(candidate.Platform == RuntimeInformation.OSDescription + "; " + RuntimeInformation.ProcessArchitecture + "; " + RuntimeInformation.FrameworkDescription
            && candidate.Host == Environment.MachineName, "A proof must be exported on its actually executed native platform/host.");
        Toolchain tools = ReadToolchain(root, candidate);
        Group group = Groups(candidate).Single(group => group.Name == "pipes");
        _ = ValidateCompletedTransportAsyncControls(root, candidate, group);
        string runRoot = Path.Combine(root, "groups/pipes");
        JsonObject run = Read(Path.Combine(runRoot, "run.json"));
        ValidatePlatformNativeCommand(run, tools);
        string ownedScope = ValidateOwnedScope(root, (string?)run["ownedStorageScope"]?["root"]
            ?? throw new InvalidDataException("The actual native owned storage scope is missing."));
        JsonObject identity = ObserveExecutionIdentity();
        Require(JsonNode.DeepEquals(run["executionIdentity"], identity), "The exporter identity differs from the actual native run context.");
        PlatformClaim[] claims = JsonSerializer.Deserialize<PlatformClaim[]>(File.ReadAllText(input.PathOf("claims")), Json)
            ?? throw new InvalidDataException("Missing current platform proof claims.");
        JsonObject report = Read(Path.Combine(runRoot, "native/reports/mutation-report.json"));
        Definition[] definitions = Definitions(report, group.Sources, candidate.Inputs);
        Require(!definitions.Any(definition => definition.Status is "RuntimeError" or "Pending") && NativePopulationHolds(definitions).Length == 0,
            "Incomplete/failed or unresolved native populations cannot supply a platform proof.");
        ValidatePlatformClaims(claims, definitions, report, candidate.Inputs);
        input.EmptyOutput();
        var files = new List<Input>();
        var sourcePins = new List<(string Path, string Sha256)>();
        Copy("candidate.json", Path.Combine(root, "candidate.json")); Copy("candidate.zip", Path.Combine(root, "candidate.zip"));
        Copy("toolchain.json", Path.Combine(root, "toolchain.json")); Copy("profile.json", Path.Combine(root, "profiles/pipes.json"));
        Copy("run.json", Path.Combine(runRoot, "run.json")); Copy("report.json", Path.Combine(runRoot, "native/reports/mutation-report.json"));
        Copy("native.log", Path.Combine(runRoot, "native.log"));
        foreach (string project in group.Projects)
        {
            string name = Path.GetFileNameWithoutExtension(project);
            foreach (string phase in new[] { "baseline", "restored" }) { Copy(phase + "-" + name + ".trx", Path.Combine(runRoot, phase + "-" + name + ".trx")); }
        }
        var toolFiles = tools.Files.ToList();
        foreach (string project in group.Projects)
        {
            string projectRoot = Path.GetDirectoryName(Path.GetFullPath(Path.Combine(repo, LanguagePrefix, "src/CIS.Core", project)))!;
            string bin = Path.Combine(projectRoot, "bin/Release/net10.0");
            foreach (Input adapter in tools.Adapters)
            {
                string path = Path.Combine(bin, adapter.Path); Require(Hash(path) == adapter.Sha256, "The native adapter changed before platform export.");
                toolFiles.Add(new(path, adapter.Sha256));
            }
            foreach (string file in new[] { Path.GetFileNameWithoutExtension(project) + ".dll", Path.GetFileNameWithoutExtension(project) + ".pdb",
                Path.GetFileNameWithoutExtension(project) + ".deps.json", Path.GetFileNameWithoutExtension(project) + ".runtimeconfig.json", "CIS.Core.dll" })
            { string path = Path.Combine(bin, file); toolFiles.Add(new(path, Hash(path))); }
        }
        Input[] uniqueTools = toolFiles.DistinctBy(pin => pin.Path, StringComparer.OrdinalIgnoreCase).OrderBy(pin => pin.Path, StringComparer.Ordinal).ToArray();
        var toolEntries = new List<PlatformTool>();
        string archivePath = Path.Combine(output, "tools.zip");
        using (ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            long expanded = 0;
            for (int index = 0; index < uniqueTools.Length; index++)
            {
                Input pin = uniqueTools[index]; string entryName = index.ToString("D4", System.Globalization.CultureInfo.InvariantCulture) + "/" + Path.GetFileName(pin.Path);
                Require(Hash(pin.Path) == pin.Sha256, "A native tool/input changed during platform export.");
                long bytes = new FileInfo(pin.Path).Length; expanded = checked(expanded + bytes);
                Require(expanded <= OwnedLimit, "The exported tool closure exceeds the existing owned-byte ceiling.");
                archive.CreateEntryFromFile(pin.Path, entryName, CompressionLevel.Optimal); toolEntries.Add(new(pin.Path, entryName, pin.Sha256, bytes));
                sourcePins.Add((pin.Path, pin.Sha256)); Capacity();
            }
        }
        files.Add(new("tools.zip", Hash(archivePath)));
        string producer = typeof(HealthcheckGroupedMutation).Assembly.Location, helper = typeof(CIS.TestSupport.ProcessRunner).Assembly.Location;
        var proof = new PlatformProof("cis.healthcheck.platform-native-proof/v1", candidate.Identity, CommonInputs(candidate), candidate.Platform,
            candidate.Host, identity, "pipes", files.OrderBy(pin => pin.Path, StringComparer.Ordinal).ToArray(), toolEntries.ToArray(), claims,
            Hash(producer), Hash(helper), DateTimeOffset.UtcNow);
        VerifyInputs(repo, candidate); _ = ReadToolchain(root, candidate);
        Require(sourcePins.All(pin => Hash(pin.Path) == pin.Sha256) && JsonNode.DeepEquals(ObserveExecutionIdentity(), identity), "Platform inputs/context changed during export.");
        WriteRetained(root, Path.Combine(output, "proof.json"), JsonSerializer.Serialize(proof, Json)); Capacity();
        Console.WriteLine("Exported actual Windows proof; raw chosen-lane outcomes and score remain unchanged: " + Path.Combine(output, "proof.json"));
        await Task.CompletedTask; return 0;

        void Capacity() => Require(OwnedBytes(ownedScope) <= OwnedLimit && OwnedBytes(root, skipWorkspace: true) <= RetainedLimit
            && Math.Min(ResolveStorageVolume(root).AvailableBytes(), ResolveStorageVolume(ownedScope).AvailableBytes()) >= FreeReserve,
            "Platform export exceeded existing aggregate owned/retained/free-reserve limits.");
        void Copy(string relative, string original)
        {
            Capacity(); string hash = Hash(original);
            Require(OwnedBytes(root, skipWorkspace: true) + new FileInfo(original).Length <= RetainedLimit, "Portable evidence would exceed retained scope.");
            File.Copy(original, Path.Combine(output, relative), overwrite: false);
            Require(Hash(original) == hash && Hash(Path.Combine(output, relative)) == hash, "Actual platform evidence changed while copying.");
            files.Add(new(relative, hash)); sourcePins.Add((original, hash)); Capacity();
        }
    }

    private static PlatformAdmission[] ReadPlatformAdmissions(string root, Candidate candidate, Toolchain mainTools, Definition[] chosen, Review[] reviews)
    {
        Require(reviews.Length > 0, "Missing platform reviews.");
        PlatformProofReference reference = reviews[0].PlatformProof ?? throw new InvalidDataException("PlatformCovered needs its exact actual native proof reference.");
        Require(reviews.All(review => review.PlatformProof == reference), "One platform bundle must retain one exact proof reference.");
        string path = Path.GetFullPath(reference.Path), directory = Path.GetDirectoryName(path)!;
        _ = ResolveStorageVolume(directory);
        Require(Path.IsPathFullyQualified(reference.Path) && AtOrUnder(root, path) && Path.GetFileName(path) == "proof.json"
            && Hash(path) == reference.Sha256, "Platform proof must be unchanged, owned retained evidence.");
        Require(OwnedBytes(root, skipWorkspace: true) <= RetainedLimit, "Portable proof evidence exceeds retained scope.");
        PlatformProof proof = JsonSerializer.Deserialize<PlatformProof>(File.ReadAllText(path), Json) ?? throw new InvalidDataException("Missing platform proof.");
        string producerHash = mainTools.Files.Single(pin => Path.GetFileName(pin.Path) == "CIS.DependencyChecker.dll").Sha256;
        string helperHash = mainTools.Files.Single(pin => Path.GetFileName(pin.Path) == "CIS.Core.TestSupport.dll").Sha256;
        ValidatePlatformProofHeader(proof, candidate, producerHash, helperHash);
        string[] requiredFiles = ["candidate.json", "candidate.zip", "toolchain.json", "profile.json", "run.json", "report.json", "native.log", "tools.zip",
            "baseline-CIS.Core.UnitTests.trx", "restored-CIS.Core.UnitTests.trx", "baseline-CIS.Core.IntegrationTests.trx", "restored-CIS.Core.IntegrationTests.trx"];
        Require(proof.Files.Select(pin => pin.Path).Order(StringComparer.Ordinal).SequenceEqual(requiredFiles.Order(StringComparer.Ordinal), StringComparer.Ordinal), "Portable proof artifact membership differs.");
        foreach (Input pin in proof.Files) { Require(Hash(HealthcheckMutationChildren.HealthcheckMutationChildProtocol.Input(directory, pin.Path)) == pin.Sha256, "A portable native proof artifact changed: " + pin.Path); }
        Candidate foreign = CandidateAt(directory);
        Require(foreign.Identity == proof.OriginCandidateIdentity && foreign.Platform == proof.Platform && foreign.Host == proof.Host
            && foreign.Inputs.SequenceEqual(candidate.Inputs) && CommonInputs(foreign) == proof.CommonInputsSha256, "Platform source/test/build/config inputs differ from the chosen candidate.");
        VerifyPlatformArchive(Path.Combine(directory, "candidate.zip"), foreign.Inputs);
        Group group = Groups(candidate).Single(group => group.Name == "pipes");
        Require(Read(Path.Combine(directory, "profile.json")).ToJsonString() == Configuration(group).ToJsonString(), "Foreign native selection/coverage/bail/concurrency differs from its current whole Pipes recipe.");
        JsonObject run = Read(Path.Combine(directory, "run.json")); ValidateExecution(run);
        ValidatePlatformContext(run, group, proof.ExecutionIdentity);
        Require(run["linuxIdentity"] is null, "A Windows proof cannot carry a Linux identity prerequisite receipt.");
        Require((string?)run["schema"] == "cis.healthcheck.grouped-run/v1" && (string?)run["candidateIdentity"] == foreign.Identity && (string?)run["group"] == "pipes" && (string?)run["platform"] == proof.Platform
            && (string?)run["host"] == proof.Host && (string?)run["configSha256"] == Hash(Path.Combine(directory, "profile.json"))
            && (string?)run["reportSha256"] == Hash(Path.Combine(directory, "report.json"))
            && (string?)run["toolchainSha256"] == Hash(Path.Combine(directory, "toolchain.json")), "Platform native run/config/report/toolchain binding differs.");
        Require(run["sources"]!.AsArray().Select(node => node!.GetValue<string>()).SequenceEqual(group.Sources, StringComparer.Ordinal), "Platform native source population differs from the whole group.");
        Toolchain tools = JsonSerializer.Deserialize<Toolchain>(File.ReadAllText(Path.Combine(directory, "toolchain.json")), Json)!;
        Require(tools.Schema == "cis.healthcheck.grouped-toolchain/v1" && tools.Platform == proof.Platform && tools.Host == proof.Host
            && tools.Sdk == "10.0.401" && tools.SubjectRuntime == "10.0.12" && tools.ToolRuntime == "8.0.31"
            && tools.Files.Length > 0 && tools.Adapters.Length > 0, "Foreign native tool/runtime versions or payload inventories differ.");
        ValidatePlatformNativeCommand(run, tools);
        ValidatePlatformTools(Path.Combine(directory, "tools.zip"), proof.Tools, tools, mainTools, producerHash, helperHash);
        Input[] trxPins = run["nativeTrxPins"]!.Deserialize<Input[]>(Json)!;
        foreach (string name in requiredFiles.Where(name => name.EndsWith(".trx", StringComparison.Ordinal)))
        {
            Require(trxPins.Single(pin => pin.Path == name).Sha256 == Hash(Path.Combine(directory, name)), "Platform baseline/restored TRX changed.");
            PassingTrx(Path.Combine(directory, name));
        }
        JsonObject report = Read(Path.Combine(directory, "report.json"));
        Definition[] definitions = Definitions(report, group.Sources, candidate.Inputs);
        Require(!definitions.Any(item => item.Status is "RuntimeError" or "Pending") && NativePopulationHolds(definitions).Length == 0,
            "Foreign native execution remains incomplete, failed or unresolved.");
        _ = ValidateTransportAsyncControls("pipes", definitions, report, relative => CandidateSource(root, candidate, relative));
        ValidatePlatformClaims(proof.Claims, definitions, report, candidate.Inputs);
        PlatformAdmission[] admissions = reviews.Select(review => ValidatePlatformCoveredDefinition(chosen.Single(item => item.Identity == review.DefinitionIdentity),
            review, proof, definitions, reference.Sha256)).ToArray();
        Require(Hash(path) == reference.Sha256 && proof.Files.All(pin => Hash(HealthcheckMutationChildren.HealthcheckMutationChildProtocol.Input(directory, pin.Path)) == pin.Sha256),
            "Portable proof evidence changed during batch receiving.");
        return admissions;
    }

    internal static void ValidatePlatformContext(JsonObject run, Group group, JsonObject identity)
    {
        var expected = new Dictionary<string, string>(group.BuildProperties, StringComparer.Ordinal) { ["CisLinuxIdentityQualification"] = "false" };
        Require(JsonNode.DeepEquals(run["buildProperties"], JsonSerializer.SerializeToNode(expected, Json))
            && run["environment"] is JsonObject environment && expected.All(property => (string?)environment[property.Key] == property.Value)
            && JsonNode.DeepEquals(run["executionIdentity"], identity), "Only the declared Windows/Linux identity-context property may differ; actual native execution identity must match.");
    }

    internal static void ValidatePlatformProofHeader(PlatformProof proof, Candidate candidate, string producerHash, string helperHash)
    {
        Require(proof.Schema == "cis.healthcheck.platform-native-proof/v1" && proof.Group == "pipes"
            && proof.CommonInputsSha256 == CommonInputs(candidate) && proof.Platform.StartsWith("Microsoft Windows ", StringComparison.Ordinal)
            && proof.Platform.EndsWith("; X64; .NET 10.0.12", StringComparison.Ordinal)
            && (string?)proof.ExecutionIdentity["operatingSystem"] == "windows" && !string.IsNullOrWhiteSpace((string?)proof.ExecutionIdentity["userSid"])
            && !string.IsNullOrWhiteSpace((string?)proof.ExecutionIdentity["ownerSid"]), "Platform proof is not a current Windows x64 native context with identical maintained inputs.");
        Require(proof.ProducerSha256 == producerHash && proof.HelperSha256 == helperHash,
            "Use the same frozen platform-neutral checker/helper bytes for platform export and chosen-lane receiving.");
    }

    internal static void ValidatePlatformNativeCommand(JsonObject run, Toolchain tools)
    {
        ValidateSdkMsBuildEnvironment(run, tools);
        Input cli = tools.Files.Single(pin => ForeignFileName(pin.Path) == "Stryker.CLI.dll"), core = tools.Files.Single(pin => ForeignFileName(pin.Path) == "Stryker.Core.dll");
        Require((string?)run["strykerCliSha256"] == cli.Sha256 && (string?)run["strykerCoreSha256"] == core.Sha256, "Platform native producer hashes differ from its frozen deployment.");
        JsonNode command = run["commandLog"]!.AsArray().Single(command => (string?)command!["name"] == "native")!;
        string[] arguments = command["arguments"]!.Deserialize<string[]>(Json)!;
        bool Same(string first, string second) => first.Replace('\\', '/').Equals(second.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);
        Require(Same((string?)command["executable"] ?? "", tools.ToolDotnet) && arguments.Length == 11
            && arguments.Take(5).SequenceEqual(new[] { "exec", "--fx-version", "8.0.31", "--roll-forward", "Disable" }, StringComparer.Ordinal)
            && Same(arguments[5], cli.Path) && arguments[6] == "--config-file" && arguments[8] == "--output" && arguments[10] == "--skip-version-check",
            "Platform native command changed the tool/runtime or added undeclared overrides.");
        string config = arguments[7].Replace('\\', '/'); const string suffix = "/profiles/pipes.json";
        Require(config.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            && Same(arguments[9], config[..^suffix.Length] + "/groups/pipes/native"), "Platform native config/output paths do not belong to one exact Pipes attempt.");
    }

    internal static PlatformAdmission ValidatePlatformCoveredDefinition(Definition definition, Review review, PlatformProof proof, Definition[] foreign, string proofHash)
    {
        Require(review.Decision == "PlatformCovered" && definition.Status is "Survived" or "NoCoverage" && review.RawStatus == definition.Status,
            "PlatformCovered applies only to the unchanged chosen-lane Survived/NoCoverage outcome; Timeout has no credit.");
        PlatformClaim claim = proof.Claims.Single(item => item.DefinitionIdentity == definition.Identity);
        Require(claim.Requirement == review.Requirement, "The platform claim and named miss review refer to different obligations.");
        Definition actual = foreign.Single(item => item.Identity == definition.Identity);
        Require(actual.Status == "Killed" && actual.Source == definition.Source && actual.SourceSha256 == definition.SourceSha256
            && actual.Operator == definition.Operator && actual.Location == definition.Location && actual.Replacement == definition.Replacement,
            "The actual foreign Killed definition differs from the chosen-lane source/operator/span/replacement.");
        return new(definition.Identity, proofHash, proof.OriginCandidateIdentity, proof.Platform, proof.Host, claim.WitnessSource, claim.WitnessName);
    }

    internal static void ValidatePlatformClaims(PlatformClaim[] claims, Definition[] definitions, JsonObject report, Input[] inputs)
    {
        Require(claims.Length > 0 && claims.Select(claim => claim.DefinitionIdentity).Distinct(StringComparer.Ordinal).Count() == claims.Length, "Platform claims must identify unique actual definitions.");
        foreach (PlatformClaim claim in claims)
        {
            Definition definition = definitions.Single(item => item.Identity == claim.DefinitionIdentity);
            Require(definition.Status == "Killed" && !string.IsNullOrWhiteSpace(claim.Requirement)
                && claim.WitnessSource.StartsWith("tests/", StringComparison.Ordinal) && claim.WitnessSource.EndsWith(".cs", StringComparison.Ordinal)
                && !claim.WitnessName.Contains("TestAssemblySetup", StringComparison.Ordinal), "Platform claims need a real product kill and current requirement/test witness.");
            JsonNode native = report["files"]!.AsObject().Single(file => CoreReportPath(file.Key) == definition.Source).Value!["mutants"]!.AsArray()
                .Single(node => (string?)node!["id"] == definition.NativeId)!;
            Require((string?)native["status"] == "Killed" && (string?)native["mutatorName"] == definition.Operator
                && (string?)native["replacement"] == definition.Replacement && JsonNode.DeepEquals(native["location"], JsonNode.Parse(definition.Location)), "Platform claim differs from its raw native outcome.");
            string[] killed = native["killedBy"]?.AsArray().Select(node => node!.GetValue<string>()).ToArray() ?? [];
            Require(killed.Length > 0 && killed.Distinct(StringComparer.Ordinal).Count() == killed.Length, "Platform native kill lacks unambiguous killedBy witnesses.");
            var file = report["testFiles"]!.AsObject().Single(file => LanguageReportPath(file.Key) == claim.WitnessSource);
            string source = (string?)file.Value!["source"] ?? "";
            Input pin = inputs.Single(input => input.Path == LanguagePrefix + claim.WitnessSource);
            byte[] bytes = Encoding.UTF8.GetBytes(source);
            Require(Digest(bytes) == pin.Sha256 || Digest([0xef, 0xbb, 0xbf, .. bytes]) == pin.Sha256, "Platform test witness source differs from the frozen candidate.");
            string[] witnesses = file.Value!["tests"]!.AsArray().Where(node => (string?)node!["name"] == claim.WitnessName
                && killed.Contains((string?)node!["id"], StringComparer.Ordinal)).Select(node => node!["id"]!.GetValue<string>()).ToArray();
            Require(witnesses.Length > 0 && witnesses.All(id => report["testFiles"]!.AsObject().SelectMany(file => file.Value!["tests"]!.AsArray()).Count(node => (string?)node!["id"] == id) == 1),
                "Platform proof lacks its exact current native product-test witness.");
        }
    }

    internal static void ValidatePlatformTools(string path, PlatformTool[] pins, Toolchain tools, Toolchain main, string producer, string helper)
    {
        Require(pins.Length > 0 && pins.Select(pin => pin.OriginalPath).Distinct(StringComparer.OrdinalIgnoreCase).Count() == pins.Length
            && pins.Select(pin => pin.Entry).Distinct(StringComparer.Ordinal).Count() == pins.Length, "Duplicate/missing platform tool closure.");
        Require(tools.Adapters.Length > 0 && tools.Adapters.SequenceEqual(main.Adapters), "Foreign native MSTest/VSTest adapter bytes differ from the already qualified payload.");
        Require(tools.Files.All(file => pins.Any(pin => pin.OriginalPath == file.Path && pin.Sha256 == file.Sha256))
            && tools.Adapters.All(file => pins.Any(pin => ForeignFileName(pin.OriginalPath) == file.Path && pin.Sha256 == file.Sha256)), "Platform tool/adapter archive omitted frozen inputs.");
        foreach (string name in new[] { "Stryker.CLI.dll", "Stryker.Core.dll" })
        {
            Require(tools.Files.Single(pin => ForeignFileName(pin.Path) == name).Sha256 == main.Files.Single(pin => Path.GetFileName(pin.Path) == name).Sha256,
                "Foreign native Stryker producer changed rather than executing the same pinned deployment.");
        }
        Require(tools.Files.Single(pin => ForeignFileName(pin.Path) == "CIS.DependencyChecker.dll").Sha256 == producer
            && tools.Files.Single(pin => ForeignFileName(pin.Path) == "CIS.Core.TestSupport.dll").Sha256 == helper, "Foreign export producer/helper differ from the frozen checker.");
        using ZipArchive archive = ZipFile.OpenRead(path);
        Require(archive.Entries.Count == pins.Length && archive.Entries.Sum(entry => entry.Length) <= OwnedLimit, "Platform tool archive membership/expanded size differs.");
        foreach (PlatformTool pin in pins)
        {
            Require(pin.Entry.Split('/').Length == 2 && pin.Entry.Split('/')[0].All(char.IsAsciiDigit)
                && pin.Entry.Split('/')[0].Length == 4 && pin.Entry.Split('/')[1] == ForeignFileName(pin.OriginalPath), "Unexpected platform tool archive path.");
            ZipArchiveEntry entry = archive.Entries.Single(entry => entry.FullName == pin.Entry);
            using Stream content = entry.Open();
            Require(entry.Length == pin.Bytes && Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(content)) == pin.Sha256, "A frozen platform tool/input changed in transport.");
        }
    }

    internal static void VerifyPlatformArchive(string path, Input[] inputs)
    {
        using ZipArchive archive = ZipFile.OpenRead(path);
        Require(archive.Entries.Count == inputs.Length && archive.Entries.Sum(entry => entry.Length) <= OwnedLimit, "Platform source archive membership/expanded size differs.");
        foreach (Input input in inputs)
        {
            ZipArchiveEntry entry = archive.Entries.Single(entry => entry.FullName == input.Path);
            using Stream source = entry.Open();
            Require(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(source)) == input.Sha256, "Platform source archive bytes differ: " + input.Path);
        }
    }

    private static string ForeignFileName(string path) => path.Replace('\\', '/').Split('/')[^1];
}
