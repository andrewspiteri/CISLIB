using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using static CIS.Verification.TypeManagerVerificationInputs;

namespace CIS.Verification;

internal static partial class HealthcheckGroupedMutation
{
    internal sealed record Definition(string Source, string SourceSha256, string NativeId, string Identity, string Operator,
        string Location, string Replacement, string Status, string? StatusReason = null);
    internal sealed record Review(string CandidateIdentity, string DefinitionIdentity, string RawStatus, string Requirement,
        string Decision, string Reviewer, string Rationale, string ReviewedAtUtc)
    {
        public PlatformProofReference? PlatformProof { get; init; }
    }
    internal sealed record Authority(string Policy, string[] Reviewers);
    internal sealed record ChildControl(string Session, string Kind, int ActiveMutant, string NativeReport, string NativeReportSha256,
        string DefinitionIdentity, string NativeBinding, string NativeBindingSha256);
    internal sealed record SourcePopulation(string Source, int Definitions, int Eligible, int CompileError, int Ignored, int RuntimeError, int Pending, string Population);
    internal sealed record Assessment(int Killed, int Survived, int NoCoverage, int Timeout, int CompileError, int Ignored,
        decimal? ConservativeScore, bool CompleteScope, bool Qualified, string[] Holds)
    {
        public int RawEligibleDenominator { get; init; }
        public int ReviewedEquivalent { get; init; }
        public int EffectiveDenominator { get; init; }
        public decimal? RawConservativeScore { get; init; }
        public int RuntimeError { get; init; }
        public int Pending { get; init; }
        public bool SourceInventoryComplete { get; init; }
        public SourcePopulation[] SourcePopulations { get; init; } = [];
    }

