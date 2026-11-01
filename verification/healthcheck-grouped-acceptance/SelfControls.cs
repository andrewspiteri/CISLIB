using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using static CIS.Recovery.HealthcheckAcceptance.Program;

namespace CIS.Recovery.HealthcheckAcceptance;

// These execute the same admission/arithmetic routines, with synthetic outcomes clearly identified.
// They do not produce native platform, product, child or module qualification.
internal static class SelfControls
{
    internal static int Run(Dictionary<string, string> input)
    {
        string checker = Take(input, "checker"), policyPath = Take(input, "policy"), output = Take(input, "output");
        string sourceRoot = Take(input, "consumer-source"), buildReceipt = Take(input, "consumer-build");
        Require(input.Count == 0, "Unknown self-control argument.");
        var frozen = new FrozenNativeEvidence(checker); JsonObject policy = CompositeAcceptance.Policy(frozen.ReadPinned(policyPath));
        var passed = new List<string>();
        void Pass(string name, Action check) { check(); passed.Add(name); }
        void Reject(string name, Action check)
        {
            bool rejected = false; try { check(); } catch (Exception error) when (error is InvalidDataException or InvalidOperationException or ArgumentException) { rejected = true; }
            Require(rejected, "Negative admission control was accepted: " + name); passed.Add(name);
        }
        Pass("actual-consumer-build-portable-output-and-pdb-source-closure", () => ConsumerIdentity.Postflight(ConsumerIdentity.Validate(sourceRoot, buildReceipt)));
        JsonArray Selection() => new(CompositeAcceptance.Declared.Select(item => (JsonNode)new JsonObject { ["group"] = item.Key, ["platform"] = item.Value }).ToArray());
        Pass("exact-four-whole-group-assignment", () => CompositeAcceptance.ValidateSelection(Selection()));
        Reject("missing-native-group", () => { JsonArray rows = Selection(); rows.RemoveAt(0); CompositeAcceptance.ValidateSelection(rows); });
        Reject("both-pipes-platforms", () => { JsonArray rows = Selection(); rows.Add(new JsonObject { ["group"] = "pipes", ["platform"] = "linux" }); CompositeAcceptance.ValidateSelection(rows); });
        Reject("reassigned-whole-group", () => { JsonArray rows = Selection(); rows[3]!["platform"] = "linux"; CompositeAcceptance.ValidateSelection(rows); });
        Reject("missing-obligation-policy", () => { JsonObject changed = (JsonObject)policy.DeepClone(); changed["linuxPermissionObligations"]!.AsArray().RemoveAt(0); CompositeAcceptance.Policy(changed); });
        Reject("missing-windows-control-policy", () => { JsonObject changed = (JsonObject)policy.DeepClone(); changed["windowsRequiredControls"]!.AsArray().RemoveAt(0); CompositeAcceptance.Policy(changed); });
        Reject("timeout-credit-policy", () => { JsonObject changed = (JsonObject)policy.DeepClone(); changed["timeoutKillCredit"] = true; CompositeAcceptance.Policy(changed); });
        JsonObject Completion() => new() { ["budgetSeconds"] = 600, ["restorationReserveSeconds"] = 90, ["nativeCeilingSeconds"] = 450, ["seconds"] = 599.0,
            ["nativeExitCode"] = 0, ["nativeOutputDrained"] = true, ["outputDrained"] = true, ["restored"] = true, ["cleanupHealthy"] = true, ["failures"] = new JsonArray() };
        Pass("complete-bounded-permission-control", () => TransportPrerequisites.ValidatePermissionCompletion(Completion()));
        foreach (string field in new[] { "nativeOutputDrained", "outputDrained", "restored", "cleanupHealthy" })
        { Reject("failed-" + field, () => { JsonObject changed = Completion(); changed[field] = false; TransportPrerequisites.ValidatePermissionCompletion(changed); }); }
        Reject("infrastructure-failure-marker", () => { JsonObject changed = Completion(); changed["failures"]!.AsArray().Add("actual helper failed"); TransportPrerequisites.ValidatePermissionCompletion(changed); });
        Reject("total-deadline-exceeded", () => { JsonObject changed = Completion(); changed["seconds"] = 600.001; TransportPrerequisites.ValidatePermissionCompletion(changed); });
        Reject("native-exit-failure", () => { JsonObject changed = Completion(); changed["nativeExitCode"] = 1; TransportPrerequisites.ValidatePermissionCompletion(changed); });
        JsonObject Child() => new() { ["schema"] = "cis.recovery.child-operator/v4", ["recordRevision"] = 2, ["candidateIdentity"] = "synthetic-origin", ["complete"] = true, ["nativeCompleted"] = true,
            ["restored"] = true, ["processCleanupHealthy"] = true, ["failures"] = new JsonArray(), ["temporaryCleanup"] = "owned short temp removed",
            ["budgetSeconds"] = 600, ["restorationReserveSeconds"] = 90, ["seconds"] = 599.0, ["noUnionScopeOrScoreCredit"] = true };
        Pass("complete-bounded-child-header-only", () => CompositeAcceptance.ValidateChildCompletion(Child(), "synthetic-origin"));
        Reject("child-original-v3-not-relabelled", () => { JsonObject changed = Child(); changed["schema"] = "cis.recovery.child-operator/v3"; CompositeAcceptance.ValidateChildCompletion(changed, "synthetic-origin"); });
        Reject("child-restoration-failure", () => { JsonObject changed = Child(); changed["restored"] = false; CompositeAcceptance.ValidateChildCompletion(changed, "synthetic-origin"); });
        Reject("replayed-other-candidate-child", () => CompositeAcceptance.ValidateChildCompletion(Child(), "another-origin"));
        Reject("unrevisioned-v4-cannot-be-relabelled-complete", () => { JsonObject changed = Child(); changed.Remove("recordRevision"); CompositeAcceptance.ValidateChildCompletion(changed, "synthetic-origin"); });
        JsonObject Execution()
        {
            JsonObject execution = Child(); execution["schema"] = "cis.recovery.child-operator-execution/v4";
            execution["aggregateLimitBytes"] = 2147483648L; execution["freeReserveBytes"] = 2147483648L; execution["childLimitBytes"] = 134217728L;
            execution["retainedLimitBytes"] = 268435456L; execution["peakOwnedBytes"] = 1000L; execution["peakChildBytes"] = 100L; execution["retainedBytesBeforeManifest"] = 100L;
            execution["failureRecords"] = new JsonArray(); var commands = new JsonArray();
            string[] names = ["baseline-CIS.Core.IntegrationTests", "native", "restored-build", "restored-CIS.Core.IntegrationTests", "audit-http-host", "audit-probe"];
            for (int i = 0; i < names.Length; i++) { commands.Add(new JsonObject { ["name"] = names[i], ["processId"] = i + 1, ["executable"] = "/private/dotnet", ["arguments"] = new JsonArray("synthetic"),
                ["exitCode"] = 0, ["outputDrained"] = true, ["cleanupHealthy"] = true, ["monitorCompleted"] = true, ["capacityFailure"] = null, ["failure"] = null,
                ["startedElapsedSeconds"] = i * 5.0, ["seconds"] = 1.0, ["timeoutSeconds"] = 600.0 - i * 5 - (i == 1 ? 90 : 0), ["restorationReserveSeconds"] = i == 1 ? 90 : 0,
                ["peakOwnedBytes"] = 1000L, ["peakChildBytes"] = 100L }); }
            execution["commands"] = commands; return execution;
        }
        Pass("durable-six-phase-child-guard-terminal", () =>
        {
            JsonObject execution = Execution(); execution["retainedBytesBeforeManifest"] = 267911168L;
            CompositeAcceptance.ValidateChildExecutionRecord(Child(), execution, "synthetic-origin");
            foreach (string number in new[] { "505", "504.5" })
            {
                JsonObject parsed = JsonNode.Parse(execution.ToJsonString(Json))!.AsObject(); parsed["commands"]![1]!["timeoutSeconds"] = JsonNode.Parse(number);
                CompositeAcceptance.ValidateChildExecutionRecord(Child(), parsed, "synthetic-origin");
            }
            execution["retainedBytesBeforeManifest"] = 267911169L; bool rejected = false;
            try { CompositeAcceptance.ValidateChildExecutionRecord(Child(), execution, "synthetic-origin"); } catch (InvalidDataException) { rejected = true; }
            Require(rejected, "The child terminal borrowed the second bounded record allowance.");
        });
        Reject("missing-child-phase-capacity-terminal-result", () => { JsonObject changed = Execution(); changed["commands"]![1]!.AsObject().Remove("capacityFailure"); CompositeAcceptance.ValidateChildExecutionRecord(Child(), changed, "synthetic-origin"); });
        Reject("caught-child-capacity-failure-forbids-admission", () => { JsonObject changed = Execution(); changed["commands"]![1]!["capacityFailure"] = "actual capacity breach"; CompositeAcceptance.ValidateChildExecutionRecord(Child(), changed, "synthetic-origin"); });
        Reject("child-guard-peak-cannot-exceed-original-cap", () => { JsonObject changed = Execution(); changed["peakOwnedBytes"] = 2147483649L; CompositeAcceptance.ValidateChildExecutionRecord(Child(), changed, "synthetic-origin"); });
        Reject("child-native-cannot-borrow-restoration-reserve", () => { JsonObject changed = Execution(); changed["commands"]![1]!["timeoutSeconds"] = 600.0; CompositeAcceptance.ValidateChildExecutionRecord(Child(), changed, "synthetic-origin"); });
        Reject("child-terminal-over-original600-deadline", () => { JsonObject changed = Execution(); changed["seconds"] = 600.001; CompositeAcceptance.ValidateChildExecutionRecord(Child(), changed, "synthetic-origin"); });
        Reject("child-terminal-cannot-drop-durable-failure-record", () => { JsonObject changed = Execution(); changed["failureRecords"]!.AsArray().Add("actual failure record"); CompositeAcceptance.ValidateChildExecutionRecord(Child(), changed, "synthetic-origin"); });
        Reject("child-terminal-requires-both-real-audits", () => { JsonObject changed = Execution(); changed["commands"]!.AsArray().RemoveAt(5); CompositeAcceptance.ValidateChildExecutionRecord(Child(), changed, "synthetic-origin"); });
        Reject("child-native-command-cannot-add-undeclared-argument", () => CompositeAcceptance.ValidateChildCommandArguments(JsonNode.Parse("{\"executable\":\"/private/dotnet\",\"arguments\":[\"actual\",\"extra\"]}")!, "/private/dotnet", ["actual"]));
        Pass("two-original-child-kinds", () => CompositeAcceptance.ValidateChildRows(new JsonArray(new JsonObject { ["kind"] = "http-host" }, new JsonObject { ["kind"] = "probe" })));
        Reject("missing-real-probe-child", () => CompositeAcceptance.ValidateChildRows(new JsonArray(new JsonObject { ["kind"] = "http-host" })));
        Reject("duplicate-child-cannot-substitute", () => CompositeAcceptance.ValidateChildRows(new JsonArray(new JsonObject { ["kind"] = "http-host" }, new JsonObject { ["kind"] = "http-host" })));
        const string cli = "/pinned/Stryker.CLI.dll", config = "/candidate/groups/pipes/stryker-config.json", native = "/candidate/groups/pipes/native";
        JsonObject tools = new() { ["toolDotnet"] = "/private/dotnet", ["files"] = new JsonArray(new JsonObject { ["path"] = cli }) };
        JsonObject Command() => new() { ["commandLog"] = new JsonArray(new JsonObject { ["name"] = "native", ["executable"] = "/private/dotnet", ["exitCode"] = 0,
            ["outputDrained"] = true, ["timeoutSeconds"] = 450.0, ["seconds"] = 200.0,
            ["arguments"] = JsonSerializer.SerializeToNode(new[] { "exec", "--fx-version", "8.0.31", "--roll-forward", "Disable", cli, "--config-file", config, "--output", native, "--skip-version-check" }, Json) }) };
        Pass("actual-command-fixed-profile", () => TransportPrerequisites.ValidateControlCommand(Command(), tools, config, native));
        Reject("replayed-config-command", () => TransportPrerequisites.ValidateControlCommand(Command(), tools, "/another/config.json", native));
        Reject("wrong-tool-runtime", () => { JsonObject changed = Command(); changed["commandLog"]![0]!["arguments"]![2] = "8.0.32"; TransportPrerequisites.ValidateControlCommand(changed, tools, config, native); });
        Reject("native-ceiling-extension", () => { JsonObject changed = Command(); changed["commandLog"]![0]!["timeoutSeconds"] = 451.0; TransportPrerequisites.ValidateControlCommand(changed, tools, config, native); });
        JsonObject NativeTests() => new() { ["name"] = "baseline-CIS.Core.UnitTests", ["executable"] = "/private/dotnet",
            ["arguments"] = JsonSerializer.SerializeToNode(new[] { "test", "/original-candidate/workspace/implementations/csharp/tests/CIS.Core.UnitTests/CIS.Core.UnitTests.csproj", "-c", "Release", "--no-build", "--no-restore", "--filter", "actual-filter", "--logger", "trx;LogFileName=baseline-CIS.Core.UnitTests.trx", "--results-directory", "/original-candidate/groups/pilot" }, Json) };
        Pass("copied-evidence-keeps-original-native-assembly-origin", () => Require(FrozenNativeEvidence.OriginalTestAssembly(NativeTests(), "CIS.Core.UnitTests", "actual-filter", "/private/dotnet", false)
            == "/original-candidate/workspace/implementations/csharp/tests/CIS.Core.UnitTests/bin/Release/net10.0/CIS.Core.UnitTests.dll", "Evidence relocation changed original execution provenance."));
        Reject("changed-native-test-selection", () => FrozenNativeEvidence.OriginalTestAssembly(NativeTests(), "CIS.Core.UnitTests", "reduced-filter", "/private/dotnet", false));
        Reject("relative-original-native-project", () => { JsonObject command = NativeTests(); command["arguments"]![1] = "tests/CIS.Core.UnitTests/CIS.Core.UnitTests.csproj"; FrozenNativeEvidence.OriginalTestAssembly(command, "CIS.Core.UnitTests", "actual-filter", "/private/dotnet", false); });
        JsonObject candidateNode = new() { ["schema"] = "self-control", ["identity"] = "synthetic-origin", ["platform"] = "synthetic", ["host"] = "self-control", ["inputs"] = new JsonArray(),
            ["sources"] = JsonSerializer.SerializeToNode(new[] { "a.cs", "b.cs" }, Json), ["archiveSha256"] = "synthetic" };
        object candidate = frozen.Decode("Candidate", candidateNode), authority = frozen.Decode("Authority", new JsonObject { ["policy"] = "synthetic admission controls only", ["reviewers"] = new JsonArray("source-review") });
        JsonArray headerInputs = new(new JsonObject { ["path"] = "src/a.cs", ["sha256"] = new string('a', 64) }, new JsonObject { ["path"] = "tests/b.cs", ["sha256"] = new string('b', 64) });
        object PlatformCandidate(JsonArray inputs, string platform, string host, string identity) => frozen.Decode("Candidate", new JsonObject
        {
            ["schema"] = "synthetic-header-control", ["identity"] = identity, ["platform"] = platform, ["host"] = host,
            ["inputs"] = inputs.DeepClone(), ["sources"] = new JsonArray(), ["archiveSha256"] = "synthetic"
        });
        object windows = PlatformCandidate(headerInputs, "Microsoft Windows fixture; X64; .NET 10.0.12", "windows-control", "synthetic-windows");
        object linux = PlatformCandidate(headerInputs, "Ubuntu fixture; X64; .NET 10.0.12", "linux-control", "synthetic-linux");
        JsonObject Header(object origin) => new()
        {
            ["schema"] = "cis.healthcheck.platform-native-proof/v1", ["group"] = "pipes", ["originCandidateIdentity"] = FrozenNativeEvidence.Property<string>(origin, "Identity"),
            ["platform"] = FrozenNativeEvidence.Property<string>(origin, "Platform"), ["host"] = FrozenNativeEvidence.Property<string>(origin, "Host"),
            ["executionIdentity"] = new JsonObject { ["operatingSystem"] = "windows", ["userSid"] = "synthetic-user", ["ownerSid"] = "synthetic-owner" },
            ["commonInputsSha256"] = frozen.CommonWithNewLine(origin, "\r\n"), ["producerSha256"] = FrozenNativeEvidence.CheckerSha, ["helperSha256"] = FrozenNativeEvidence.HelperSha
        };
        Pass("equal-typed-inputs-keep-original-CRLF-and-consumer-LF-digests", () =>
        {
            JsonObject proof = Header(windows); string original = proof.ToJsonString();
            string crlf = frozen.CommonWithNewLine(windows, "\r\n"), lf = frozen.CommonWithNewLine(linux, "\n");
            JsonObject result = frozen.ValidatePortableWindowsHeader(proof, windows, linux);
            Require(crlf != lf && (string?)result["originalProducerCommonInputsSha256"] == crlf && (string?)result["consumerCommonInputsSha256"] == lf
                && proof.ToJsonString() == original && (bool?)result["originalProofDigestRewritten"] == false, "Portable admission changed an original digest or ignored JSON line endings.");
        });
        void ChangedInput(Action<JsonArray> change)
        {
            JsonArray inputs = (JsonArray)headerInputs.DeepClone(); change(inputs);
            object changed = PlatformCandidate(inputs, "Microsoft Windows fixture; X64; .NET 10.0.12", "windows-control", "synthetic-windows");
            frozen.ValidatePortableWindowsHeader(Header(changed), changed, linux);
        }
        Reject("portable-header-rejects-changed-source-sha-even-with-new-producer-digest", () => ChangedInput(inputs => inputs[0]!["sha256"] = new string('c', 64)));
        Reject("portable-header-rejects-changed-path-even-with-new-producer-digest", () => ChangedInput(inputs => inputs[0]!["path"] = "src/another.cs"));
        Reject("portable-header-rejects-reordered-exact-same-input-set", () => ChangedInput(inputs => { JsonNode first = inputs[0]!.DeepClone(); inputs.RemoveAt(0); inputs.Add(first); }));
        Reject("Windows-proof-cannot-substitute-consumer-LF-digest", () => { JsonObject proof = Header(windows); proof["commonInputsSha256"] = frozen.CommonWithNewLine(windows, "\n"); frozen.ValidatePortableWindowsHeader(proof, windows, linux); });
        Reject("portable-header-keeps-original-producer-check", () => { JsonObject proof = Header(windows); proof["producerSha256"] = new string('0', 64); frozen.ValidatePortableWindowsHeader(proof, windows, linux); });
        Reject("portable-header-keeps-original-helper-check", () => { JsonObject proof = Header(windows); proof["helperSha256"] = new string('0', 64); frozen.ValidatePortableWindowsHeader(proof, windows, linux); });
        Reject("portable-header-keeps-runtime-check", () => { JsonObject proof = Header(windows); proof["platform"] = "Microsoft Windows fixture; X64; .NET 10.0.13"; frozen.ValidatePortableWindowsHeader(proof, windows, linux); });
        Reject("portable-header-keeps-Windows-platform-check", () => { JsonObject proof = Header(windows); proof["platform"] = "Ubuntu fixture; X64; .NET 10.0.12"; frozen.ValidatePortableWindowsHeader(proof, windows, linux); });
        Reject("portable-header-keeps-x64-check", () => { JsonObject proof = Header(windows); proof["platform"] = "Microsoft Windows fixture; Arm64; .NET 10.0.12"; frozen.ValidatePortableWindowsHeader(proof, windows, linux); });
        Reject("portable-header-keeps-user-SID-check", () => { JsonObject proof = Header(windows); proof["executionIdentity"]!["userSid"] = ""; frozen.ValidatePortableWindowsHeader(proof, windows, linux); });
        Reject("portable-header-keeps-owner-SID-check", () => { JsonObject proof = Header(windows); proof["executionIdentity"]!["ownerSid"] = ""; frozen.ValidatePortableWindowsHeader(proof, windows, linux); });
        Reject("portable-header-keeps-actual-OS-check", () => { JsonObject proof = Header(windows); proof["executionIdentity"]!["operatingSystem"] = "linux"; frozen.ValidatePortableWindowsHeader(proof, windows, linux); });
        Reject("portable-header-keeps-schema-check", () => { JsonObject proof = Header(windows); proof["schema"] = "unknown"; frozen.ValidatePortableWindowsHeader(proof, windows, linux); });
        Reject("portable-header-keeps-whole-Pipes-group-check", () => { JsonObject proof = Header(windows); proof["group"] = "runtime"; frozen.ValidatePortableWindowsHeader(proof, windows, linux); });
        object Definition(string id, string source, string status) => frozen.Decode("Definition", new JsonObject { ["identity"] = id, ["source"] = source, ["sourceSha256"] = "synthetic", ["nativeId"] = id,
            ["operator"] = "Statement mutation", ["location"] = "{}", ["replacement"] = ";", ["status"] = status, ["statusReason"] = "synthetic control" });
        JsonObject Review(string id, string status, string decision) => new() { ["candidateIdentity"] = "synthetic-origin", ["definitionIdentity"] = id, ["rawStatus"] = status, ["decision"] = decision,
            ["requirement"] = "self-control-only", ["reviewer"] = "source-review", ["reviewedAtUtc"] = "2026-10-30T00:00:00Z", ["rationale"] = "Synthetic admission test; supplies no native product evidence." };
        JsonObject Assess(object[] definitions, JsonArray reviews, string[]? sources = null, Array? admissions = null) => FrozenNativeEvidence.Node(frozen.Call("Assess", candidate, sources ?? new[] { "a.cs", "b.cs" },
            frozen.ArrayOf("Definition", definitions), frozen.DecodeArray("Review", reviews), authority, Array.Empty<string>(), admissions ?? frozen.ArrayOf("PlatformAdmission", []))!).AsObject();
        Pass("reviewed-timeout-full-denominator-zero-kill", () =>
        {
            JsonObject result = Assess([Definition("k", "a.cs", "Killed"), Definition("t", "b.cs", "Timeout")], new JsonArray(Review("t", "Timeout", "ReviewedTimeout")));
            Require((int?)result["killed"] == 1 && (int?)result["rawEligibleDenominator"] == 2 && (int?)result["effectiveDenominator"] == 2, "Timeout gained kill/exclusion credit.");
        });
        Reject("timeout-cannot-be-equivalent", () => Assess([Definition("t", "a.cs", "Timeout")], new JsonArray(Review("t", "Timeout", "Equivalent"))));
        Reject("wrong-native-origin-review", () => { JsonObject row = Review("s", "Survived", "ReviewedNoncritical"); row["candidateIdentity"] = "another-origin"; Assess([Definition("s", "a.cs", "Survived")], new JsonArray(row)); });
        Reject("replayed-old-status-review", () => Assess([Definition("s", "a.cs", "NoCoverage")], new JsonArray(Review("s", "Survived", "ReviewedNoncritical"))));
        Reject("platform-label-without-real-proof", () => Assess([Definition("s", "a.cs", "NoCoverage")], new JsonArray(Review("s", "NoCoverage", "PlatformCovered"))));
        string permissionIdentity = (string)policy["linuxPermissionObligations"]![0]!["definitionIdentity"]!;
        Reject("known-platform-obligation-cannot-subtract-equivalent-denominator", () => CompositeAcceptance.RequirePlatformReviews(new JsonArray(Review(permissionIdentity, "NoCoverage", "Equivalent")), [permissionIdentity]));
        Reject("known-critical-platform-obligation-cannot-be-renamed-noncritical", () => CompositeAcceptance.RequirePlatformReviews(new JsonArray(Review(permissionIdentity, "Survived", "ReviewedNoncritical")), [permissionIdentity]));
        Pass("platform-covered-keeps-chosen-status-full-denominator-zero-kill", () =>
        {
            JsonObject row = Review(permissionIdentity, "NoCoverage", "PlatformCovered"); string sha = new('a', 64);
            row["platformProof"] = new JsonObject { ["path"] = "/synthetic/proof.json", ["sha256"] = sha };
            var rows = new JsonArray(row); CompositeAcceptance.RequirePlatformReviews(rows, [permissionIdentity]);
            object admission = frozen.Decode("PlatformAdmission", new JsonObject { ["definitionIdentity"] = permissionIdentity, ["proofSha256"] = sha,
                ["originCandidateIdentity"] = "synthetic-control-origin", ["platform"] = "synthetic", ["host"] = "self-control", ["witnessSource"] = "tests/synthetic.cs", ["witnessName"] = "synthetic-case" });
            JsonObject result = Assess([Definition("k", "a.cs", "Killed"), Definition(permissionIdentity, "b.cs", "NoCoverage")], rows, admissions: frozen.ArrayOf("PlatformAdmission", [admission]));
            Require((int?)result["killed"] == 1 && (int?)result["noCoverage"] == 1 && (int?)result["rawEligibleDenominator"] == 2
                && (int?)result["effectiveDenominator"] == 2 && (int?)result["reviewedEquivalent"] == 0, "Platform proof changed the chosen outcome or added kill/exclusion credit.");
        });
        Pass("unreviewed-miss-remains-held", () => Require(Assess([Definition("s", "a.cs", "Survived")], new JsonArray())["holds"]!.AsArray().Any(row => row!.GetValue<string>().StartsWith("Unreviewed", StringComparison.Ordinal)), "An unreviewed miss vanished."));
        Pass("sum-resolves-only-disjoint-structural-hold", () =>
        {
            JsonObject one = Assess([Definition("a", "a.cs", "Killed")], new JsonArray(), ["a.cs"]);
            JsonObject two = Assess([Definition("b", "b.cs", "Killed")], new JsonArray(), ["b.cs"]);
            JsonObject total = CompositeAcceptance.Sum([one, two]); Require((bool?)total["qualified"] == true && (int?)total["killed"] == 2, "The proven disjoint union failed arithmetic.");
        });
        Reject("unknown-structural-hold-never-dropped", () => { JsonObject partial = Assess([Definition("a", "a.cs", "Killed")], new JsonArray(), ["a.cs"]); partial["holds"]![0] = "Infrastructure failed"; CompositeAcceptance.Sum([partial]); });
        Reject("span-column-crosses-line", () => TransportPrerequisites.Point("ab\ncd", JsonNode.Parse("{\"start\":{\"line\":1,\"column\":5}}")!, "start"));
        string scratch = Path.Combine(Path.GetDirectoryName(output)!, "consumer-pin-control-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(scratch);
        try
        {
            string recordsRoot = Path.Combine(scratch, "child-record-controls"); Directory.CreateDirectory(recordsRoot);
            const string common = "synthetic-common", windowsProof = "/synthetic/windows/proof.json", windowsSha = "synthetic-windows-sha", permissionSha = "synthetic-linux-sha", consumerSha = "synthetic-consumer-build";
            JsonObject Prerequisite() => new() { ["schema"] = "cis.healthcheck.transport-prerequisites/v2", ["commonInputsSha256"] = common,
                ["windowsProofPath"] = windowsProof, ["windowsProofSha256"] = windowsSha,
                ["linuxPermissionProof"] = new JsonObject { ["sha256"] = permissionSha }, ["consumer"] = new JsonObject { ["buildReceiptSha256"] = consumerSha },
                ["fullSourceEvidence"] = new string('x', 180000) };
            JsonObject FilePin(string file) => new() { ["path"] = file, ["sha256"] = FrozenNativeEvidence.Hash(file), ["bytes"] = new FileInfo(file).Length };
            JsonObject Records(JsonObject? before = null, JsonObject? after = null)
            {
                before ??= Prerequisite(); after ??= (JsonObject)before.DeepClone(); JsonObject manifest = Child(); manifest["actualEvidencePins"] = new JsonArray();
                foreach ((string field, string name, string summary, JsonObject receipt) in new[] {
                    ("transportPrerequisitesRecord", "transport-prerequisites-before.json", "transportPrerequisites", before),
                    ("postflightTransportPrerequisitesRecord", "transport-prerequisites-after.json", "postflightTransportPrerequisites", after) })
                {
                    string file = Path.Combine(recordsRoot, name); File.WriteAllText(file, receipt.ToJsonString(Json)); JsonObject pin = FilePin(file);
                    manifest[field] = pin; manifest["actualEvidencePins"]!.AsArray().Add(pin.DeepClone()); manifest[summary] = CompositeAcceptance.ChildPrerequisiteSummary(receipt);
                }
                return manifest;
            }
            void AdmitRecords(JsonObject records) => CompositeAcceptance.ValidateChildPrerequisiteRecords(frozen, records, recordsRoot, common, windowsProof, windowsSha, permissionSha, consumerSha);
            Pass("full-child-prerequisite-records-pinned-with-bounded-summary", () => { JsonObject records = Records(); AdmitRecords(records);
                Require(records.ToJsonString().Length < 4096 && new FileInfo(Path.Combine(recordsRoot, "transport-prerequisites-before.json")).Length > 180000, "A compact summary discarded or inlined the full prerequisite record."); });
            Reject("missing-full-child-prerequisite-reference", () => { JsonObject records = Records(); records.Remove("transportPrerequisitesRecord"); AdmitRecords(records); });
            Reject("tampered-full-child-prerequisite-record", () => { JsonObject records = Records(); File.AppendAllText(Path.Combine(recordsRoot, "transport-prerequisites-after.json"), " "); AdmitRecords(records); });
            Reject("child-prerequisite-record-cannot-use-wrong-proof", () => { JsonObject receipt = Prerequisite(); receipt["linuxPermissionProof"]!["sha256"] = "other-proof"; AdmitRecords(Records(receipt)); });
            Reject("child-prerequisite-full-before-after-identity-must-match", () => { JsonObject before = Prerequisite(), after = Prerequisite(); after["fullSourceEvidence"] = "different bound source closure"; AdmitRecords(Records(before, after)); });
            Reject("child-prerequisite-summary-alone-cannot-claim-admission", () => { JsonObject records = Records(); records["transportPrerequisites"]!["consumer"]!["buildReceiptSha256"] = "invented-build"; AdmitRecords(records); });
            Reject("child-execution-terminal-record-reference-required", () => CompositeAcceptance.ReadChildRecord(frozen, Records(), "executionRecord", recordsRoot, "execution-record.json"));
            JsonObject PhaseRecords(JsonObject execution)
            {
                var manifest = new JsonObject { ["actualEvidencePins"] = new JsonArray() }; int index = 0;
                foreach (JsonNode? command in execution["commands"]!.AsArray())
                {
                    JsonObject observed = command!.AsObject();
                    string file = Path.Combine(recordsRoot, "phase-" + (++index).ToString("D2", System.Globalization.CultureInfo.InvariantCulture) + "-" + (string)observed["name"]! + ".json");
                    File.WriteAllText(file, observed.ToJsonString(Json)); JsonObject pin = FilePin(file); observed["record"] = pin; manifest["actualEvidencePins"]!.AsArray().Add(pin.DeepClone());
                }
                return manifest;
            }
            Pass("actual-child-phase-files-match-durable-terminal", () => { JsonObject execution = Execution(), records = PhaseRecords(execution); CompositeAcceptance.ValidateChildPhaseRecords(frozen, records, execution, recordsRoot); });
            Reject("rehashing-a-tampered-child-phase-cannot-rewrite-terminal", () => { JsonObject execution = Execution(), records = PhaseRecords(execution);
                JsonObject phase = execution["commands"]![1]!.AsObject(); string file = (string)phase["record"]!["path"]!; JsonObject changed = FrozenNativeEvidence.Read(file); changed["monitorCompleted"] = false;
                File.WriteAllText(file, changed.ToJsonString(Json)); JsonObject pin = FilePin(file); phase["record"] = pin; records["actualEvidencePins"]![1] = pin.DeepClone();
                CompositeAcceptance.ValidateChildPhaseRecords(frozen, records, execution, recordsRoot); });
            string nativeRoot = Path.Combine(scratch, "linked-source-control"); Directory.CreateDirectory(nativeRoot);
            string[] linked = ["HealthcheckQualification/Mutation/Children/HealthcheckMutationChildProtocol.cs", "HealthcheckQualification/Mutation/Children/HealthcheckMutationChildSupport.cs",
                "HealthcheckQualification/Mutation/Children/HealthcheckMutationChildCapture.cs", "JsonlInputSelector.cs"];
            var sourceInputs = new JsonArray(); var sourceText = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string name in linked)
            {
                string canonical = FrozenNativeEvidence.Language + "eng/CIS.DependencyChecker/" + name;
                string path = Path.Combine(nativeRoot, "workspace", canonical), text = "// Synthetic linked-source admission control: " + name + "\n";
                Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text); sourceText.Add(canonical, text);
                sourceInputs.Add(new JsonObject { ["path"] = canonical, ["sha256"] = FrozenNativeEvidence.Hash(path) });
            }
            object nativeCandidate = frozen.Decode("Candidate", new JsonObject { ["schema"] = "synthetic-source-control", ["identity"] = "synthetic-linked-origin",
                ["platform"] = "synthetic", ["host"] = "self-control", ["inputs"] = sourceInputs, ["sources"] = new JsonArray(), ["archiveSha256"] = "synthetic" });
            string archive = Path.Combine(nativeRoot, "candidate.zip");
            using (ZipArchive zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            {
                foreach (string canonical in sourceText.Keys)
                {
                    using Stream destination = zip.CreateEntry(canonical).Open();
                    using Stream original = File.OpenRead(Path.Combine(nativeRoot, "workspace", canonical)); original.CopyTo(destination);
                }
            }
            Pass("linked-source-real-archive-retains-exact-canonical-members", () => frozen.Call("VerifyPlatformArchive", archive, FrozenNativeEvidence.Property<object>(nativeCandidate, "Inputs")));
            foreach (string name in linked)
            {
                string alias = FrozenNativeEvidence.Language + "tests/CIS.Core.IntegrationTests/../../eng/CIS.DependencyChecker/" + name;
                string canonical = FrozenNativeEvidence.Language + "eng/CIS.DependencyChecker/" + name;
                Pass("captured-linked-path-shape-" + Path.GetFileName(name), () => Require(frozen.Source(nativeRoot, nativeCandidate, alias) == sourceText[canonical]
                    && FrozenNativeEvidence.CanonicalSourcePath(alias) == canonical, "The linked relative alias did not bind its exact captured source."));
            }
            string reported = FrozenNativeEvidence.Language + "tests/CIS.Core.IntegrationTests/../../eng/CIS.DependencyChecker/" + linked[0];
            string expectedPath = FrozenNativeEvidence.Language + "eng/CIS.DependencyChecker/" + linked[0];
            Pass("linked-source-normalizes-backslashes-and-dot-without-filesystem-resolution", () => Require(frozen.Source(nativeRoot, nativeCandidate,
                reported.Replace("/eng/", "/./eng/", StringComparison.Ordinal).Replace('/', '\\')) == sourceText[expectedPath], "Separator/dot normalization changed source membership."));
            foreach ((string name, string path) in new[] { ("root-escape", "../../eng/source.cs"), ("language-escape", "implementations/csharp/../csharp/eng/source.cs"),
                ("outside-candidate", "implementations/csharp/../../README.md"), ("absolute", "/implementations/csharp/eng/source.cs"),
                ("drive", "C:\\implementations\\csharp\\eng\\source.cs"), ("UNC", "\\\\server\\share\\implementations\\csharp\\eng\\source.cs") })
            { Reject("linked-source-rejects-" + name, () => frozen.Source(nativeRoot, nativeCandidate, path)); }
            Reject("linked-source-cannot-add-unlisted-candidate-input", () => frozen.Source(nativeRoot, nativeCandidate, FrozenNativeEvidence.Language + "eng/Missing.cs"));
            string changedSource = Path.Combine(nativeRoot, "workspace", expectedPath); File.AppendAllText(changedSource, "// source drift\n");
            Reject("linked-source-cannot-use-mismatched-captured-sha", () => frozen.Source(nativeRoot, nativeCandidate, reported));
            foreach (string source in ConsumerIdentity.Sources) { File.Copy(Path.Combine(sourceRoot, source), Path.Combine(scratch, source)); }
            Pass("source-relocation-keeps-actual-pdb-checksums", () => ConsumerIdentity.Validate(scratch, buildReceipt));
            File.AppendAllText(Path.Combine(scratch, "Program.cs"), "\n// deliberate self-control source drift\n");
            Reject("changed-consumer-source-cannot-use-old-build", () => ConsumerIdentity.Validate(scratch, buildReceipt));
            JsonObject receipt = FrozenNativeEvidence.Read(buildReceipt); receipt["portableOutputPins"]!.AsArray().RemoveAt(0);
            string changedReceipt = Path.Combine(scratch, "changed-build.json"); File.WriteAllText(changedReceipt, receipt.ToJsonString(Json));
            Reject("missing-compiled-consumer-output", () => ConsumerIdentity.Validate(sourceRoot, changedReceipt));
            string file = Path.Combine(scratch, "input.txt"); File.WriteAllText(file, "before"); frozen.PinFile(file); File.WriteAllText(file, "after");
            Reject("actual-file-drift-detected", frozen.Postflight);
        }
        finally { Directory.Delete(scratch, true); }
        WriteFresh(output, new JsonObject { ["schema"] = "cis.healthcheck.analysis-consumer-self-controls/v1", ["passed"] = JsonSerializer.SerializeToNode(passed, Json),
            ["count"] = passed.Count, ["qualifiedNativeProductEvidence"] = false, ["syntheticArithmeticOnly"] = true, ["frozenExecutorSha256"] = FrozenNativeEvidence.CheckerSha });
        Console.WriteLine("Self-controls passed: " + passed.Count); return 0;
    }
}
