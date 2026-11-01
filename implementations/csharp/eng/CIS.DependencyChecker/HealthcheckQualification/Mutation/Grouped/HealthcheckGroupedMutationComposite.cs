using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CIS.TestSupport;
using static CIS.Verification.TypeManagerVerificationInputs;

namespace CIS.Verification;

internal static partial class HealthcheckGroupedMutation
{
    private const string CompositePolicyPath = LanguagePrefix + "verification/mutation/healthcheck/composite-policy.json";
    private sealed record CompositeGroup(Candidate Candidate, Group Group, Toolchain Tools, JsonObject Run,
        JsonObject Report, Definition[] Definitions, string Directory, string RunPath, string ReportPath);
    private static readonly IReadOnlyDictionary<string, string> CompositePlatforms = new Dictionary<string, string>(StringComparer.Ordinal)
    { ["pilot"] = "linux", ["runtime"] = "linux", ["http"] = "linux", ["pipes"] = "windows" };

    // This is an additional mode of the maintained receiver. The original v1
    // same-host route and the historical frozen analysis bridge are unchanged.
    private static async Task<int> ReceiveCompositeAsync(TypeManagerVerificationInputs input)
    {
        Require(new[] { "reviews", "review-authority", "groups", "child-controls" }.All(name => input.Optional(name) is null),
            "Composite origins, reviews and controls must come from the one explicit manifest.");
        Require(input.Optional("policy") is not null, "Composite receiving requires the maintained candidate policy.");
        string root = input.PathOf("candidate-root"), repo = input.PathOf("repo-root");
        Require(AtOrUnder(root, input.Output) && !AtOrUnder(Path.Combine(root, "workspace"), input.Output), "Keep receiving output in this candidate's retained scope.");
        input.EmptyOutput();
        var pins = new Dictionary<string, string>(StringComparer.Ordinal);
        void Pin(string path) { path = Path.GetFullPath(path); string hash = Hash(path); Require(!pins.TryGetValue(path, out string? previous) || previous == hash, "A receiving input changed."); pins[path] = hash; }
        JsonObject ReadPinned(string path) { Pin(path); JsonObject value = Read(path); Require(Hash(path) == pins[Path.GetFullPath(path)], "A receiving input changed while reading."); return value; }
        Candidate CandidatePinned(string directory)
        {
            Pin(Path.Combine(directory, "candidate.json")); Pin(Path.Combine(directory, "candidate.zip"));
            Candidate value = CandidateAt(directory); VerifyPlatformArchive(Path.Combine(directory, "candidate.zip"), value.Inputs); return value;
        }
        Candidate anchor = CandidatePinned(root);
        Require(Capture(repo).SequenceEqual(anchor.Inputs), "Current production/test/tool/policy inputs differ from the frozen candidate.");
        VerifyInputs(Path.Combine(root, "workspace"), anchor); ValidatePilot(root, anchor);
        Toolchain tools = ReadToolchain(root, anchor); Pin(Path.Combine(root, "toolchain.json"));
        ValidateCompositeTools(tools);
        foreach (Input tool in tools.Files) { Pin(tool.Path); }
        string policyPath = input.PathOf("policy");
        Require(Path.GetFullPath(Path.Combine(repo, CompositePolicyPath)) == policyPath
            && anchor.Inputs.Single(pin => pin.Path == CompositePolicyPath).Sha256 == Hash(policyPath),
            "The receiving policy must be the actual maintained input frozen in this candidate.");
        JsonObject policy = ReadPinned(policyPath); ValidateCompositePolicy(anchor, policy, relative => CandidateSource(root, anchor, relative));
        JsonObject manifest = ReadPinned(input.PathOf("composite-manifest"));
        Require((string?)manifest["schema"] == "cis.healthcheck.composite-native-inputs/v3", "Use an explicit prospective composite manifest.");
        JsonArray origins = manifest["groups"]!.AsArray(); ValidateCompositeSelection(origins);
        var groups = new List<CompositeGroup>();
        foreach (JsonObject origin in origins.OfType<JsonObject>())
        {
            string name = (string)origin["group"]!;
            groups.Add((string?)origin["platform"] == "windows"
                ? ReadCompositeWindows(root, anchor, tools, CompositePath(origin, "proof"), CandidatePinned, ReadPinned, Pin)
                : ReadCompositeLocal(anchor, name, CompositePath(origin, "candidateRoot"), CandidatePinned, ReadPinned, Pin));
        }
        string[] received = groups.SelectMany(group => group.Group.Sources).ToArray();
        Require(received.Distinct(StringComparer.Ordinal).Count() == received.Length, "Selected whole-group scopes overlap.");
        bool completeScope = received.Order(StringComparer.Ordinal).SequenceEqual(anchor.Sources.Order(StringComparer.Ordinal), StringComparer.Ordinal);
        string authorityPath = CompositePath(manifest, "reviewAuthority"); Pin(authorityPath);
        string authorityRelative = Path.GetRelativePath(repo, authorityPath).Replace('\\', '/');
        Require(anchor.Inputs.Any(pin => pin.Path == authorityRelative && pin.Sha256 == Hash(authorityPath))
            && authorityRelative == LanguagePrefix + "verification/mutation/healthcheck/review-authority.json", "Review authority must be the maintained frozen authority.");
        Authority authority = JsonSerializer.Deserialize<Authority>(File.ReadAllText(authorityPath), Json)!;
        var reviews = new List<Review>();
        foreach (JsonNode? file in manifest["reviewFiles"]!.AsArray())
        {
            string path = CompositeAbsolute(file!.GetValue<string>()); Pin(path);
            reviews.AddRange(JsonSerializer.Deserialize<Review[]>(File.ReadAllText(path), Json)!);
        }
        Require(reviews.Select(row => row.DefinitionIdentity).Distinct(StringComparer.Ordinal).Count() == reviews.Count,
            "Duplicate reviews cannot supply union credit.");
        foreach (Review review in reviews)
        { Require(groups.Count(group => group.Candidate.Identity == review.CandidateIdentity && group.Definitions.Any(definition => definition.Identity == review.DefinitionIdentity)) == 1, "A review has no unique selected current native origin."); }
        var holds = new List<string>(); JsonObject? permission = null;
        if (groups.SingleOrDefault(group => group.Group.Name == "pipes") is CompositeGroup windows)
        {
            ValidateCompositeWindowsControls(root, anchor, windows, policy);
            if (manifest["linuxPermissionProof"] is JsonValue permissionPath)
            { permission = ReadCompositePermission(root, anchor, policy, permissionPath.GetValue<string>(), CandidatePinned, ReadPinned, Pin); }
            else { holds.Add("Current six-obligation Linux permission proof remains unexecuted."); }
        }
        var assessments = new List<Assessment>();
        foreach (IGrouping<string, CompositeGroup> origin in groups.GroupBy(group => group.Candidate.Identity, StringComparer.Ordinal))
        {
            CompositeGroup[] selected = origin.ToArray(); Definition[] definitions = selected.SelectMany(group => group.Definitions).ToArray();
            Review[] rows = reviews.Where(review => review.CandidateIdentity == origin.Key).ToArray();
            PlatformAdmission[] admissions = CompositePermissionAdmissions(rows, definitions, permission, policy);
            assessments.Add(Assess(selected[0].Candidate, selected.SelectMany(group => group.Group.Sources), definitions, rows, authority, platformAdmissions: admissions));
        }
        JsonArray children = new();
        if (manifest["childControls"] is JsonValue childPath)
        { children = await ReadCompositeChildren(root, anchor, tools, policy, CompositeAbsolute(childPath.GetValue<string>()), input.Output, ReadPinned, Pin); }
        else { holds.Add("Current bounded HTTP and probe child activation controls remain unexecuted."); }
        Assessment total = SumCompositeAssessments(anchor, received, groups.SelectMany(group => group.Definitions).ToArray(), assessments, holds);
        foreach (CompositeGroup group in groups.Where(group => group.Group.Name != "pipes"))
        { VerifyInputs(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(group.Directory)!)!, "workspace"), group.Candidate); }
        Require(Capture(repo).SequenceEqual(anchor.Inputs), "Current candidate inputs changed during receiving.");
        foreach ((string path, string hash) in pins) { Require(Hash(path) == hash, "A receiving input changed: " + path); }
        Require(OwnedBytes(root, skipWorkspace: true) <= RetainedLimit, "Aggregate retained candidate evidence exceeds256MiB.");
        var results = new JsonObject
        {
            ["schema"] = "cis.healthcheck.grouped-acceptance/v3",
            ["candidateIdentity"] = anchor.Identity,
            ["candidateArchiveSha256"] = anchor.ArchiveSha256,
            ["policySha256"] = Hash(policyPath),
            ["nativeSourceInventory"] = JsonSerializer.SerializeToNode(anchor.Sources, Json),
            ["receivedSources"] = JsonSerializer.SerializeToNode(received.Order(StringComparer.Ordinal), Json),
            ["groups"] = new JsonArray(groups.Select(group => (JsonNode)new JsonObject
            {
                ["group"] = group.Group.Name,
                ["candidateIdentity"] = group.Candidate.Identity,
                ["platform"] = group.Candidate.Platform,
                ["host"] = group.Candidate.Host,
                ["run"] = group.RunPath,
                ["runSha256"] = Hash(group.RunPath),
                ["report"] = group.ReportPath,
                ["reportSha256"] = Hash(group.ReportPath)
            }).ToArray()),
            ["assessment"] = JsonSerializer.SerializeToNode(total, Json),
            ["originPartialAssessments"] = JsonSerializer.SerializeToNode(assessments, Json),
            ["linuxPermissionProof"] = permission,
            ["childControls"] = children,
            ["reviews"] = JsonSerializer.SerializeToNode(reviews, Json),
            ["observedInputPins"] = JsonSerializer.SerializeToNode(pins.Select(pin => new Input(pin.Key, pin.Value)), Json),
            ["nativeExecutorSha256"] = Hash(typeof(HealthcheckGroupedMutation).Assembly.Location),
            ["nativeHelperSha256"] = Hash(typeof(ProcessRunner).Assembly.Location),
            ["partialPilotEligibleForLaterMatchingUnion"] = !completeScope,
            ["supplementaryKilledAdded"] = 0,
            ["timeoutKillCredit"] = 0,
            ["releaseQualified"] = false
        };
        WriteRetained(root, Path.Combine(input.Output, "results.json"), results.ToJsonString(Json));
        return total.Qualified ? 0 : 1;
    }

    private static string CompositeAbsolute(string path) { Require(Path.IsPathFullyQualified(path), "Composite artifacts require explicit absolute paths."); return Path.GetFullPath(path); }
    private static string CompositePath(JsonObject value, string key) => CompositeAbsolute(value[key]!.GetValue<string>());

    internal static void ValidateCompositeSelection(JsonArray origins)
    {
        Require(origins.Count is > 0 and <= 4 && origins.All(origin => origin is JsonObject)
            && origins.Select(origin => (string?)origin!["group"]).Distinct(StringComparer.Ordinal).Count() == origins.Count,
            "Select unique whole groups; a partial pilot remains incomplete.");
        foreach (JsonNode? origin in origins)
        { Require(CompositePlatforms.TryGetValue((string?)origin!["group"] ?? "", out string? platform) && (string?)origin["platform"] == platform, "The declared whole-group platform recipe differs."); }
    }

    internal static void ValidateCompositePolicy(Candidate candidate, JsonObject policy, Func<string, string> readSource)
    {
        Require((string?)policy["schema"] == "cis.healthcheck.grouped-composite-policy/v1" && !string.IsNullOrWhiteSpace((string?)policy["authority"])
            && (int?)policy["conservativeThreshold"] == 90 && (bool?)policy["timeoutKillCredit"] == false && (int?)policy["supplementaryScopeCredit"] == 0,
            "The current policy must retain complete-scope90percent and zero timeout/supplementary kill credit.");
        JsonObject platforms = policy["wholeGroupPlatforms"]!.AsObject();
        Require(platforms.Count == 4 && CompositePlatforms.All(pair => (string?)platforms[pair.Key] == pair.Value), "The adopted four whole-group platform assignments changed.");
        foreach ((string key, int count) in new[] { ("linuxPermissionObligations", 6), ("windowsRequiredControls", 7) })
        {
            JsonArray rows = policy[key]!.AsArray();
            string[] roles = key == "linuxPermissionObligations"
                ? ["unixEndpointOuterOptions", "unixEndpointInnerOptions", "unixEndpointAlwaysFirst", "unixEndpointNeverFirst", "unixServerForeignPeerRecovery", "unixProbeForeignOwner"]
                : ["windowsSourceSettlement", "windowsInitialOwnership", "windowsInitialCleanup", "windowsRecoveryFirstInstance", "windowsRecoverySiblingOwnership", "windowsReplacementCleanup", "windowsProbeCancellationSettlement"];
            Require(rows.Count == count && rows.Select(row => (string?)row!["definitionIdentity"]).Distinct(StringComparer.Ordinal).Count() == count
                && rows.Select(row => (string?)row!["obligation"]).Order(StringComparer.Ordinal).SequenceEqual(roles.Order(StringComparer.Ordinal), StringComparer.Ordinal),
                "Missing, duplicated or substituted required platform roles/definitions.");
            foreach (JsonNode? row in rows)
            {
                (string expectedSource, string method, string[] witnesses) = CompositeSemanticRole((string)row!["obligation"]!);
                string source = CorePrefix + row!["source"]!.GetValue<string>();
                Require((string?)row["source"] == "Healthcheck/Pipes/" + expectedSource + ".cs"
                    && candidate.Inputs.Any(pin => pin.Path == source && pin.Sha256 == (string?)row["sourceSha256"])
                    && row["allowedWitnesses"] is JsonArray { Count: > 0 }, "Required platform tuples must bind actual current source and witnesses.");
                string text = readSource(source); JsonNode location = JsonNode.Parse((string)row["location"]!)!;
                int start = CompositePoint(text, location, "start"), end = CompositePoint(text, location, "end");
                Require(start < end && CompositeMethodContains(text, method, start, end), "A platform role's actual native span belongs to another shipping method.");
                foreach (JsonNode? witness in row["allowedWitnesses"]!.AsArray())
                {
                    Require(candidate.Inputs.Any(pin => pin.Path == LanguagePrefix + (string?)witness!["source"])
                    && witnesses.Contains(CompositeWitnessRole(witness!), StringComparer.Ordinal), "A platform obligation lacks its captured behavioral witness role.");
                }
            }
        }
        string[] permissionCases = policy["linuxPermissionObligations"]!.AsArray().SelectMany(row => row!["allowedWitnesses"]!.AsArray())
            .Select(witness => (string)witness!["name"]!).Append("CIS.Tests.TestAssemblySetup.SchedulingMatchesRequestedMode").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        Require(policy["linuxPermissionCases"]!.Deserialize<string[]>(Json)!.Order(StringComparer.Ordinal).SequenceEqual(permissionCases, StringComparer.Ordinal)
            && permissionCases.Length == 5 && policy["childSelections"]!.AsArray().Select(row => (string?)row!["source"]).Order(StringComparer.Ordinal).SequenceEqual(
                new[] { "Healthcheck/AspNetCore/HealthcheckApplicationBuilderExtensions.cs", "Healthcheck/Pipes/HealthProbeClient.cs" }, StringComparer.Ordinal)
            && policy["childCases"]!.Deserialize<string[]>(Json)!.Order(StringComparer.Ordinal).SequenceEqual(new[] {
                "CIS.Tests.IntegrationTests.Healthcheck.Reporting.HealthcheckHttpTransportTests.RealKestrelServesKnownViewsWithCachePreventionAndNoBusinessEffects",
                "CIS.Tests.IntegrationTests.Healthcheck.Reporting.HealthcheckProbeProcessTests.OmittedViewAndValidUpperCliLimitsStillRequestAndEmitHealth",
                "CIS.Tests.TestAssemblySetup.SchedulingMatchesRequestedMode" }.Order(StringComparer.Ordinal), StringComparer.Ordinal), "Required permission/child behavioral selections changed.");
    }

    // Stable behavioral roles, not mutation identities/operators/replacements/spans.
    // Those tuples remain reviewed, captured policy data and are checked against
    // the unchanged native Definitions and existing async/UID semantic validators.
    private static (string Source, string Method, string[] Witnesses) CompositeSemanticRole(string role) => role switch
    {
        "unixEndpointOuterOptions" => ("HealthPipeEndpoint", "Create", ["first-instance", "duplex", "foreign-peer"]),
        "unixEndpointInnerOptions" => ("HealthPipeEndpoint", "Create", ["duplex", "foreign-peer"]),
        "unixEndpointAlwaysFirst" => ("HealthPipeEndpoint", "Create", ["first-instance", "duplex"]),
        "unixEndpointNeverFirst" => ("HealthPipeEndpoint", "Create", ["first-instance"]),
        "unixServerForeignPeerRecovery" => ("HealthPipeServer", "RunSlotAsync", ["foreign-peer"]),
        "unixProbeForeignOwner" => ("HealthProbeClient", "RunAsync", ["foreign-owner"]),
        "windowsSourceSettlement" => ("HealthPipeServer", "DisposeAsync", ["source-settlement"]),
        "windowsInitialOwnership" => ("HealthPipeServer", "StartCore", ["initial-failure"]),
        "windowsInitialCleanup" => ("HealthPipeServer", "StartCore", ["initial-failure", "initial-cleanup"]),
        "windowsRecoveryFirstInstance" or "windowsRecoverySiblingOwnership" => ("HealthPipeServer", "RecoverSlotAsync", ["sibling-recovery"]),
        "windowsReplacementCleanup" => ("HealthPipeServer", "RecoverSlotAsync", ["replacement-cleanup"]),
        "windowsProbeCancellationSettlement" => ("HealthProbeClient", "RunAsync", ["probe-cancellation", "probe-error", "probe-error-settlement", "probe-missing", "probe-deadline", "probe-validation"]),
        _ => throw new InvalidDataException("Unknown platform behavioral role.")
    };

    private static string CompositeWitnessRole(JsonNode witness)
    {
        string name = (string?)witness["name"] ?? ""; string[] parts = name.Split('.');
        Require(parts.Length >= 2, "A behavioral witness needs its actual fully qualified method.");
        string type = parts[^2], method = parts[^1];
        bool integration = type == "HealthcheckPipeTransportTests";
        Require(name == (integration ? "CIS.Tests.IntegrationTests.Healthcheck.Reporting." : "CIS.Tests.UnitTests.Healthcheck.") + type + "." + method
            && (string?)witness["source"] == "tests/" + (integration ? "CIS.Core.IntegrationTests" : "CIS.Core.UnitTests") + "/Healthcheck/Reporting/" + type + ".cs",
            "The behavioral witness source/class/method mapping changed.");
        return (type, method) switch
        {
            ("HealthPipeInternalMutationQualificationTests", "FirstInstanceProtectionRejectsAnExistingEndpointEvenWhenItHasSpareCapacity") => "first-instance",
            ("HealthPipePublicLifecycleBoundaryTests", "ActualFirstAndAdditionalEndpointsSupportAsynchronousDuplexTransport") => "duplex",
            ("HealthPipeForeignOwnerBoundaryTests", "ForeignPeerRejectionPreservesTheActualOwnersAdmission") => "foreign-peer",
            ("HealthPipeForeignOwnerBoundaryTests", "CurrentUserOnlyRejectsAnAccessibleForeignOwnerBeforeSendingARequest") => "foreign-owner",
            ("HealthcheckPipeTransportTests", "DisposalWaitsForAnActuallyRunningSnapshotSource") => "source-settlement",
            ("HealthPipeStartupOwnershipBoundaryTests", "ARealSecondEndpointCreationFailureClosesTheFirstAndSettlesTerminalCalls") => "initial-failure",
            ("HealthPipeStartupOwnershipBoundaryTests", "StopDuringActualInitialCreationClosesTheUnpublishedHandleBeforeCollection") => "initial-cleanup",
            ("HealthPipeWindowsRecoveryTests", "ADisconnectedSlotRecoversWhileItsSiblingStillOwnsTheEndpointName") => "sibling-recovery",
            ("HealthPipeWindowsRecoveryTests", "StopDuringActualReplacementCreationClosesItsUnpublishedHandleBeforeCollection") => "replacement-cleanup",
            ("HealthPipeActiveCancellationBoundaryTests", "CallerCancellationAfterARealRequestDoesNotWaitForThePeerToSendAResponse") => "probe-cancellation",
            ("HealthPipeInternalMutationQualificationTests", "ProbeCancellationProducesOnlyTheBoundedErrorAndKeepsCallerStreamsOpen") => "probe-error",
            ("HealthProbeCancellationCompletionQualificationTests", "ReleasingTheActualCanceledCallErrorWriteCompletesTheCallerOwnedOperation") => "probe-error-settlement",
            ("HealthProbeClientTransportTests", "MissingLocalEndpointIsExit2WithoutRetryOrSnapshot") => "probe-missing",
            ("HealthProbeClientTransportTests", "ResponseHeaderAndBodyShareOneTotalMonotonicTimeoutBudget") => "probe-deadline",
            ("HealthProbeClientTransportTests", "ResponseValidationMustFinishWithinTheOriginalMonotonicBudget") => "probe-validation",
            _ => throw new InvalidDataException("Unknown platform behavioral witness.")
        };
    }

    private static bool CompositeMethodContains(string source, string method, int start, int end)
    {
        // These three small shipping types use ordinary four-space member
        // declarations. Refuse changed/ambiguous syntax instead of guessing.
        Match[] members = Regex.Matches(source, @"(?m)^    (?:public|private|internal) [^\r\n]*\b(?<name>[A-Za-z_][A-Za-z0-9_]*)\(").Cast<Match>().ToArray();
        return members.Select((member, index) => (member, finish: index + 1 < members.Length ? members[index + 1].Index : source.Length))
            .Count(item => item.member.Groups["name"].Value == method && start >= item.member.Index && end <= item.finish) == 1;
    }

    private static void ValidateCompositeTools(Toolchain tools)
    {
        Require(tools.Files.Single(pin => ForeignFileName(pin.Path) == "CIS.DependencyChecker.dll").Sha256 == Hash(typeof(HealthcheckGroupedMutation).Assembly.Location)
            && tools.Files.Single(pin => ForeignFileName(pin.Path) == "CIS.Core.TestSupport.dll").Sha256 == Hash(typeof(ProcessRunner).Assembly.Location),
            "Native evidence must use this actual current compiled receiver/helper, not caller-asserted hashes.");
    }

    private static CompositeGroup ReadCompositeLocal(Candidate anchor, string name, string root,
        Func<string, Candidate> candidateAt, Func<string, JsonObject> read, Action<string> pin)
    {
        Candidate candidate = candidateAt(root);
        Require(candidate.Inputs.SequenceEqual(anchor.Inputs), "A selected Linux group belongs to different maintained inputs.");
        VerifyInputs(Path.Combine(root, "workspace"), candidate); ValidatePilot(root, candidate);
        Toolchain tools = ReadToolchain(root, candidate); pin(Path.Combine(root, "toolchain.json")); ValidateCompositeTools(tools);
        foreach (Input file in tools.Files) { pin(file.Path); }
        Group group = Groups(candidate).Single(group => group.Name == name);
        string directory = Path.Combine(root, "groups", name), runPath = Path.Combine(directory, "run.json"), reportPath = Path.Combine(directory, "native/reports/mutation-report.json");
        JsonObject run = read(runPath); ValidateExecution(run); ValidateContext(run, group); ValidateSdkMsBuildEnvironment(run, tools);
        Require((string?)run["schema"] == "cis.healthcheck.grouped-run/v1" && (string?)run["candidateIdentity"] == candidate.Identity
            && (string?)run["platform"] == candidate.Platform && (string?)run["host"] == candidate.Host && (string?)run["group"] == name
            && (string?)run["executionIdentity"]?["operatingSystem"] == "linux" && run["linuxIdentity"] is null,
            "The selected Linux group's actual execution context differs.");
        string profile = Path.Combine(root, "profiles", name + ".json");
        Require(JsonNode.DeepEquals(read(profile), Configuration(group)) && (string?)run["configSha256"] == Hash(profile)
            && (string?)run["toolchainSha256"] == Hash(Path.Combine(root, "toolchain.json")), "Selected profile/toolchain differs from the native run.");
        ValidateCompositeNativeCommand(name, run, tools);
        JsonObject report = read(reportPath);
        Require((string?)run["reportSha256"] == Hash(reportPath) && JsonNode.DeepEquals(run["sources"], JsonSerializer.SerializeToNode(group.Sources, Json)), "Native report/source scope changed.");
        Definition[] definitions = Definitions(report, group.Sources, candidate.Inputs);
        ValidateTransportAsyncControls(name, definitions, report, relative => CandidateSource(root, candidate, relative));
        ValidateCompositeTests(directory, root, candidate, group, tools, run, report, false, pin);
        return new(candidate, group, tools, run, report, definitions, directory, runPath, reportPath);
    }

    private static CompositeGroup ReadCompositeWindows(string anchorRoot, Candidate anchor, Toolchain main, string path,
        Func<string, Candidate> candidateAt, Func<string, JsonObject> read, Action<string> pin)
    {
        string directory = Path.GetDirectoryName(path)!; JsonObject raw = read(path);
        PlatformProof proof = raw.Deserialize<PlatformProof>(Json)!; Candidate candidate = candidateAt(directory);
        Require(candidate.Inputs.SequenceEqual(anchor.Inputs) && proof.OriginCandidateIdentity == candidate.Identity && proof.Platform == candidate.Platform && proof.Host == candidate.Host,
            "Whole Windows Pipes must retain the exact ordered current candidate inputs and original platform identity.");
        string producer = main.Files.Single(file => ForeignFileName(file.Path) == "CIS.DependencyChecker.dll").Sha256;
        string helper = main.Files.Single(file => ForeignFileName(file.Path) == "CIS.Core.TestSupport.dll").Sha256;
        // The original digest is verified using its producing platform's actual
        // CRLF serialization; ordered input tuples, not rewritten receipts,
        // establish equality with the receiving platform's LF serialization.
        ValidateCompositeWindowsHeader(proof, candidate, anchor, producer, helper);
        string[] required = ["candidate.json", "candidate.zip", "toolchain.json", "profile.json", "run.json", "report.json", "native.log", "tools.zip",
            "baseline-CIS.Core.UnitTests.trx", "restored-CIS.Core.UnitTests.trx", "baseline-CIS.Core.IntegrationTests.trx", "restored-CIS.Core.IntegrationTests.trx"];
        Require(proof.Files.Select(file => file.Path).Order(StringComparer.Ordinal).SequenceEqual(required.Order(StringComparer.Ordinal), StringComparer.Ordinal), "Whole portable Windows artifact membership differs.");
        foreach (Input file in proof.Files)
        { string retained = HealthcheckMutationChildren.HealthcheckMutationChildProtocol.Input(directory, file.Path); Require(Hash(retained) == file.Sha256, "A portable Windows member changed."); pin(retained); }
        Group group = Groups(anchor).Single(group => group.Name == "pipes");
        Require(JsonNode.DeepEquals(read(Path.Combine(directory, "profile.json")), Configuration(group)), "Whole Windows profile differs.");
        string runPath = Path.Combine(directory, "run.json"), reportPath = Path.Combine(directory, "report.json");
        JsonObject run = read(runPath); ValidateExecution(run); ValidatePlatformContext(run, group, proof.ExecutionIdentity);
        Require((string?)run["schema"] == "cis.healthcheck.grouped-run/v1" && (string?)run["candidateIdentity"] == candidate.Identity
            && (string?)run["platform"] == candidate.Platform && (string?)run["host"] == candidate.Host && (string?)run["group"] == "pipes"
            && run["linuxIdentity"] is null && (string?)run["configSha256"] == Hash(Path.Combine(directory, "profile.json"))
            && (string?)run["toolchainSha256"] == Hash(Path.Combine(directory, "toolchain.json")) && (string?)run["reportSha256"] == Hash(reportPath)
            && JsonNode.DeepEquals(run["sources"], JsonSerializer.SerializeToNode(group.Sources, Json)), "Whole Windows native run binding differs.");
        Toolchain tools = read(Path.Combine(directory, "toolchain.json")).Deserialize<Toolchain>(Json)!;
        Require(tools.Schema == "cis.healthcheck.grouped-toolchain/v1" && tools.Platform == candidate.Platform && tools.Host == candidate.Host
            && tools.Sdk == main.Sdk && tools.SubjectRuntime == main.SubjectRuntime && tools.ToolRuntime == main.ToolRuntime, "Portable Windows tools belong to another declared SDK/runtime/platform.");
        ValidatePlatformNativeCommand(run, tools); ValidatePlatformTools(Path.Combine(directory, "tools.zip"), proof.Tools, tools, main, producer, helper);
        JsonObject report = read(reportPath); Definition[] definitions = Definitions(report, group.Sources, anchor.Inputs);
        Require(!definitions.Any(item => item.Status is "RuntimeError" or "Pending") && NativePopulationHolds(definitions).Length == 0, "Whole Windows native execution is incomplete or inadmissible.");
        ValidateTransportAsyncControls("pipes", definitions, report, relative => CandidateSource(anchorRoot, anchor, relative));
        ValidatePlatformClaims(proof.Claims, definitions, report, anchor.Inputs);
        ValidateCompositeTests(directory, anchorRoot, anchor, group, tools, run, report, true, pin);
        return new(candidate, group, tools, run, report, definitions, directory, runPath, reportPath);
    }

    private static void ValidateCompositeNativeCommand(string name, JsonObject run, Toolchain tools)
    {
        Input cli = tools.Files.Single(file => ForeignFileName(file.Path) == "Stryker.CLI.dll"), core = tools.Files.Single(file => ForeignFileName(file.Path) == "Stryker.Core.dll");
        JsonNode command = run["commandLog"]!.AsArray().Single(row => (string?)row!["name"] == "native")!;
        string[] args = command["arguments"]!.Deserialize<string[]>(Json)!;
        Require((string?)run["strykerCliSha256"] == cli.Sha256 && (string?)run["strykerCoreSha256"] == core.Sha256
            && (string?)command["executable"] == tools.ToolDotnet && args.Length == (name == "pilot" ? 12 : 11)
            && args.Take(6).SequenceEqual(new[] { "exec", "--fx-version", "8.0.31", "--roll-forward", "Disable", cli.Path }, StringComparer.Ordinal)
            && args[6] == "--config-file" && args[8] == "--output" && args[10] == "--skip-version-check" && (name != "pilot" || args[11] == "--log-to-file"), "Native command changed its pinned host/producer or added overrides.");
        string suffix = "/profiles/" + name + ".json";
        Require(args[7].EndsWith(suffix, StringComparison.Ordinal) && args[9] == args[7][..^suffix.Length] + "/groups/" + name + "/native", "Native config/output do not belong to one group attempt.");
    }

    private static void ValidateCompositeTests(string directory, string root, Candidate candidate, Group group, Toolchain tools,
        JsonObject run, JsonObject report, bool windows, Action<string> pin)
    {
        var contexts = new List<Dictionary<string, string>>();
        foreach (string phase in new[] { "baseline", "restored" })
        {
            var tests = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string projectPath in group.Projects)
            {
                string project = Path.GetFileNameWithoutExtension(projectPath), name = phase + "-" + project;
                string path = Path.Combine(directory, name + ".trx");
                Require(run["nativeTrxPins"]!.Deserialize<Input[]>(Json)!.Single(file => file.Path == name + ".trx").Sha256 == Hash(path), "A baseline/restored native report changed.");
                pin(path); PassingTrx(path);
                JsonNode command = run["commandLog"]!.AsArray().Single(row => (string?)row!["name"] == name)!;
                string assembly = CompositeTestAssembly(command, project, group.Filter, tools.SubjectDotnet, windows);
                XDocument document = XDocument.Load(path); XNamespace ns = document.Root!.Name.Namespace;
                foreach (XElement test in document.Descendants(ns + "UnitTest"))
                {
                    XElement method = test.Descendants(ns + "TestMethod").Single();
                    Require(string.Equals(((string?)method.Attribute("codeBase"))?.Replace('\\', '/'), assembly, windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
                        && tests.TryAdd((string)test.Attribute("id")!, (string?)method.Attribute("className") + "." + (string?)method.Attribute("name")), "Native test UUID/codeBase is ambiguous or belongs to another command.");
                }
            }
            contexts.Add(tests);
        }
        var actual = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string path, JsonNode? file) in report["testFiles"]!.AsObject())
        {
            string relative = LanguageReportPath(path), source = CompositeCandidateSource(root, candidate, LanguagePrefix + relative);
            Require((string?)file!["source"] == source, "Native test-file source differs from its captured current input.");
            foreach (JsonNode? test in file["tests"]!.AsArray())
            {
                string id = (string)test!["id"]!, name = (string)test["name"]!;
                Require(actual.TryAdd(id, name) && contexts.All(context => context.TryGetValue(id, out string? found) && found == name), "Raw mutation test UUID/name differs from both passing contexts.");
            }
        }
        Require(actual.Count > 0 && contexts.All(context => context.Count == actual.Count), "Whole native test population differs from baseline/restored contexts.");
    }

    private static string CompositeTestAssembly(JsonNode command, string project, string filter, string host, bool windows)
    {
        string[] args = command["arguments"]!.Deserialize<string[]>(Json)!;
        StringComparison comparison = windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        Require(string.Equals(((string?)command["executable"])?.Replace('\\', '/'), host.Replace('\\', '/'), comparison) && args.Length == 12 && args[0] == "test"
            && args.Skip(2).Take(8).SequenceEqual(new[] { "-c", "Release", "--no-build", "--no-restore", "--filter", filter, "--logger", "trx;LogFileName=" + (string)command["name"]! + ".trx" }, StringComparer.Ordinal)
            && args[10] == "--results-directory", "Baseline/restored native command changed its host/filter/build context.");
        string path = args[1].Replace('\\', '/');
        Require((windows ? path.Length > 3 && char.IsAsciiLetter(path[0]) && path[1..3] == ":/" : path.StartsWith('/'))
            && !path.Split('/').Any(part => part is "." or "..") && path.EndsWith("/" + project + "/" + project + ".csproj", comparison), "Native test project path is ambiguous.");
        return path[..path.LastIndexOf('/')] + "/bin/Release/net10.0/" + project + ".dll";
    }

    private static void ValidateCompositeTuple(Definition actual, JsonNode expected)
    {
        Require(actual.Identity == (string?)expected["definitionIdentity"] && actual.Source == (string?)expected["source"] && actual.SourceSha256 == (string?)expected["sourceSha256"]
            && actual.Operator == (string?)expected["operator"] && actual.Location == (string?)expected["location"] && actual.Replacement == (string?)expected["replacement"], "Required native definition tuple differs from the reviewed candidate policy.");
    }

    private static void ValidateCompositeWindowsControls(string root, Candidate candidate, CompositeGroup group, JsonObject policy)
    {
        foreach (JsonNode? expected in policy["windowsRequiredControls"]!.AsArray())
        {
            Definition actual = group.Definitions.Single(definition => definition.Identity == (string?)expected!["definitionIdentity"]);
            ValidateCompositeTuple(actual, expected!); Require(actual.Status == "Killed", "Every required Windows async/ownership/two-slot definition must actually be Killed.");
            bool witnessed = expected!["allowedWitnesses"]!.AsArray().Any(witness => CompositeWitness(root, candidate, group.Report, actual, witness!,
                [Path.Combine(group.Directory, "baseline-" + ((string)witness!["source"]!).Split('/')[1] + ".trx"), Path.Combine(group.Directory, "restored-" + ((string)witness["source"]!).Split('/')[1] + ".trx")]));
            Require(witnessed, "A required Windows definition lacks an actual current named killer in both passing contexts.");
        }
    }

    private static bool CompositeWitness(string root, Candidate candidate, JsonObject report, Definition definition, JsonNode witness, string[] trxs)
    {
        string source = (string)witness["source"]!, name = (string)witness["name"]!;
        JsonNode native = report["files"]!.AsObject().Single(file => CoreReportPath(file.Key) == definition.Source).Value!["mutants"]!.AsArray().Single(row => (string?)row!["id"] == definition.NativeId)!;
        string[] killed = native["killedBy"]?.Deserialize<string[]>(Json) ?? [];
        Require(killed.Distinct(StringComparer.Ordinal).Count() == killed.Length, "Ambiguous native killer IDs.");
        JsonNode file = report["testFiles"]!.AsObject().Single(file => LanguageReportPath(file.Key) == source).Value!;
        Require((string?)file["source"] == CompositeCandidateSource(root, candidate, LanguagePrefix + source), "Native witness source differs from its captured candidate.");
        string[] ids = file["tests"]!.AsArray().Where(test => (string?)test!["name"] == name && killed.Contains((string?)test["id"], StringComparer.Ordinal)).Select(test => (string)test!["id"]!).ToArray();
        if (ids.Length == 0) { return false; }
        foreach (string id in ids)
        {
            Require(report["testFiles"]!.AsObject().SelectMany(file => file.Value!["tests"]!.AsArray()).Count(test => (string?)test!["id"] == id) == 1, "Native witness UUID is ambiguous.");
            foreach (string path in trxs)
            {
                XDocument document = XDocument.Load(path); XNamespace ns = document.Root!.Name.Namespace;
                XElement method = document.Descendants(ns + "UnitTest").Single(test => (string?)test.Attribute("id") == id).Descendants(ns + "TestMethod").Single();
                Require((string?)method.Attribute("className") + "." + (string?)method.Attribute("name") == name
                    && ((string?)method.Attribute("codeBase"))?.Replace('\\', '/').EndsWith("/" + source.Split('/')[1] + "/bin/Release/net10.0/" + source.Split('/')[1] + ".dll", StringComparison.Ordinal) == true
                    && document.Descendants(ns + "UnitTestResult").Count(test => (string?)test.Attribute("testId") == id && (string?)test.Attribute("outcome") == "Passed") == 1,
                    "Native witness did not uniquely pass with its actual codeBase in both contexts.");
            }
        }
        return true;
    }

    private static JsonObject ReadCompositePermission(string anchorRoot, Candidate anchor, JsonObject policy, string path,
        Func<string, Candidate> candidateAt, Func<string, JsonObject> read, Action<string> pin)
    {
        path = CompositeAbsolute(path); JsonObject proof = read(path);
        Require((string?)proof["schema"] == "cis.healthcheck.linux-permission-native-proof/v1" && (bool?)proof["complete"] == true, "Current Linux permission execution is absent/incomplete.");
        string root = CompositePath(proof, "candidateRoot"); Candidate candidate = candidateAt(root);
        Require(candidate.Inputs.SequenceEqual(anchor.Inputs) && (string?)proof["candidateIdentity"] == candidate.Identity
            && (string?)proof["originalCommonInputs"] == CommonInputs(candidate) && (int?)proof["frozenNativeCandidateSourceCount"] == candidate.Inputs.Length
            && (bool?)proof["noModuleMutationPopulationOrScoreCredit"] == true, "Permission proof does not bind the exact current candidate membership.");
        VerifyInputs(Path.Combine(root, "workspace"), candidate); ValidatePilot(root, candidate);
        Toolchain tools = ReadToolchain(root, candidate); pin(Path.Combine(root, "toolchain.json")); ValidateCompositeTools(tools);
        Require((string?)proof["platform"] == candidate.Platform && (string?)proof["host"] == candidate.Host
            && (string?)proof["actualExecutionIdentity"]?["operatingSystem"] == "linux" && (uint?)proof["actualExecutionIdentity"]?["uid"] == 0
            && (uint?)proof["actualExecutionIdentity"]?["effectiveUid"] == 0 && (string?)proof["actualExecutionIdentity"]?["host"] == candidate.Host,
            "Permission proof requires its actually observed Linux UID0 native context.");
        Require((int?)proof["budgetSeconds"] == 600 && (int?)proof["restorationReserveSeconds"] == 90 && (int?)proof["nativeCeilingSeconds"] == 450
            && (double?)proof["seconds"] is > 0 and <= 600 && (int?)proof["nativeExitCode"] == 0 && (bool?)proof["nativeOutputDrained"] == true
            && (bool?)proof["outputDrained"] == true && (bool?)proof["restored"] == true && (bool?)proof["cleanupHealthy"] == true && proof["failures"] is JsonArray { Count: 0 },
            "Permission execution, bounded native command, restoration or cleanup failed.");
        JsonArray files = proof["files"]!.AsArray();
        Require(files.Count > 0 && files.Select(file => (string?)file!["path"]).Distinct(StringComparer.Ordinal).Count() == files.Count, "Permission retained inputs are missing/duplicated.");
        foreach (JsonNode? file in files)
        {
            string retained = CompositePath(file!.AsObject(), "path");
            Require(Hash(retained) == (string?)file["sha256"] && new FileInfo(retained).Length == (long?)file["bytes"], "A permission source/tool/native/identity artifact changed."); pin(retained);
        }
        void Pinned(string retained) => Require(files.Any(file => (string?)file!["path"] == retained && (string?)file["sha256"] == Hash(retained)), "Permission proof omits a required actual artifact.");
        foreach (string name in new[] { "candidate.json", "candidate.zip", "toolchain.json" }) { Pinned(Path.Combine(root, name)); }
        foreach (string name in new[] { "checker", "originalHelper", "executedRelocatedHelper" })
        {
            JsonNode file = proof[name]!; string retained = CompositePath(file.AsObject(), "path");
            string actual = Hash(name == "checker" ? typeof(HealthcheckGroupedMutation).Assembly.Location : typeof(ProcessRunner).Assembly.Location);
            Require(Hash(retained) == actual && (string?)file["sha256"] == actual, "Permission execution used another current checker/helper."); Pinned(retained);
        }
        Group group = Groups(candidate).Single(group => group.Name == "pipes"); ValidateContext(proof, group); ValidateSdkMsBuildEnvironment(proof, tools);
        string configPath = CompositePath(proof, "configurationPath"), reportPath = CompositePath(proof, "reportPath"); Pinned(configPath); Pinned(reportPath);
        JsonObject config = read(configPath)["stryker-config"]!.AsObject();
        Require((string?)config["mutation-level"] == "Complete" && (string?)config["coverage-analysis"] == "perTest" && (string?)config["test-runner"] == "vstest"
            && (string?)config["configuration"] == "Release" && (string?)config["target-framework"] == "net10.0" && (int?)config["concurrency"] == 2
            && (bool?)config["disable-bail"] == true && (int?)config["additional-timeout"] == 10000
            && config["test-projects"]!.Deserialize<string[]>(Json)!.SequenceEqual(new[] { "../../tests/CIS.Core.UnitTests/CIS.Core.UnitTests.csproj" }, StringComparer.Ordinal), "The fixed bounded permission native recipe changed.");
        string[] cases = ((string)config["test-case-filter"]!).Split('|').Select(value => value.Replace("FullyQualifiedName=", "", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToArray();
        Require(cases.SequenceEqual(policy["linuxPermissionCases"]!.Deserialize<string[]>(Json)!.Order(StringComparer.Ordinal), StringComparer.Ordinal), "Permission native selection differs from its reviewed cases.");
        JsonArray obligations = policy["linuxPermissionObligations"]!.AsArray();
        string[] sources = obligations.Select(row => (string)row!["source"]!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        string[] selectors = sources.Select(source =>
        {
            string text = CandidateSource(anchorRoot, anchor, CorePrefix + source); JsonNode[] rows = obligations.Where(row => (string?)row!["source"] == source).Select(row => row!).ToArray();
            return source + "{" + rows.Min(row => CompositePoint(text, JsonNode.Parse((string)row["location"]!)!, "start")) + ".."
                + rows.Max(row => CompositePoint(text, JsonNode.Parse((string)row["location"]!)!, "end")) + "}";
        }).Order(StringComparer.Ordinal).ToArray();
        Require(config["mutate"]!.Deserialize<string[]>(Json)!.Order(StringComparer.Ordinal).SequenceEqual(selectors, StringComparer.Ordinal), "Permission mutation spans differ from the source-bound policy.");
        JsonNode command = proof["commandLog"]!.AsArray().Single(row => (string?)row!["name"] == "native")!;
        string cli = tools.Files.Single(file => ForeignFileName(file.Path) == "Stryker.CLI.dll").Path;
        Require((string?)command["executable"] == tools.ToolDotnet && (int?)command["exitCode"] == 0 && (bool?)command["outputDrained"] == true
            && (double?)command["timeoutSeconds"] is > 0 and <= 450 && (double?)command["seconds"] is > 0 and <= 450
            && command["arguments"]!.Deserialize<string[]>(Json)!.SequenceEqual(new[] { "exec", "--fx-version", "8.0.31", "--roll-forward", "Disable", cli,
                "--config-file", configPath, "--output", Path.GetDirectoryName(Path.GetDirectoryName(reportPath)!)!, "--skip-version-check" }, StringComparer.Ordinal), "The actual permission native invocation differs.");
        JsonObject report = read(reportPath); Definition[] definitions = Definitions(report, sources, candidate.Inputs);
        Require(definitions.Count(definition => Eligible(definition.Status)) == 6 && !definitions.Any(definition => definition.Status is "RuntimeError" or "Pending") && NativePopulationHolds(definitions).Length == 0, "Permission native population is incomplete or contains unresolved outcomes.");
        string baseline = CompositePath(proof, "baselineTrxPath"), restored = CompositePath(proof, "restoredTrxPath"); Pinned(baseline); Pinned(restored);
        Require(PassingTrx(baseline) == 5 && PassingTrx(restored) == 5, "Exactly five current baseline/restored permission cases must pass.");
        JsonArray claims = proof["claims"]!.AsArray();
        Require(claims.Count == 6 && claims.Select(row => (string?)row!["obligation"]).Distinct(StringComparer.Ordinal).Count() == 6, "All six distinct permission claims are required.");
        foreach (JsonNode? expected in obligations)
        {
            Definition actual = definitions.Single(definition => definition.Identity == (string?)expected!["definitionIdentity"]); ValidateCompositeTuple(actual, expected!);
            JsonNode claim = claims.Single(row => (string?)row!["obligation"] == (string?)expected!["obligation"])!;
            Require(actual.Status == "Killed" && (string?)claim["definitionIdentity"] == actual.Identity && (string?)claim["status"] == "Killed"
                && JsonNode.DeepEquals(claim["location"], JsonNode.Parse(actual.Location)), "A required permission definition was not actually Killed.");
            foreach (string field in new[] { "source", "sourceSha256", "operator", "replacement", "requirement" })
            { Require((string?)claim[field] == (string?)expected![field], "Permission claim differs from its source-bound obligation."); }
            JsonNode witness = expected!["allowedWitnesses"]!.AsArray().Single(row => (string?)row!["source"] == (string?)claim["witnessSource"] && (string?)row["name"] == (string?)claim["witnessName"])!;
            Require(CompositeWitness(anchorRoot, anchor, report, actual, witness, [baseline, restored]), "A permission definition lacks its actual current named native killer.");
        }
        JsonObject journals = ValidateLinuxIdentityJournals(root, candidate, group, proof["linuxIdentity"]!.AsObject())!;
        Require(JsonNode.DeepEquals(journals, proof["linuxIdentity"]), "Actual UID/role/context/cleanup journals changed.");
        ValidateLinuxIdentityControl(definitions, report, relative => CandidateSource(anchorRoot, anchor, relative));
        ValidateLinuxServerRecoveryControl(definitions, report, relative => CandidateSource(anchorRoot, anchor, relative));
        return new JsonObject
        {
            ["path"] = path,
            ["sha256"] = Hash(path),
            ["nativeCandidateIdentity"] = candidate.Identity,
            ["platform"] = candidate.Platform,
            ["host"] = candidate.Host,
            ["claims"] = claims.DeepClone(),
            ["moduleKilledAdded"] = 0,
            ["moduleDenominatorAdded"] = 0
        };
    }

    private static int CompositePoint(string text, JsonNode location, string field)
    {
        int line = (int?)location[field]?["line"] ?? 0, column = (int?)location[field]?["column"] ?? 0, offset = 0;
        Require(line > 0 && column > 0, "Native definition needs positive UTF-16 positions.");
        for (int current = 1; current < line; current++) { int next = text.IndexOf('\n', offset); Require(next >= 0, "Native definition exceeds actual source lines."); offset = next + 1; }
        int end = text.IndexOf('\n', offset); if (end < 0) { end = text.Length; }
        if (end > offset && text[end - 1] == '\r') { end--; }
        Require(column - 1 <= end - offset, "Native definition exceeds its actual source column."); return offset + column - 1;
    }

    private static string CompositeCandidateSource(string root, Candidate candidate, string relative)
    {
        relative = relative.Replace('\\', '/');
        Require(relative.StartsWith(LanguagePrefix, StringComparison.Ordinal) && !relative.Contains(':') && !relative.Any(char.IsControl), "Native test source escaped the language scope.");
        var parts = new List<string>();
        foreach (string part in relative.Split('/'))
        {
            if (part is "" or ".") { continue; }
            if (part == "..") { Require(parts.Count > 2, "Native source alias escaped the language scope."); parts.RemoveAt(parts.Count - 1); }
            else { parts.Add(part); }
        }
        Require(parts.Count > 2, "Native source alias does not name a captured file.");
        return CandidateSource(root, candidate, string.Join('/', parts));
    }

    internal static void ValidateCompositeWindowsHeader(PlatformProof proof, Candidate foreign, Candidate anchor, string producer, string helper)
    {
        string digest = Digest(JsonSerializer.SerializeToUtf8Bytes(foreign.Inputs, new JsonSerializerOptions(Json) { NewLine = "\r\n" }));
        Require(proof.Schema == "cis.healthcheck.platform-native-proof/v1" && proof.Group == "pipes" && foreign.Inputs.SequenceEqual(anchor.Inputs)
            && proof.CommonInputsSha256 == digest && proof.OriginCandidateIdentity == foreign.Identity && proof.Platform == foreign.Platform && proof.Host == foreign.Host
            && proof.Platform.StartsWith("Microsoft Windows ", StringComparison.Ordinal) && proof.Platform.EndsWith("; X64; .NET 10.0.12", StringComparison.Ordinal)
            && (string?)proof.ExecutionIdentity["operatingSystem"] == "windows" && !string.IsNullOrWhiteSpace((string?)proof.ExecutionIdentity["userSid"])
            && !string.IsNullOrWhiteSpace((string?)proof.ExecutionIdentity["ownerSid"]) && proof.ProducerSha256 == producer && proof.HelperSha256 == helper,
            "Whole Windows proof must bind exact current ordered inputs, original producing serialization, actual platform identity and current compiled tools.");
    }

    private static PlatformAdmission[] CompositePermissionAdmissions(Review[] reviews, Definition[] definitions, JsonObject? permission, JsonObject policy)
    {
        JsonArray obligations = policy["linuxPermissionObligations"]!.AsArray();
        foreach (Definition definition in definitions.Where(definition => definition.Status is "Survived" or "NoCoverage" && obligations.Any(row => (string?)row!["definitionIdentity"] == definition.Identity)))
        { Require(reviews.Count(review => review.DefinitionIdentity == definition.Identity && review.Decision == "PlatformCovered") == 1, "A Unix permission obligation cannot use Equivalent/noncritical instead of its genuine platform proof."); }
        var result = new List<PlatformAdmission>();
        foreach (Review review in reviews.Where(review => review.Decision == "PlatformCovered"))
        {
            Require(permission is not null, "PlatformCovered requires an actually validated current permission proof.");
            JsonNode expected = obligations.Single(row => (string?)row!["definitionIdentity"] == review.DefinitionIdentity)!;
            Definition actual = definitions.Single(definition => definition.Identity == review.DefinitionIdentity);
            JsonNode claim = permission!["claims"]!.AsArray().Single(row => (string?)row!["definitionIdentity"] == actual.Identity)!;
            Require(actual.Status is "Survived" or "NoCoverage" && review.RawStatus == actual.Status && review.Requirement == (string?)expected["requirement"]
                && review.PlatformProof?.Path == (string?)permission["path"] && review.PlatformProof?.Sha256 == (string?)permission["sha256"], "PlatformCovered must bind the unchanged selected Windows miss to its current Linux obligation proof.");
            result.Add(new(actual.Identity, (string)permission["sha256"]!, (string)permission["nativeCandidateIdentity"]!, (string)permission["platform"]!, (string)permission["host"]!, (string)claim["witnessSource"]!, (string)claim["witnessName"]!));
        }
        return result.ToArray();
    }

    internal static JsonObject CompositeChildPassingContext(JsonObject context, string phase)
    {
        Require(phase is "baseline" or "restored", "Unknown child passing-context phase.");
        Require(context["restored"] is JsonValue flag && flag.TryGetValue<bool>(out bool healthy) && healthy,
            "Child restoration must be the actual boolean health witness, not a passing-context object.");
        Require(context["baselineContext"] is JsonObject && context["restoredContext"] is JsonObject,
            "Both distinct child baseline/restored passing-context records are required.");
        return context[phase + "Context"]!.AsObject();
    }

    private static async Task<JsonArray> ReadCompositeChildren(string root, Candidate candidate, Toolchain tools, JsonObject policy, string path, string output,
        Func<string, JsonObject> read, Action<string> pin)
    {
        JsonObject context = read(path);
        Require((string?)context["schema"] == "cis.healthcheck.composite-child-controls/v1" && (string?)context["candidateIdentity"] == candidate.Identity
            && (string?)context["producerSha256"] == Hash(typeof(HealthcheckGroupedMutation).Assembly.Location)
            && (string?)context["helperSha256"] == Hash(typeof(ProcessRunner).Assembly.Location)
            && (bool?)context["restored"] == true && (bool?)context["processCleanupHealthy"] == true
            && (string?)context["temporaryCleanup"] == "owned short temp removed" && context["failures"] is JsonArray { Count: 0 }
            && (int?)context["budgetSeconds"] == 600 && (int?)context["restorationReserveSeconds"] == 90
            && (double?)context["seconds"] is > 0 and <= 600,
            "Child controls need the current producer, actual candidate and healthy restoration/cleanup record.");
        ValidateContext(context, Groups(candidate).Single(group => group.Name == "http")); ValidateSdkMsBuildEnvironment(context, tools);
        ChildControl[] controls = context["controls"]!.Deserialize<ChildControl[]>(Json)!;
        Require(controls.Select(control => control.Kind).Order(StringComparer.Ordinal).SequenceEqual(new[] { "http-host", "probe" }.Order(StringComparer.Ordinal), StringComparer.Ordinal), "Both real bounded child activation controls are required.");
        string[] cases = policy["childCases"]!.Deserialize<string[]>(Json)!;
        string filter = string.Join('|', cases.Select(name => "FullyQualifiedName=" + name));
        foreach (string phase in new[] { "baseline", "restored" })
        {
            JsonObject observed = CompositeChildPassingContext(context, phase); string trx = CompositePath(observed, "path"); pin(trx);
            Require((string?)observed["sha256"] == Hash(trx) && PassingTrx(trx) == cases.Length, "The actual passing child baseline/restored TRX changed or is incomplete.");
            JsonObject command = observed["command"]!.AsObject();
            Require((string?)command["name"] == phase + "-CIS.Core.IntegrationTests" && (int?)command["exitCode"] == 0
                && (bool?)command["outputDrained"] == true, "A child passing-context command failed or did not drain.");
            string assembly = CompositeTestAssembly(command, "CIS.Core.IntegrationTests", filter, tools.SubjectDotnet, false);
            string[] arguments = command["arguments"]!.Deserialize<string[]>(Json)!;
            Require(Path.GetFullPath(Path.Combine(arguments[11], (string)command["name"]! + ".trx")) == trx,
                "The child passing-context command does not name this actual TRX.");
            XDocument document = XDocument.Load(trx); XNamespace ns = document.Root!.Name.Namespace;
            XElement[] methods = document.Descendants(ns + "UnitTest").Select(test => test.Descendants(ns + "TestMethod").Single()).ToArray();
            Require(methods.Select(method => (string?)method.Attribute("className") + "." + (string?)method.Attribute("name")).Order(StringComparer.Ordinal)
                .SequenceEqual(cases.Order(StringComparer.Ordinal), StringComparer.Ordinal)
                && methods.All(method => ((string?)method.Attribute("codeBase"))?.Replace('\\', '/') == assembly),
                "Child baseline/restored membership or actual test assembly differs from the policy.");
        }
        var result = new JsonArray();
        foreach (ChildControl control in controls)
        {
            Require(control.ActiveMutant >= 0, "A child control needs an actual selected mutant.");
            pin(control.Session); pin(control.NativeReport); pin(control.NativeBinding);
            var session = HealthcheckMutationChildren.HealthcheckMutationChildProtocol.Read<HealthcheckMutationChildren.HealthcheckMutationChildProtocol.Session>(control.Session);
            HealthcheckMutationNativeRun.Selection[] selection = ValidateChildBinding(control, session, candidate, tools);
            pin(session.ConfigurationPath);
            JsonObject configuration = read(session.ConfigurationPath)["stryker-config"]!.AsObject();
            Require((string?)configuration["coverage-analysis"] == "off" && (string?)configuration["test-case-filter"] == filter
                && configuration["test-projects"]!.Deserialize<string[]>(Json)!.SequenceEqual(new[] { "../../tests/CIS.Core.IntegrationTests/CIS.Core.IntegrationTests.csproj" }, StringComparer.Ordinal),
                "The actual child native configuration must run the three policy cases with coverage selection off.");
            Definition[] definitions = Definitions(Read(control.NativeReport), selection.Select(item => item.Source).Order(StringComparer.Ordinal).ToArray(), candidate.Inputs);
            Definition active = definitions.Single(definition => definition.Identity == control.DefinitionIdentity && definition.NativeId == control.ActiveMutant.ToString(System.Globalization.CultureInfo.InvariantCulture));
            string source = control.Kind == "http-host" ? "Healthcheck/AspNetCore/HealthcheckApplicationBuilderExtensions.cs" : "Healthcheck/Pipes/HealthProbeClient.cs";
            string text = CandidateSource(root, candidate, CorePrefix + source);
            JsonNode expected = policy["childSelections"]!.AsArray().Single(row => (string?)row!["source"] == source)!;
            string statement = (string)expected["statement"]!; int start = text.IndexOf(statement, StringComparison.Ordinal);
            Require(start >= 0 && start == text.LastIndexOf(statement, StringComparison.Ordinal) && selection.Length == 2
                && selection.Single(item => item.Source == source) is { Start: not null, End: not null } selected && selected.Start == start && selected.End == start + statement.Length,
                "Actual child selection differs from the unique current source-bound activation statement.");
            Require(active.Source == source && active.Status is "Killed" or "Survived", "The child did not execute its actual shipping body.");
            ValidateChildDefinitionSelection(active, selection, text);
            string destination = Path.Combine(output, "children", control.Kind);
            int code = await HealthcheckChildMutationVerification.RunAsync(["audit", "--session", control.Session, "--output", destination, "--require-kind", control.Kind, "--require-active", control.ActiveMutant.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
            Require(code == 0, "Actual child activation, source selection, protocol or lifecycle audit failed.");
            result.Add(new JsonObject
            {
                ["kind"] = control.Kind,
                ["definitionIdentity"] = active.Identity,
                ["nativeReportSha256"] = Hash(control.NativeReport),
                ["sessionSha256"] = Hash(control.Session),
                ["actualAuditSha256"] = Hash(Path.Combine(destination, "results.json")),
                ["passingContextsSha256"] = Hash(path),
                ["moduleScopeCredit"] = 0
            });
        }
        return result;
    }

    internal static Assessment SumCompositeAssessments(Candidate anchor, IEnumerable<string> sources, Definition[] definitions,
        IEnumerable<Assessment> origins, IEnumerable<string> prerequisiteHolds)
    {
        Assessment[] rows = origins.ToArray(); var holds = new List<string>(prerequisiteHolds);
        bool inventoryComplete = sources.Order(StringComparer.Ordinal).SequenceEqual(anchor.Sources.Order(StringComparer.Ordinal), StringComparer.Ordinal);
        if (!inventoryComplete) { holds.Add("Complete current Healthcheck source inventory has not executed."); }
        foreach (Assessment row in rows)
        {
            Require(row.SourceInventoryComplete || row.Holds.FirstOrDefault() == "Complete current Healthcheck source inventory has not executed.", "The origin structural hold changed.");
            holds.AddRange(row.Holds.Skip(row.SourceInventoryComplete ? 0 : 1));
        }
        Require(definitions.Select(item => item.Identity).Distinct(StringComparer.Ordinal).Count() == definitions.Length, "Composite definitions overlap.");
        int killed = rows.Sum(row => row.Killed), raw = rows.Sum(row => row.RawEligibleDenominator), equivalent = rows.Sum(row => row.ReviewedEquivalent), effective = raw - equivalent;
        Require(rows.Sum(row => row.EffectiveDenominator) == effective && equivalent >= 0 && effective >= 0, "Summed conservative arithmetic differs.");
        Require(killed == definitions.Count(definition => definition.Status == "Killed")
            && raw == definitions.Count(definition => Eligible(definition.Status)) && equivalent <= definitions.Count(definition => definition.Status is "Survived" or "NoCoverage"),
            "Summed assessments differ from the unchanged raw native populations.");
        int runtime = rows.Sum(row => row.RuntimeError), pending = rows.Sum(row => row.Pending);
        bool complete = inventoryComplete && runtime == 0 && pending == 0 && definitions.All(definition => definition.Status is "Killed" or "Survived" or "NoCoverage" or "Timeout" or "CompileError" or "Ignored");
        decimal? score = complete && effective > 0 ? 100m * killed / effective : null;
        if (effective == 0) { holds.Add("No eligible mutation definitions remain for a full-scope gate."); }
        if (score < 90) { holds.Add("The complete disjoint conservative score is below90percent."); }
        return new(killed, rows.Sum(row => row.Survived), rows.Sum(row => row.NoCoverage), rows.Sum(row => row.Timeout), rows.Sum(row => row.CompileError), rows.Sum(row => row.Ignored),
            score, complete, complete && score >= 90 && holds.Count == 0, holds.ToArray())
        {
            RawEligibleDenominator = raw,
            ReviewedEquivalent = equivalent,
            EffectiveDenominator = effective,
            RawConservativeScore = complete && raw > 0 ? 100m * killed / raw : null,
            RuntimeError = runtime,
            Pending = pending,
            SourceInventoryComplete = inventoryComplete,
            SourcePopulations = rows.SelectMany(row => row.SourcePopulations).ToArray()
        };
    }
}
