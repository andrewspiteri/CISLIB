using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.TestSupport;
using static CIS.Verification.HealthcheckMutationUnionModel;

namespace CIS.Verification;

internal static partial class HealthcheckMutationUnionVerification
{
    internal static async Task<int> RunSharedAsync(string[] arguments)
    {
        var options = new TypeManagerVerificationInputs(arguments, ["repo-root", "manifest", "output-root"], []);
        var files = new PinnedInputs(options.PathOf("repo-root"), options.Output);
        JsonObject manifest = files.ReadDocument(options.PathOf("manifest"));
        SharedFields(manifest, ["schema", "outcomePolicy", "osLane", "verifier", "profiles"]);
        Need(String(manifest, "schema") == "cis.healthcheck.shared-mutation-inputs/v1"
            && String(manifest, "outcomePolicy") == "cis-native-completed/v1", "Select the prospective shared native-completed input policy.");
        string lane = String(manifest, "osLane");
        Need(lane == (OperatingSystem.IsWindows() ? "windows" : "ubuntu")
            && (OperatingSystem.IsWindows() || OperatingSystem.IsLinux()), "Verify each native OS lane on its own host.");
        Source[] sources = ReadSharedSources(files);
        JsonObject verifier = Item(manifest["verifier"]);
        SharedFields(verifier, ["dotnet", "assembly", "testingAssembly", "helperAssembly"]);
        string dotnet = files.Pin(Item(verifier["dotnet"]));
        string assembly = files.Pin(Item(verifier["assembly"]));
        string testing = files.Pin(Item(verifier["testingAssembly"]));
        string helper = files.Pin(Item(verifier["helperAssembly"]));
        Need(Path.GetDirectoryName(assembly) == Path.GetDirectoryName(testing)
            && Path.GetDirectoryName(testing) == Path.GetDirectoryName(helper), "Use one installed verifier/tooling directory.");
        files.GuardRoot(Path.GetDirectoryName(assembly)!);
        PinSharedVerifierDeployment(files, Path.GetDirectoryName(assembly)!);
        JsonObject[] descriptors = Items(manifest, "profiles").Select(Item).ToArray();
        Need(descriptors.Length is > 0 and <= 128, "Supply a bounded explicit set of original profile receipts.");
        Need(!manifest.ContainsKey("adjustments") && !manifest.ContainsKey("reviews"),
            "Supplied adjustment/approval flags cannot qualify survivors. Current independent review receiving remains separate.");
        options.EmptyOutput();
        var batches = new List<HealthcheckSharedMutationModel.Batch>();
        var observations = new JsonArray();
        var pending = new JsonArray();
        foreach (JsonObject descriptor in descriptors)
        {
            SharedFields(descriptor, ["snapshotRoot", "profile"], ["refinementProfiles"]);
            string snapshot = files.Resolve(String(descriptor, "snapshotRoot"));
            files.GuardRoot(snapshot);
            string profilePath = files.Pin(Item(descriptor["profile"]));
            Need(Inside(snapshot, profilePath), "The original profile must belong to its snapshot.");
            JsonObject profile = files.ReadDocument(profilePath);
            var requested = new List<HealthcheckSharedMutationProtocol.Profile>
            {
                new(Path.GetRelativePath(snapshot, profilePath).Replace('\\', '/'), Hash(profilePath), profile)
            };
            string[] sourceFiles = Items(profile, "sourceFiles").Select(node => node!.GetValue<string>()).ToArray();
            Need(sourceFiles.Length is > 0 and <= 46 && sourceFiles.Distinct(StringComparer.Ordinal).Count() == sourceFiles.Length
                && sourceFiles.All(path => path.StartsWith(CorePrefix, StringComparison.Ordinal)
                    && sources.Any(source => CorePrefix + source.Path == path)), "A profile has foreign or duplicate shipping scope.");
            ValidateSharedConfiguration(files, snapshot, profile, sources);
            var command = new List<string> { assembly, "test", "incremental", "verify", "--repo", snapshot,
                "--profile", Path.GetRelativePath(snapshot, profilePath), "--format", "json" };
            Need(!descriptor.ContainsKey("refinementProfiles") || descriptor["refinementProfiles"] is JsonArray,
                "Explicit refinement profiles must be an array.");
            foreach (JsonNode? pin in descriptor["refinementProfiles"] as JsonArray ?? [])
            {
                string rerun = files.Pin(Item(pin));
                Need(Inside(snapshot, rerun), "A refinement profile must belong to the same snapshot.");
                Need(requested.Count <= 32, "At most32 explicit refinement profiles are supported.");
                requested.Add(new(Path.GetRelativePath(snapshot, rerun).Replace('\\', '/'), Hash(rerun), files.ReadDocument(rerun)));
                command.Add("--refinement-profile"); command.Add(Path.GetRelativePath(snapshot, rerun));
            }
            foreach (var request in requested)
            {
                string state = Under(snapshot, ".cis/local/testing/incremental/" + String(request.Value, "id") + "/state.json");
                if (File.Exists(state)) { PinSharedCurrentFile(files, state); }
            }
            DateTimeOffset started = DateTimeOffset.UtcNow;
            ProcessResult execution = await ProcessRunner.RunAsync(dotnet, command, snapshot, TimeSpan.FromMinutes(10),
                outputEncoding: new UTF8Encoding(false, true), maximumCapturedBytesPerStream: 16384,
                maximumCapturedStandardOutputBytes: 1048576);
            DateTimeOffset finished = DateTimeOffset.UtcNow;
            Need(execution.OutputDrained && !execution.CaptureTruncated, "Shared verification did not return complete bounded output.");
            JsonObject receipt = HealthcheckSharedMutationProtocol.Parse(execution.StandardOutput);
            if (!HealthcheckSharedMutationProtocol.IsVerifiedResponse(receipt, execution.ExitCode))
            {
                pending.Add(new JsonObject
                {
                    ["profileSha256"] = Hash(profilePath),
                    ["reason"] = "Shared evidence is not current and verified.",
                    ["diagnostics"] = receipt["diagnostics"]?.DeepClone()
                });
                continue;
            }
            Need(String(receipt, "testingProducerSha256") == Hash(testing) && String(receipt, "helperProducerSha256") == Hash(helper)
                && String(receipt, "profileSha256") == Hash(profilePath), "Shared producer/profile bindings changed.");
            Need(String(receipt, "runtimeIdentifier").StartsWith(lane == "ubuntu" ? "linux-" : "win-", StringComparison.Ordinal),
                "Cross-platform evidence cannot be pooled.");
            var inputRecords = HealthcheckSharedMutationProtocol.Validate(receipt, requested.ToArray(), snapshot,
                testing, Hash(testing), helper, Hash(helper), lane, started, finished);
            string[] stale = CheckSharedCurrentInputs(files, snapshot, inputRecords, String(profile, "strykerConfigFile"));
            if (stale.Length > 0)
            {
                pending.Add(new JsonObject
                {
                    ["profileSha256"] = Hash(profilePath),
                    ["reason"] = "Relevant current source/test/build inputs differ.",
                    ["staleInputs"] = JsonSerializer.SerializeToNode(stale)
                });
                continue;
            }
            JsonObject[] verified = Items(receipt, "batches").Select(Item).ToArray();
            Need(verified.Select(batch => String(batch, "sourceFile")).ToHashSet(StringComparer.Ordinal).SetEquals(sourceFiles)
                && verified.Length == sourceFiles.Length, "Shared receipt is incomplete or duplicates its declared sources.");
            foreach (JsonObject batch in verified)
            {
                Need(batch["fullFile"]?.GetValue<bool>() == true && batch["fullPostflightReceiptVerified"]?.GetValue<bool>() == true,
                    "Qualification requires full-file native work and verified full-filter postflight receipts.");
                Source source = sources.Single(source => CorePrefix + source.Path == String(batch, "sourceFile"));
                string report = PinSharedReport(files, snapshot, batch, "mutationReportPath", "mutationReportSha256", "mutationReportBytes");
                PinSharedReport(files, snapshot, batch, "unitReportPath", "unitReportSha256", "unitReportBytes");
                Definition[] definitions = ParseFileReport(files.ReadDocument(report), source.Group, Hash(report),
                    Path.Combine(snapshot, CorePrefix).Replace('\\', '/').TrimEnd('/'), sources, source.Path);
                HealthcheckSharedMutationProtocol.ValidateRawPopulation(Item(batch["raw"]), definitions.Select(definition => definition.Status).ToArray());
                VerifySharedCreditWitnesses(files, snapshot, receipt, source, definitions, sources);
                string[] rerunKills = Items(receipt, "credits").Select(Item).Where(credit => String(credit, "sourceFile") == CorePrefix + source.Path)
                    .Select(credit => String(credit, "mutantId")).ToArray();
                var issues = new List<IntegrityIssue>();
                string[] nativeKills = definitions.Where(definition => definition.Status == "Killed").Select(definition => definition.Id).ToArray();
                if (source.Group is "pipes" or "aspnet")
                {
                    // Parent completion cannot attest a child loaded or activated
                    // the mutated body. Missing receiving support is an explicit
                    // hold, never a synthetic success or an assertion-text rule.
                    issues.Add(new("MissingSharedChildEvidence", "Current child-origin/body/terminal receipts are required for this process boundary.", []));
                    nativeKills = [];
                    Need(rerunKills.Length == 0, "Child refinement credit needs verified child evidence.");
                }
                batches.Add(new(source, Hash(report), definitions, nativeKills, rerunKills, issues.ToArray()));
            }
            // Re-pin the original/rerun raw reports independently. The verifier
            // has already checked their commands, test identities and chronology.
            foreach (JsonObject refinement in Items(receipt, "refinements").Select(Item))
            {
                foreach (JsonObject report in Items(refinement, "rerunReports").Select(Item))
                {
                    files.Pin(new JsonObject
                    {
                        ["path"] = Under(snapshot, String(report, "path")),
                        ["sha256"] = String(report, "sha256"),
                        ["bytes"] = Integer(report, "bytes")
                    });
                }
            }
            observations.Add(new JsonObject
            {
                ["profileSha256"] = Hash(profilePath),
                ["graphBuildId"] = receipt["graphBuildId"]?.DeepClone(),
                ["sharedReceiptSha256"] = TypeManagerVerificationInputs.Digest(execution.StandardOutputBytes.Span),
                ["sourceCount"] = verified.Length,
                ["historicalPostflightXmlRetained"] = false
            });
        }
        JsonObject result = HealthcheckSharedMutationModel.Evaluate(sources, batches.ToArray(), []);
        if (pending.Count > 0) { result["qualified"] = false; }
        result["osLane"] = lane;
        result["pendingProfiles"] = pending;
        result["sharedVerifications"] = observations;
        result["verifiedInputs"] = files.Inventory();
        result["scope"] = "Current HC12 mutation union only; no whole-Core or release approval.";
        result["createdUtc"] = DateTimeOffset.UtcNow.ToString("O");
        files.VerifyAgain();
        string output = Path.Combine(options.Output, "results.json");
        File.WriteAllText(output, result.ToJsonString(OutputOptions));
        Console.WriteLine(output);
        return result["qualified"]?.GetValue<bool>() == true ? 0 : 1;
    }