    private static async Task<int> ReceiveAsync(string[] args)
    {
        var input = new TypeManagerVerificationInputs(args, ["repo-root", "candidate-root", "output-root"], ["reviews", "review-authority", "groups", "child-controls"]);
        input.EmptyOutput();
        string root = input.PathOf("candidate-root"), repo = input.PathOf("repo-root");
        Require(Path.GetFullPath(input.Output).StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal), "Receiving output must remain in this candidate's aggregate retained root.");
        Require(OwnedBytes(root, skipWorkspace: true) <= RetainedLimit, "Aggregate retained candidate evidence already exceeds 256 MiB.");
        Candidate candidate = CandidateAt(root);
        Toolchain tools = ReadToolchain(root, candidate);
        string toolchainHash = Hash(Path.Combine(root, "toolchain.json"));
        Require(Capture(repo).SequenceEqual(candidate.Inputs), "Current production/test/build/configuration inputs differ from the frozen candidate.");
        string[] groupNames = (input.Optional("groups") ?? "pilot,runtime,http,pipes").Split(',');
        Require(groupNames.Distinct(StringComparer.Ordinal).Count() == groupNames.Length, "Duplicate native groups cannot supply union credit.");
        Group[] selected = groupNames.Select(name => Groups(candidate).Single(group => group.Name == name)).ToArray();
        var definitions = new List<Definition>();
        var sources = new HashSet<string>(StringComparer.Ordinal);
        var holds = new List<string>();
        var reports = new JsonArray();
        foreach (Group group in selected)
        {
            foreach (string source in group.Sources) { Require(sources.Add(source), "Group scope overlaps: " + source); }
            string output = Path.Combine(root, "groups", group.Name);
            JsonObject run = Read(Path.Combine(output, "run.json"));
            Require((string?)run["schema"] == "cis.healthcheck.grouped-run/v1" && (string?)run["candidateIdentity"] == candidate.Identity
                && (string?)run["group"] == group.Name, "A native run belongs to another candidate/group.");
            Require(run["sources"]!.AsArray().Select(value => value!.GetValue<string>()).SequenceEqual(group.Sources, StringComparer.Ordinal), "Run source inventory differs from its stable group.");
            Require((string?)run["configSha256"] == Hash(Path.Combine(root, "profiles", group.Name + ".json")), "Native group configuration changed.");
            ValidateExecution(run);
            ValidateContext(run, group);
            ValidateSdkMsBuildEnvironment(run, tools);
            Require((string?)run["toolchainSha256"] == toolchainHash
                && (string?)run["strykerCliSha256"] == tools.Files.Single(pin => Path.GetFileName(pin.Path) == "Stryker.CLI.dll").Sha256
                && (string?)run["strykerCoreSha256"] == tools.Files.Single(pin => Path.GetFileName(pin.Path) == "Stryker.Core.dll").Sha256,
                "A native group used changed tool/SDK/runtime/adapter inputs after the correctness pilot.");
            Require((string?)run["platform"] == candidate.Platform && (string?)run["host"] == candidate.Host, "Native platform/host differs from the candidate.");
            string report = Path.Combine(output, "native/reports/mutation-report.json");
            Require((string?)run["reportSha256"] == Hash(report), "Native report changed after execution.");
            foreach (string project in group.Projects)
            {
                string name = Path.GetFileNameWithoutExtension(project);
                Input[] trxPins = run["nativeTrxPins"]!.Deserialize<Input[]>(Json)!;
                foreach (string phase in new[] { "baseline", "restored" })
                {
                    string file = phase + "-" + name + ".trx";
                    Require(trxPins.Single(pin => pin.Path == file).Sha256 == Hash(Path.Combine(output, file)), "Native baseline/postflight report changed.");
                }
                PassingTrx(Path.Combine(output, "baseline-" + name + ".trx"));
                PassingTrx(Path.Combine(output, "restored-" + name + ".trx"));
            }
            JsonObject nativeReport = Read(report);
            Definition[] groupDefinitions = Definitions(nativeReport, group.Sources, candidate.Inputs);
            JsonNode? linuxIdentity = ValidateLinuxIdentityAdmission(root, candidate, group, run, groupDefinitions, nativeReport,
                relative => CandidateSource(root, candidate, relative));
            definitions.AddRange(groupDefinitions);
            JsonArray asyncControls = ValidateTransportAsyncControls(group.Name, groupDefinitions, nativeReport, relative => CandidateSource(root, candidate, relative));
            reports.Add(new JsonObject
            {
                ["group"] = group.Name,
                ["path"] = report,
                ["sha256"] = Hash(report),
                ["seconds"] = run["seconds"]?.DeepClone(),
                ["outOfGroupNativeMarkers"] = nativeReport["files"]!.AsObject().Count - group.Sources.Length,
                ["peakOwnedBytes"] = run["peakOwnedBytes"]?.DeepClone(),
                ["ownedStorageScope"] = run["ownedStorageScope"]?.DeepClone(),
                ["storageVolumes"] = run["storageVolumes"]?.DeepClone(),
                ["linuxIdentityQualification"] = linuxIdentity,
                ["asyncControls"] = asyncControls
            });
        }
        var childControls = new JsonArray();
        if (input.Optional("child-controls") is string childPath)
        {
            ChildControl[] children = JsonSerializer.Deserialize<ChildControl[]>(File.ReadAllText(Path.GetFullPath(childPath)), Json)
                ?? throw new InvalidDataException("Missing actual child control inputs.");
            Require(children.Select(item => item.Kind).Order(StringComparer.Ordinal).SequenceEqual(new[] { "http-host", "probe" }.Order(StringComparer.Ordinal), StringComparer.Ordinal),
                "Bounded actual child controls must retain both HTTP entrypoint and probe propagation.");
            foreach (ChildControl child in children)
            {
                Require(child.ActiveMutant >= 0, "A real child control requires an actual selected native mutant.");
                var session = HealthcheckMutationChildren.HealthcheckMutationChildProtocol.Read<HealthcheckMutationChildren.HealthcheckMutationChildProtocol.Session>(Path.GetFullPath(child.Session));
                HealthcheckMutationNativeRun.Selection[] selection = ValidateChildBinding(child, session, candidate, tools);
                string[] childSources = selection.Select(item => item.Source).Order(StringComparer.Ordinal).ToArray();
                Definition[] childDefinitions = Definitions(Read(Path.GetFullPath(child.NativeReport)), childSources, candidate.Inputs);
                Definition activeDefinition = childDefinitions.Single(definition => definition.Identity == child.DefinitionIdentity
                    && definition.NativeId == child.ActiveMutant.ToString(System.Globalization.CultureInfo.InvariantCulture));
                string expectedBody = child.Kind == "http-host" ? "Healthcheck/AspNetCore/HealthcheckApplicationBuilderExtensions.cs" : "Healthcheck/Pipes/HealthProbeClient.cs";
                Require(activeDefinition.Source == expectedBody && activeDefinition.Status is "Killed" or "Survived", "Actual child controls require a completed definition in the shipping HTTP/probe body, not unrelated instrumentation.");
                ValidateChildDefinitionSelection(activeDefinition, selection, File.ReadAllText(Path.Combine(session.LanguageRoot, "src/CIS.Core", activeDefinition.Source)));
                string audit = Path.Combine(input.Output, "children", child.Kind);
                int code = await HealthcheckChildMutationVerification.RunAsync(["audit", "--session", Path.GetFullPath(child.Session), "--output", audit,
                    "--require-kind", child.Kind, "--require-active", child.ActiveMutant.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
                Require(code == 0, "Actual child mutation propagation/selector/lifecycle audit failed.");
                childControls.Add(new JsonObject
                {
                    ["kind"] = child.Kind,
                    ["activeMutant"] = child.ActiveMutant,
                    ["sessionSha256"] = Hash(Path.GetFullPath(child.Session)),
                    ["nativeReportSha256"] = child.NativeReportSha256,
                    ["definitionIdentity"] = child.DefinitionIdentity,
                    ["nativeBindingSha256"] = child.NativeBindingSha256,
                    ["selection"] = JsonSerializer.SerializeToNode(selection, Json),
                    ["auditSha256"] = Hash(Path.Combine(audit, "results.json"))
                });
            }
        }
        else { holds.Add("Current bounded actual HTTP and probe child-mutation controls remain unexecuted."); }
        Review[] reviews = [];
        Authority authority = new("No review authority supplied", []);
        if (input.Optional("reviews") is string reviewsPath)
        {
            Require(input.Optional("review-authority") is not null, "Miss reviews require an explicit maintained review authority input.");
            string authorityPath = input.PathOf("review-authority");
            string relative = Path.GetRelativePath(repo, authorityPath).Replace('\\', '/');
            Require(!relative.StartsWith("../", StringComparison.Ordinal) && candidate.Inputs.Any(pin => pin.Path == relative && pin.Sha256 == Hash(authorityPath)),
                "Review authority must be a maintained input frozen in this candidate, not a supplied approval flag.");
            authority = JsonSerializer.Deserialize<Authority>(File.ReadAllText(authorityPath), Json) ?? throw new InvalidDataException("Invalid review authority.");
            reviews = JsonSerializer.Deserialize<Review[]>(File.ReadAllText(reviewsPath), Json) ?? throw new InvalidDataException("Invalid miss reviews.");
        }
        PlatformAdmission[] platformAdmissions = reviews.Where(review => review.Decision == "PlatformCovered")
            .GroupBy(review => review.PlatformProof).SelectMany(group => ReadPlatformAdmissions(root, candidate, tools, definitions.ToArray(), group.ToArray())).ToArray();
        Assessment assessment = Assess(candidate, sources, definitions.ToArray(), reviews, authority, holds, platformAdmissions);
        var results = new JsonObject
        {
            ["schema"] = "cis.healthcheck.grouped-acceptance/v1",
            ["candidateIdentity"] = candidate.Identity,
            ["candidateArchiveSha256"] = candidate.ArchiveSha256,
            ["platform"] = candidate.Platform,
            ["host"] = candidate.Host,
            ["currentSourceInventory"] = JsonSerializer.SerializeToNode(candidate.Sources),
            ["receivedSources"] = JsonSerializer.SerializeToNode(sources.Order(StringComparer.Ordinal)),
            ["reports"] = reports,
            ["assessment"] = JsonSerializer.SerializeToNode(assessment, Json),
            ["childControls"] = childControls,
            ["reviews"] = JsonSerializer.SerializeToNode(reviews, Json),
            ["reviewAuthority"] = JsonSerializer.SerializeToNode(authority, Json),
            ["platformProofs"] = JsonSerializer.SerializeToNode(platformAdmissions, Json),
            ["productImplemented"] = "separate product status",
            ["releaseQualified"] = false
        };
        WriteRetained(root, Path.Combine(input.Output, "results.json"), results.ToJsonString(Json));
        WriteRetained(root, Path.Combine(input.Output, "misses.json"), JsonSerializer.Serialize(definitions.Where(item => item.Status != "Killed"), Json));
        if (!assessment.CompleteScope) { WriteRetained(root, Path.Combine(input.Output, "definitions.json"), JsonSerializer.Serialize(definitions, Json)); }
        Console.WriteLine($"Grouped acceptance: {sources.Count}/{candidate.Sources.Length} sources; {definitions.Count} definitions; score {assessment.ConservativeScore?.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) ?? "pending incomplete scope"}; qualified {assessment.Qualified}.");
        return assessment.Qualified ? 0 : 1;
    }

    internal static HealthcheckMutationNativeRun.Selection[] ValidateChildBinding(ChildControl child, HealthcheckMutationChildren.HealthcheckMutationChildProtocol.Session session,
        Candidate candidate, Toolchain tools)
    {
        bool Same(string left, string right) => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        void Pin(JsonNode? value, string path, string hash)
        {
            Require(value is JsonObject && Same((string?)value["path"] ?? "", path) && (string?)value["sha256"] == hash
                && File.Exists(path) && Hash(path) == hash && (long?)value["bytes"] == new FileInfo(path).Length,
                "A child native invocation pin differs from its actual session/configuration/source/tool input: " + path);
        }
        string bindingPath = Path.GetFullPath(child.NativeBinding);
        Require(Hash(bindingPath) == child.NativeBindingSha256, "The actual child-control native binding changed.");
        JsonObject binding = Read(bindingPath);
        string schema = (string?)binding["schema"] ?? "";
        Require(schema is "cis.healthcheck.shared-child-attempt/v1" or "cis.healthcheck.shared-child-attempt/v2" or "cis.healthcheck.shared-child-attempt/v3"
            && (string?)binding["phase"] == "mutation" && Same((string?)binding["languageRoot"] ?? "", session.LanguageRoot)
            && Same((string?)binding["workingDirectory"] ?? "", Path.Combine(session.LanguageRoot, "src/CIS.Core")),
            "Child controls require the existing native invocation binding for this maintained language root.");
        Require(session.SourceReplay is null, "Manual source replay cannot supply an actual child native control.");
        string childRepo = Path.GetFullPath(Path.Combine(session.LanguageRoot, "../.."));
        Require(Capture(childRepo).SequenceEqual(candidate.Inputs), "Child native source/test/build/configuration inputs differ from the frozen candidate.");
        Input[] sourceInputs = candidate.Inputs.Where(pin => pin.Path.StartsWith(LanguagePrefix, StringComparison.Ordinal)
            && (Path.GetExtension(pin.Path) is ".cs" or ".csproj" or ".props" or ".targets"
                || Path.GetFileName(pin.Path) is "global.json" or "Directory.Packages.props" or "packages.lock.json" or "toolchain.json"))
            .Select(pin => pin with { Path = pin.Path[LanguagePrefix.Length..] }).ToArray();
        Require(session.SourceInputs.Select(pin => new Input(pin.Path, pin.Sha256)).SequenceEqual(sourceInputs), "Child session source/test/build inventory is incomplete or belongs to another candidate.");
        Pin(binding["session"], Path.GetFullPath(child.Session), Hash(child.Session));
        Pin(binding["config"], session.ConfigurationPath, session.ConfigurationSha256);
        JsonObject native = binding["native"]!.AsObject();
        string output = Path.GetFullPath((string?)binding["outputRoot"] ?? "");
        Require((int?)native["exitCode"] == 0 && Same(child.NativeReport, Path.Combine(output, "reports/mutation-report.json"))
            && (string?)native["reportSha256"] == child.NativeReportSha256 && Hash(child.NativeReport) == child.NativeReportSha256
            && (long?)native["reportBytes"] == new FileInfo(child.NativeReport).Length,
            "Child native control must bind the exact unchanged report of one successful native invocation.");
        JsonObject producer = binding["tools"]!.AsObject();
        foreach (string name in new[] { "dotnet", "strykerCli", "strykerCore", "wrapper", "helper", "wrapperDeps", "wrapperRuntimeConfig" })
        {
            JsonNode value = producer[name]!;
            string path = (string?)value["path"] ?? "";
            Input frozen = tools.Files.SingleOrDefault(pin => Same(pin.Path, path))
                ?? throw new InvalidDataException("Child native invocation used a tool outside the frozen candidate toolchain: " + name);
            Pin(value, frozen.Path, frozen.Sha256);
        }
        Require(Same((string)producer["dotnet"]!["path"]!, tools.ToolDotnet) && Same(session.DotnetPath, tools.SubjectDotnet)
            && (string?)producer["dotnet"]!["sha256"] == session.DotnetSha256
            && Same((string)producer["strykerCli"]!["path"]!, tools.Files.Single(pin => Path.GetFileName(pin.Path) == "Stryker.CLI.dll").Path)
            && Same((string)producer["strykerCore"]!["path"]!, tools.Files.Single(pin => Path.GetFileName(pin.Path) == "Stryker.Core.dll").Path)
            && Same((string)producer["strykerCore"]!["path"]!, session.StrykerCorePath)
            && (string?)producer["strykerCore"]!["sha256"] == session.StrykerCoreSha256
            && tools.Files.Any(pin => Same(pin.Path, session.CoreLibraryPath) && pin.Sha256 == session.CoreLibrarySha256),
            "Child session host/runtime/Stryker pins differ from its native invocation and frozen toolchain.");
        HealthcheckMutationNativeRun.Selection[] selected = HealthcheckMutationNativeRun.ReadSelections(
            Read(session.ConfigurationPath)["stryker-config"]!["mutate"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray(), Path.Combine(session.LanguageRoot, "src/CIS.Core"));
        JsonArray sources = binding["sources"]!.AsArray();
        Require(sources.Count == selected.Length,
            "Child native binding source list differs from its exact configuration.");
        for (int index = 0; index < selected.Length; index++)
        {
            Input pin = candidate.Inputs.SingleOrDefault(pin => pin.Path == CorePrefix + selected[index].Source)
                ?? throw new InvalidDataException("Child native source is outside the frozen Core inventory.");
            Pin(sources[index], Path.Combine(session.LanguageRoot, "src/CIS.Core", selected[index].Source), pin.Sha256);
        }
        bool spans = selected.Any(item => item.Start is not null);
        Require(schema.EndsWith("/v3", StringComparison.Ordinal) ? spans && sources.Count == 2 && binding["source"] is null
                && JsonNode.DeepEquals(binding["selection"], JsonSerializer.SerializeToNode(selected, Json))
            : !spans && !binding.ContainsKey("selection") && (schema.EndsWith("/v1", StringComparison.Ordinal)
                ? sources.Count == 1 && JsonNode.DeepEquals(binding["source"], sources[0]) : sources.Count > 1 && binding["source"] is null),
            "Child binding source-list/span version semantics differ.");
        string directory = Path.GetDirectoryName(bindingPath)!;
        string preparedPath = Path.Combine(directory, "prepared.json"), startedPath = Path.Combine(directory, "started.json");
        Pin(binding["phases"]!["prepared"], preparedPath, Hash(preparedPath));
        Pin(binding["phases"]!["started"], startedPath, Hash(startedPath));
        JsonObject prepared = Read(preparedPath), started = Read(startedPath);
        Require((string?)prepared["schema"] == schema.Replace("attempt/v", "attempt-prepared/v", StringComparison.Ordinal)
            && (string?)started["schema"] == "cis.healthcheck.shared-child-attempt-started/v1"
            && (int?)native["processId"] is > 0 && (int?)started["processId"] == (int?)native["processId"]
            && JsonNode.DeepEquals(started["startedUtc"], native["startedUtc"]), "Child native binding lacks its exact prepared/started producer phases.");
        foreach (string name in new[] { "profileId", "attemptId", "phase", "languageRoot", "outputRoot", "workingDirectory", "config", "session", "source", "sources", "selection", "tools" })
        { Require(JsonNode.DeepEquals(binding[name], prepared[name]), "Prepared child invocation differs from its completed binding: " + name); }
        Pin(started["prepared"], preparedPath, Hash(preparedPath));
        Require(JsonNode.DeepEquals(native["command"], started["command"]) && JsonNode.DeepEquals(native["command"], prepared["command"]),
            "Child native prepared/started/completed commands differ.");
        string[] command = native["command"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();
        HealthcheckMutationNativeRun.NoSelectionOverrides(command.Skip(2).ToArray());
        if (spans) { HealthcheckMutationNativeRun.BoundedControlArguments(command.Skip(2).ToArray()); }
        string Option(string name)
        {
            int[] positions = command.Select((value, index) => (value, index)).Where(pair => pair.value == name).Select(pair => pair.index).ToArray();
            Require(positions.Length == 1 && positions[0] + 1 < command.Length, "Child native command lacks an exact option: " + name);
            return command[positions[0] + 1];
        }
        Require(command.Length > 2 && Same(command[0], tools.ToolDotnet) && Same(command[1], (string)producer["strykerCli"]!["path"]!)
            && Same(Option("--config-file"), session.ConfigurationPath) && Same(Option("--output"), output),
            "Child native command is not the exact bound tool/configuration/report invocation.");
        return selected;
    }

    internal static void ValidateChildDefinitionSelection(Definition definition, HealthcheckMutationNativeRun.Selection[] selected, string source)
    {
        HealthcheckMutationNativeRun.Selection selection = selected.Single(item => item.Source == definition.Source);
        if (selection.Start is null) { return; }
        JsonObject location = JsonNode.Parse(definition.Location)!.AsObject();
        int Offset(string field)
        {
            int line = (int?)location[field]?["line"] ?? 0, column = (int?)location[field]?["column"] ?? 0;
            Require(line > 0 && column > 0, "Native child definition needs positive UTF-16 line/column positions.");
            int offset = 0;
            for (int current = 1; current < line; current++)
            { int next = source.IndexOf('\n', offset); Require(next >= 0, "Native child definition exceeds its actual source lines."); offset = next + 1; }
            int end = source.IndexOf('\n', offset); if (end < 0) { end = source.Length; }
            if (end > offset && source[end - 1] == '\r') { end--; }
            Require(column - 1 <= end - offset, "Native child definition exceeds its actual UTF-16 source column.");
            return offset + column - 1;
        }
        int start = Offset("start"), finish = Offset("end");
        Require(definition.Status is "Killed" or "Survived" && start < finish && start >= selection.Start && finish <= selection.End,
            "Actual child activation must identify a completed native definition wholly inside its explicitly selected UTF-16 control span.");
    }

    internal static void ValidateExecution(JsonObject run)
    {
        Require((int?)run["nativeConcurrency"] == NativeConcurrency && (int?)run["processorCount"] is >= NativeConcurrency,
            "The native run does not establish the declared worker concurrency and available processor prerequisite.");
        Require((int?)run["nativeExitCode"] == 0 && (bool?)run["restored"] == true && run["failure"] is null,
            "Infrastructure/initialization/abort/cleanup failure cannot supply product kill credit, even with an all-Killed native report.");
        Require((string?)run["cleanup"] == "native scratch removed; candidate workspace retained for next group", "Native scratch cleanup is unverified.");
        Require((bool?)run["processCleanupHealthy"] == true && (string?)run["temporaryCleanup"] == "owned short temp removed", "Owned native process/temp cleanup is unverified.");
        Require((long?)run["peakOwnedBytes"] is >= 0 and <= OwnedLimit, "Owned storage budget was exceeded or not measured.");
        JsonArray commands = run["commandLog"]!.AsArray();
        Require(commands.Count > 0 && commands.All(item => (int?)item!["exitCode"] == 0 && (bool?)item["outputDrained"] == true), "A native command or process propagation/cleanup failed.");
    }

    internal static void ValidateContext(JsonObject run, Group group)
    {
        Require(JsonNode.DeepEquals(run["buildProperties"], JsonSerializer.SerializeToNode(group.BuildProperties)), "Native group build context differs from its frozen recipe.");
        Require(run["environment"] is JsonObject, "Actual native compilation environment is missing.");
        JsonObject environment = run["environment"]!.AsObject();
        Require(group.BuildProperties.All(property => (string?)environment[property.Key] == property.Value), "Actual native environment differs from the declared test compilation context.");
    }

    internal static Definition[] Definitions(JsonObject report, string[] expectedSources, Input[] inputs)
    {
        var definitions = new List<Definition>();
        var actual = new HashSet<string>(StringComparer.Ordinal);
        foreach ((string path, JsonNode? item) in report["files"]!.AsObject())
        {
            string normalized = path.Replace('\\', '/');
            const string marker = "/src/CIS.Core/";
            int core = normalized.LastIndexOf(marker, StringComparison.Ordinal);
            string source = core >= 0 ? normalized[(core + marker.Length)..] : normalized;
            Input pin = inputs.SingleOrDefault(value => value.Path == CorePrefix + source)
                ?? throw new InvalidDataException("Native report contains a source outside the frozen Core inventory: " + path);
            if (!expectedSources.Contains(source, StringComparer.Ordinal))
            {
                // Pinned 4.16.0 reports all project leaves. Excluded leaves carry this exact native marker.
                // This is outside one group; the complete disjoint union must still cover every Healthcheck source.
                Require((string?)item?["language"] == "none" && (string?)item?["source"] == "File ignored by mutate filter"
                    && item?["mutants"] is JsonArray { Count: 0 }, "Unexpected nonselected native source or executable exclusion: " + source);
                continue;
            }
            Require(actual.Add(source), "Duplicate native source: " + source);
            string text = item!["source"]!.GetValue<string>();
            // Candidate files and Stryker report text are UTF-8. Accept only BOM removal by the native reader.
            string sourceHash = Digest(Encoding.UTF8.GetBytes(text));
            Require(sourceHash == pin.Sha256 || Digest([0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes(text)]) == pin.Sha256,
                "Raw native report source differs from the candidate: " + source);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonNode? mutant in item["mutants"]!.AsArray())
            {
                string id = mutant!["id"]!.GetValue<string>(), op = mutant["mutatorName"]!.GetValue<string>(),
                    location = mutant["location"]!.ToJsonString(), replacement = (string?)mutant["replacement"] ?? "", status = mutant["status"]!.GetValue<string>();
                Require(ids.Add(id), "Duplicate native mutation ID in one source.");
                Require(status is "Killed" or "Survived" or "NoCoverage" or "Timeout" or "CompileError" or "Ignored" or "RuntimeError" or "Pending", "Unknown raw native status: " + status);
                string identity = Digest(JsonSerializer.SerializeToUtf8Bytes(new { source, sourceSha256 = pin.Sha256, op, location, replacement }));
                definitions.Add(new(source, pin.Sha256, id, identity, op, location, replacement, status, (string?)mutant["statusReason"]));
            }
        }
        Require(actual.SetEquals(expectedSources), "The native report omitted scope, including a possible zero-definition file.");
        Require(definitions.Select(item => item.Identity).Distinct(StringComparer.Ordinal).Count() == definitions.Count, "Duplicate native mutation definitions cannot supply union credit.");
        return definitions.ToArray();
    }

    internal static Assessment Assess(Candidate candidate, IEnumerable<string> receivedSources, Definition[] definitions, Review[] reviews, Authority authority,
        IEnumerable<string>? executionHolds = null, PlatformAdmission[]? platformAdmissions = null)
    {
        var holds = new List<string>(executionHolds ?? []);
        string[] received = receivedSources.Order(StringComparer.Ordinal).ToArray();
        bool inventoryComplete = received.SequenceEqual(candidate.Sources.Order(StringComparer.Ordinal), StringComparer.Ordinal);
        if (!inventoryComplete) { holds.Add("Complete current Healthcheck source inventory has not executed."); }
        Require(reviews.Select(row => row.DefinitionIdentity).Distinct(StringComparer.Ordinal).Count() == reviews.Length, "Duplicate miss reviews.");
        foreach (Review review in reviews)
        {
            Definition definition = definitions.Single(item => item.Identity == review.DefinitionIdentity);
            Require(review.CandidateIdentity == candidate.Identity && review.RawStatus == definition.Status, "Review belongs to another candidate or native outcome.");
            Require(definition.Status != "Killed" && review.Decision is "Equivalent" or "ReviewedNoncritical" or "NonTermination" or "ReviewedTimeout" or "PlatformCovered", "Invalid prospective miss decision.");
            bool timeoutReview = review.Decision is "NonTermination" or "ReviewedTimeout";
            Require(timeoutReview || definition.Status is "Survived" or "NoCoverage", "Equivalent/noncritical reviews require an actual Survived or NoCoverage outcome.");
            Require(!timeoutReview || definition.Status == "Timeout", "Timeout review requires an actual Timeout outcome; it supplies no kill or denominator credit.");
            Require(review.Decision != "PlatformCovered" || definition.Status is "Survived" or "NoCoverage"
                && review.PlatformProof is not null && platformAdmissions?.Count(proof => proof.DefinitionIdentity == definition.Identity
                    && proof.ProofSha256 == review.PlatformProof.Sha256) == 1,
                "PlatformCovered needs an actually validated current native proof, without changing the chosen-lane outcome or denominator.");
            Require(review.Decision == "PlatformCovered" || review.PlatformProof is null, "Other dispositions cannot carry an unvalidated platform proof.");
            Require(!string.IsNullOrWhiteSpace(authority.Policy) && authority.Reviewers.Contains(review.Reviewer, StringComparer.Ordinal)
                && !string.IsNullOrWhiteSpace(review.Requirement) && review.Rationale.Trim().Length >= 20
                && DateTimeOffset.TryParse(review.ReviewedAtUtc, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out _),
                "Miss decisions need actual delegated reviewer identity, requirement, dated rationale and maintained authority; an approval Boolean is insufficient.");
        }
        foreach (Definition definition in definitions.Where(item => item.Status is "Survived" or "NoCoverage" or "Timeout"))
        {
            if (!reviews.Any(review => review.DefinitionIdentity == definition.Identity)) { holds.Add("Unreviewed " + definition.Status + ": " + definition.Source + "#" + definition.NativeId); }
        }
        int Count(string status) => definitions.Count(item => item.Status == status);
        int killed = Count("Killed"), survived = Count("Survived"), noCoverage = Count("NoCoverage"), timeout = Count("Timeout"), compile = Count("CompileError"), ignored = Count("Ignored");
        int runtime = Count("RuntimeError"), pending = Count("Pending");
        int unknown = definitions.Count(definition => definition.Status is not ("Killed" or "Survived" or "NoCoverage" or "Timeout" or "CompileError" or "Ignored" or "RuntimeError" or "Pending"));
        if (unknown > 0) { holds.Add("Unknown native mutation outcomes remain unresolved."); }
        holds.AddRange(NativePopulationHolds(definitions));
        if (runtime > 0) { holds.Add("Native runtime/infrastructure errors remain unresolved."); }
        if (pending > 0) { holds.Add("Native mutation execution is incomplete."); }
        bool complete = inventoryComplete && runtime == 0 && pending == 0 && unknown == 0;
        int rawDenominator = killed + survived + noCoverage + timeout;
        int equivalent = reviews.Count(review => review.Decision == "Equivalent");
        int denominator = rawDenominator - equivalent;
        if (denominator == 0) { holds.Add("No eligible mutation definitions remain for a full-scope gate."); }
        decimal? score = complete && denominator > 0 ? 100m * killed / denominator : null;
        if (score is < 90) { holds.Add("The conservative complete-scope score is below 90%."); }
        // Preserve the adopted eligible denominator. Only a genuine Equivalent removes an eligible miss;
        // reviewed noncritical misses and every Timeout remain, and no review invents a kill.
        return new(killed, survived, noCoverage, timeout, compile, ignored, score, complete, complete && score is >= 90 && holds.Count == 0, holds.ToArray())
        {
            RawEligibleDenominator = rawDenominator,
            ReviewedEquivalent = equivalent,
            EffectiveDenominator = denominator,
            RawConservativeScore = complete && rawDenominator > 0 ? 100m * killed / rawDenominator : null,
            RuntimeError = runtime,
            Pending = pending,
            SourceInventoryComplete = inventoryComplete,
            SourcePopulations = received.Select(source =>
            {
                Definition[] population = definitions.Where(definition => definition.Source == source).ToArray();
                int eligible = population.Count(definition => Eligible(definition.Status));
                return new SourcePopulation(source, population.Length, eligible,
                    population.Count(definition => definition.Status == "CompileError"), population.Count(definition => definition.Status == "Ignored"),
                    population.Count(definition => definition.Status == "RuntimeError"), population.Count(definition => definition.Status == "Pending"),
                    population.Length == 0 ? "zero definitions" : population.Any(definition => definition.Status is "RuntimeError" or "Pending") ? "native execution incomplete or failed"
                        : eligible == 0 ? "native nonviable or ignored definitions only" : "eligible definitions present");
            }).ToArray()
        };
    }

    private static bool Eligible(string status) => status is "Killed" or "Survived" or "NoCoverage" or "Timeout";

    private static string[] NativePopulationHolds(Definition[] definitions)
    {
        var holds = new List<string>();
        foreach (Definition definition in definitions.Where(item => item.Status == "CompileError"))
        {
            if (definition.StatusReason != "Mutant caused compile errors")
            { holds.Add("Unresolved native compiler/nonviable outcome: " + definition.Source + "#" + definition.NativeId); }
        }
        foreach (Definition definition in definitions.Where(item => item.Status == "Ignored"))
        {
            bool redundant = definition.Operator == "Block removal mutation" && definition.StatusReason == "Removed by block already covered filter"
                && definitions.Any(other => other.Source == definition.Source && Eligible(other.Status) && Contains(definition.Location, other.Location));
            if (!redundant) { holds.Add("Unresolved native exclusion: " + definition.Source + "#" + definition.NativeId); }
        }
        return holds.ToArray();
    }

    private static bool Contains(string outerLocation, string innerLocation)
    {
        (long Start, long End) Span(string text)
        {
            JsonObject location = JsonNode.Parse(text)!.AsObject();
            long Point(string field)
            {
                int line = location[field]!["line"]!.GetValue<int>(), column = location[field]!["column"]!.GetValue<int>();
                Require(line > 0 && column > 0, "Native mutation span must use positive line/column positions.");
                return ((long)line << 32) | (uint)column;
            }
            long start = Point("start"), end = Point("end");
            Require(end > start, "Native mutation span is empty or reversed.");
            return (start, end);
        }
        var outer = Span(outerLocation); var inner = Span(innerLocation);
        return outer.Start <= inner.Start && outer.End >= inner.End;
    }
}