    private static void ValidateSharedConfiguration(PinnedInputs files, string snapshot, JsonObject profile, Source[] sources)
    {
        string[] paths = Items(profile, "sourceFiles").Select(node => node!.GetValue<string>()[CorePrefix.Length..]).ToArray();
        string[] groups = paths.Select(path => sources.Single(source => source.Path == path).Group).Distinct(StringComparer.Ordinal).ToArray();
        Need(groups.Length == 1, "One incremental profile must belong to one qualification partition.");
        string group = groups[0];
        string template = Under(snapshot, String(profile, "strykerConfigFile"));
        JsonObject native = files.ReadDocument(template);
        JsonObject maintained = files.ReadDocument(MaintainedConfig(files, group));
        HealthcheckSharedMutationConfiguration.Validate(group, Item(native["stryker-config"]), Item(maintained["stryker-config"]), paths);
        Need(String(profile, "fullTestFilter") == String(Item(maintained["stryker-config"]), "test-case-filter"),
            "The original profile must keep its complete maintained cohort filter.");
    }

    private static Source[] ReadSharedSources(PinnedInputs files)
    {
        var sources = new List<Source>();
        foreach (string group in Groups)
        {
            JsonObject config = files.ReadDocument(MaintainedConfig(files, group));
            foreach (JsonNode? node in Items(Item(config["stryker-config"]), "mutate"))
            {
                string relative = node!.GetValue<string>();
                string path = files.Resolve(CorePrefix + relative);
                files.Pin(new JsonObject { ["path"] = path, ["sha256"] = Hash(path), ["bytes"] = new FileInfo(path).Length });
                sources.Add(new(relative, Hash(path), group, ReadUtf8(path)));
            }
        }
        string healthRoot = files.Resolve(CorePrefix + "Healthcheck");
        files.TrackInventory(healthRoot, path => path.EndsWith(".cs", StringComparison.Ordinal));
        string[] actual = Directory.GetFiles(healthRoot, "*.cs", SearchOption.AllDirectories)
            .Select(path => "Healthcheck/" + Path.GetRelativePath(healthRoot, path).Replace('\\', '/')).Order(StringComparer.Ordinal).ToArray();
        Need(sources.Count == 46 && sources.GroupBy(source => source.Group).All(group => group.Count() == (group.Key == "unit-runtime" ? 41 : group.Key == "pipes" ? 4 : 1))
            && sources.Select(source => source.Path).Order(StringComparer.Ordinal).SequenceEqual(actual), "Maintained partitions must cover all46 current sources once.");
        return sources.ToArray();
    }

    private static string[] CheckSharedCurrentInputs(PinnedInputs files, string snapshot, HealthcheckSharedMutationProtocol.Input[] inputs, string nativeTemplate)
    {
        var stale = new List<string>();
        foreach (var input in inputs)
        {
            string path = input.Path, expected = input.Sha256;
            string original = Path.IsPathFullyQualified(path) ? path : Under(snapshot, path);
            files.Pin(new JsonObject { ["path"] = original, ["sha256"] = expected, ["bytes"] = new FileInfo(original).Length });
            if (!input.CompareCurrent || (path == nativeTemplate && input.Roles.Contains("configuration", StringComparer.Ordinal)
                && input.Roles.All(role => role is "configuration" or "dependency"))) { continue; }
            Need(!Path.IsPathFullyQualified(path), "Source/test/configuration closure must be repository-relative.");
            string current = files.Resolve(path);
            if (!File.Exists(current) || Hash(current) != expected) { stale.Add(path); continue; }
            files.Pin(new JsonObject { ["path"] = current, ["sha256"] = expected, ["bytes"] = new FileInfo(current).Length });
        }
        return stale.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    private static void VerifySharedCreditWitnesses(PinnedInputs files, string snapshot, JsonObject receipt, Source source,
        Definition[] originals, Source[] sources)
    {
        foreach (JsonObject credit in Items(receipt, "credits").Select(Item).Where(credit => String(credit, "sourceFile") == CorePrefix + source.Path))
        {
            string id = String(credit, "mutantId");
            Definition? original = originals.SingleOrDefault(definition => definition.Id == id);
            Need(original?.Status == "Timeout", "A refinement must bind an original native Timeout.");
            Need(String(credit, "definitionSha256") == HealthcheckSharedMutationProtocol.DefinitionSha256(source, original!),
                "Refinement credit has a stale native definition fingerprint.");
            string[] expectedTests = Items(credit, "killingTestIds").Select(node => node!.GetValue<string>()).ToArray();
            bool witnessed = false;
            foreach (JsonObject refinement in Items(receipt, "refinements").Select(Item))
            {
                foreach (JsonObject pin in Items(refinement, "rerunReports").Select(Item).Where(pin => String(pin, "role") == "mutation"
                    && String(pin, "sourceFile") == CorePrefix + source.Path))
                {
                    string path = files.Pin(new JsonObject
                    {
                        ["path"] = Under(snapshot, String(pin, "path")),
                        ["sha256"] = String(pin, "sha256"),
                        ["bytes"] = Integer(pin, "bytes")
                    });
                    Definition[] rerun = ParseFileReport(files.ReadDocument(path), source.Group, Hash(path),
                        Path.Combine(snapshot, CorePrefix).Replace('\\', '/').TrimEnd('/'), sources, source.Path);
                    witnessed |= rerun.Any(definition => definition.Id == id && definition.Identity == original!.Identity && definition.Status == "Killed"
                        && definition.KilledBy.ToHashSet(StringComparer.Ordinal).SetEquals(expectedTests));
                }
            }
            Need(witnessed, "A refinement has no exact current native rerun kill witness.");
        }
    }

    private static void PinSharedVerifierDeployment(PinnedInputs files, string root)
    {
        bool Relevant(string path) => Path.GetExtension(path) is ".dll" or ".json" or ".exe" or ".so" or ".dylib";
        files.TrackInventory(root, Relevant);
        string[] paths = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Where(Relevant).ToArray();
        Need(paths.Length is > 0 and <= 2048, "Shared verifier deployment inventory is empty or exceeds its bound.");
        foreach (string path in paths) { PinSharedCurrentFile(files, path); }
    }

    private static void PinSharedCurrentFile(PinnedInputs files, string path) => files.Pin(new JsonObject
    { ["path"] = path, ["sha256"] = Hash(path), ["bytes"] = new FileInfo(path).Length });

    private static void SharedFields(JsonObject value, string[] required, string[]? optional = null)
    {
        string[] allowed = [.. required, .. (optional ?? [])];
        Need(required.All(value.ContainsKey) && value.All(field => allowed.Contains(field.Key, StringComparer.Ordinal)),
            "Unexpected or missing shared manifest fields.");
    }

    private static string PinSharedReport(PinnedInputs files, string snapshot, JsonObject batch, string pathName, string hashName, string bytesName)
        => files.Pin(new JsonObject
        {
            ["path"] = Under(snapshot, String(batch, pathName)),
            ["sha256"] = String(batch, hashName),
            ["bytes"] = Integer(batch, bytesName)
        });

    private static string Under(string root, string relative)
    {
        Need(!Path.IsPathFullyQualified(relative) && !relative.Contains('\\') && !relative.Contains(':')
            && relative.Split('/').All(part => part.Length > 0 && part is not ("." or "..")), "Unsafe relative evidence path.");
        string full = Path.GetFullPath(Path.Combine(root, relative));
        Need(Inside(root, full), "Evidence path escaped its snapshot.");
        return full;
    }

    private static bool Inside(string root, string path) => Path.GetFullPath(path).StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)
        + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
