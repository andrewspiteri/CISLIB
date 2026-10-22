using System.Buffers.Binary;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Healthcheck;
using CIS.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Verification.HealthcheckMutationUnionModel;
using Finite = CIS.Verification.HealthcheckMutationFiniteAssertionValidation;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class HealthcheckMutationFiniteAssertionTests
{
    [TestMethod]
    public void AnActualManagedPortablePdbMustMatchBothPeContentIdAndChecksum()
    {
        using var fixture = new PortablePdbFixture();
        HealthcheckMutationPortablePdbValidation.Binding binding = HealthcheckMutationPortablePdbValidation.Read(fixture.Assembly, fixture.Symbols);
        Assert.IsTrue(binding.ContentIdMatches);
        Assert.IsTrue(binding.ChecksumMatches);
        Assert.IsTrue(binding.Matches);
        Assert.AreEqual(false, (bool?)binding.Details()["loadedSymbolClaim"]);
    }

    [TestMethod]
    [DataRow("pdb-id", false, true)]
    [DataRow("pdb-payload", true, false)]
    [DataRow("pe-codeview", false, true)]
    [DataRow("pe-checksum", true, false)]
    public void PortablePdbIdentityAndChecksumAreIndependentPhysicalRequirements(string mode, bool contentId, bool checksum)
    {
        using var fixture = new PortablePdbFixture(); fixture.Change(mode);
        HealthcheckMutationPortablePdbValidation.Binding binding = HealthcheckMutationPortablePdbValidation.Read(fixture.Assembly, fixture.Symbols);
        Assert.AreEqual(contentId, binding.ContentIdMatches);
        Assert.AreEqual(checksum, binding.ChecksumMatches);
        Assert.IsFalse(binding.Matches);
    }

    [TestMethod]
    [DataRow("missing-checksum")]
    [DataRow("duplicate-checksum")]
    [DataRow("unsupported-algorithm")]
    [DataRow("missing-codeview")]
    [DataRow("age")]
    [DataRow("malformed-pdb")]
    public void UnsupportedOrAmbiguousSymbolMetadataCannotAuthorizeNormalization(string mode)
    {
        using var fixture = new PortablePdbFixture(); fixture.Change(mode);
        Assert.ThrowsExactly<InvalidDataException>(() => HealthcheckMutationPortablePdbValidation.Read(fixture.Assembly, fixture.Symbols));
    }

    [TestMethod]
    [DataRow("other-role")]
    [DataRow("unknown-before")]
    [DataRow("unknown-after")]
    [DataRow("already-clean")]
    public void OnlyTheReviewedStaleTestSymbolTupleCanSelectDeeperPhysicalChecks(string mode)
    {
        string role = "implementations/csharp/tests/CIS.Core.UnitTests/bin/Release/net10.0/CIS.Core.pdb";
        string before = HealthcheckMutationFiniteAssertionCatalog.CancellationStaleTestPdbSha256, after = HealthcheckMutationFiniteAssertionCatalog.CancellationCleanCorePdbSha256;
        // Admission merely selects the metadata checks; it is never proof of a
        // native outcome, valid symbol pair or historical loaded-symbol identity.
        Assert.IsTrue(HealthcheckMutationFiniteCancellationEvidence.AdmitsSymbolRole(role, before, after));
        switch (mode)
        {
            case "other-role": role = role.Replace("CIS.Core.pdb", "CIS.Core.UnitTests.pdb", StringComparison.Ordinal); break;
            case "unknown-before": before = Hash('a'); break;
            case "unknown-after": after = Hash('a'); break;
            case "already-clean": before = after; break;
            default: throw new InvalidOperationException();
        }
        Assert.IsFalse(HealthcheckMutationFiniteCancellationEvidence.AdmitsSymbolRole(role, before, after));
    }

    [TestMethod]
    public void ExactRecordedFinitePhasePinsCannotBeSuppliedByCallerMetadata()
    {
        string[] phases = ["initialTrx", "initialProcess", "freshTrx", "freshProcess", "freshBuildProcess", "backupAbsence", "sourceAfterNative", "sourceAfterFresh", "marker"];
        string[] pins = phases.Select(HealthcheckMutationFiniteAssertionCatalog.CancellationPhaseSha256).ToArray();
        Assert.AreEqual(phases.Length, pins.Distinct(StringComparer.Ordinal).Count());
        Assert.AreEqual("3ba1e734ad5ec0fd137b49596b2fed68c96db453dc1a8d6514c6647853c4acad", pins[0]);
        Assert.ThrowsExactly<InvalidDataException>(() => HealthcheckMutationFiniteAssertionCatalog.CancellationPhaseSha256("approved"));
    }

    [TestMethod]
    public void CurrentCancellationKeepsTheOriginalKilledTupleAndRequiresBothActualReceivingRows()
    {
        Finite.Spec spec = HealthcheckMutationFiniteAssertionCatalog.CurrentCancellation;
        Assert.AreEqual("bce78201212534522fee814c6fafa733cebf75dbd70272070aa75ae16b037fb5", spec.Original.Identity);
        Assert.AreEqual("Killed", spec.Original.Status);
        Assert.HasCount(43, spec.Original.KilledBy);
        Assert.HasCount(2, Finite.TargetGuids(spec));
        Assert.IsTrue(Finite.TargetGuids(spec).All(spec.Original.KilledBy.Contains));
        HealthcheckMutationFiniteCancellationEvidence.Document(CancellationDocument());
        var source = new SourceFixture(cancellation: true);
        Assert.AreEqual("Killed", source.Parse().Single().Status);
        using var witness = new WitnessFixture(cancellation: true);
        Finite.NativeWitness(witness.Audit(), [witness.Target], witness.Spec, [witness.HostPath]);
    }

    [TestMethod]
    [DataRow("schema")]
    [DataRow("lane")]
    [DataRow("original")]
    [DataRow("native-root")]
    [DataRow("approval")]
    [DataRow("invented-exit")]
    [DataRow("missing-body-root")]
    public void TheContemporaneousCancellationProfileCannotUseAnOldCaptureOrAnApprovalFlag(string mode)
    {
        JsonObject document = CancellationDocument();
        switch (mode)
        {
            case "schema": document["schema"] = Finite.Schema; break;
            case "lane": document["osLane"] = "windows"; break;
            case "original": document["original"]!["definitionIdentity"] = HealthcheckMutationFiniteAssertionCatalog.IdleWithdrawal.Original.Identity; break;
            case "native-root": document["finite"]!["nativeSourceRoot"] = HealthcheckMutationFiniteAssertionCatalog.NativeRoot; break;
            case "approval": document["approved"] = true; break;
            case "invented-exit": document["finite"]!["nativeExit"] = "0"; break;
            case "missing-body-root": document["capture"]!.AsObject().Remove("bodyRoot"); break;
            default: throw new InvalidOperationException();
        }
        Assert.ThrowsExactly<InvalidDataException>(() => HealthcheckMutationFiniteCancellationEvidence.Document(document));
    }

    [TestMethod]
    [DataRow("missing-witness")]
    [DataRow("missing-result")]
    [DataRow("wrong-second-assertion")]
    [DataRow("same-guid-twice")]
    [DataRow("sibling-witness")]
    public void TheFirstCancellationFailureCannotStandInForTheSecondDeclaredRow(string mode)
    {
        using var fixture = new WitnessFixture(cancellation: true); NativeAuditResult audit = fixture.Audit();
        switch (mode)
        {
            case "missing-witness": audit.Observations["witnessSamples"]!.AsArray().RemoveAt(1); break;
            case "missing-result": fixture.SecondResult = null; break;
            case "wrong-second-assertion": fixture.SecondResult!["ErrorMessage"] = "Assertion failed. Unrelated receiving assertion."; break;
            case "same-guid-twice": fixture.SecondResult!["TestCase"]!["Id"] = fixture.Spec.TargetGuid; break;
            case "sibling-witness": audit.Observations["witnessSamples"]![1]!["mutantId"] = "1903"; break;
            default: throw new InvalidOperationException();
        }
        fixture.Write();
        Assert.ThrowsExactly<InvalidDataException>(() => Finite.NativeWitness(audit, [fixture.Target], fixture.Spec, [fixture.HostPath]));
    }

    [TestMethod]
    [DataRow("first-only")]
    [DataRow("duplicate")]
    [DataRow("unknown-guid")]
    [DataRow("changed-original-killedBy")]
    public void AFiniteCancellationReportCannotChangeItsOriginalTupleOrItsTwoDeclaredKillers(string mode)
    {
        var fixture = new SourceFixture(cancellation: true);
        switch (mode)
        {
            case "first-only": fixture.Mutant["killedBy"]!.AsArray().RemoveAt(1); break;
            case "duplicate": fixture.Mutant["killedBy"]!.AsArray().Add(fixture.Spec.TargetGuid); break;
            case "unknown-guid": fixture.Mutant["killedBy"]![1] = Guid.NewGuid().ToString(); break;
            case "changed-original-killedBy": fixture.Original = fixture.Original with { Definitions = [fixture.Spec.Original with { KilledBy = [fixture.Spec.TargetGuid] }] }; break;
            default: throw new InvalidOperationException();
        }
        Assert.ThrowsExactly<InvalidDataException>(() => fixture.Parse());
    }

    [TestMethod]
    [DataRow("actual-exit")]
    [DataRow("timeout")]
    [DataRow("cwd")]
    [DataRow("argv")]
    [DataRow("host-version")]
    [DataRow("budget")]
    [DataRow("environment")]
    [DataRow("chronology")]
    public void ARecordedNativeProcessMustKeepItsRealCommandAndOutcome(string mode)
    {
        JsonObject process = CancellationProcess();
        HealthcheckMutationFiniteCancellationEvidence.Process(process, "native-process");
        switch (mode)
        {
            case "actual-exit": process["actualExitCode"] = 2; break;
            case "timeout": process["timedOut"] = true; break;
            case "cwd": process["cwd"] = "/other"; break;
            case "argv": process["argv"]!.AsArray().Add("--ignore-mutations"); break;
            case "host-version": process["argv"]![11] = "10.0.12"; break;
            case "budget": process["outerBudgetSeconds"] = 60000; break;
            case "environment": process["explicitEnvironment"]!["CisMutationTesting"] = "false"; break;
            case "chronology": process["finishedUtc"] = "2026-10-22T11:00:00Z"; break;
            default: throw new InvalidOperationException();
        }
        Assert.ThrowsExactly<InvalidDataException>(() => HealthcheckMutationFiniteCancellationEvidence.Process(process, "native-process"));
    }

    [TestMethod]
    public void KernelCustomLauncherAndMonitorConsoleAreSeparateRoles()
    {
        using var fixture = new KernelFixture();
        JsonObject result = HealthcheckMutationFiniteCancellationEvidence.Kernels(fixture.Traces(), fixture.Logs());
        Assert.HasCount(4, result["actualKernelHostChains"]!.AsArray());
        Assert.AreEqual(3, result["actualKernelHostChains"]!.AsArray().Count(value => (bool?)value!["customLauncherSeparateFromMonitor"] == true));
    }

    [TestMethod]
    public void PostFreshBodiesMustActuallyMatchEachCorrespondingOriginalRole()
    {
        using var fixture = new PostFreshFixture();
        HealthcheckMutationFiniteCancellationEvidence.PostFresh(fixture.Index, fixture.Root, fixture.Originals, fixture.Process);
    }

    [TestMethod]
    [DataRow("missing-role")]
    [DataRow("duplicate-role")]
    [DataRow("unknown-role")]
    [DataRow("origin")]
    [DataRow("copy-origin")]
    [DataRow("before-fresh-finished")]
    [DataRow("wrong-hash")]
    [DataRow("changed-body")]
    [DataRow("length")]
    [DataRow("mvid")]
    public void APostFreshFlagCannotReplaceItsActualBodiesAndChronology(string mode)
    {
        using var fixture = new PostFreshFixture(); JsonObject first = fixture.Index[0]!.AsObject();
        switch (mode)
        {
            case "missing-role": fixture.Index.RemoveAt(1); break;
            case "duplicate-role": fixture.Index[1] = first.DeepClone(); break;
            case "unknown-role": first["relativeRole"] = "other.dll"; break;
            case "origin": first["actualOrigin"] = "/other/CIS.Core.dll"; break;
            case "copy-origin": first["path"] = "/other/CIS.Core.dll"; break;
            case "before-fresh-finished": first["observedUtc"] = "2026-10-22T12:02:34Z"; break;
            case "wrong-hash": first["sha256"] = Hash('a'); break;
            case "changed-body": File.AppendAllText(Path.Combine(fixture.Root, (string)first["relativeRole"]!), "changed"); break;
            case "length": first["bytes"] = 1L; break;
            case "mvid": first["mvid"] = Guid.NewGuid().ToString(); break;
            default: throw new InvalidOperationException();
        }
        Assert.ThrowsExactly<InvalidDataException>(() => HealthcheckMutationFiniteCancellationEvidence.PostFresh(fixture.Index, fixture.Root, fixture.Originals, fixture.Process));
    }

    [TestMethod]
    public void NativeInventoryFingerprintIsOrderIndependentButBindsPhysicalNamesAndBytes()
    {
        using var fixture = new PostFreshFixture(); string[] files = Directory.GetFiles(fixture.Root, "*", SearchOption.AllDirectories);
        string original = HealthcheckMutationFiniteCancellationEvidence.InventoryFingerprint(fixture.Root, files);
        Assert.AreEqual(original, HealthcheckMutationFiniteCancellationEvidence.InventoryFingerprint(fixture.Root, files.Reverse()));
        File.AppendAllText(files[0], "changed");
        Assert.AreNotEqual(original, HealthcheckMutationFiniteCancellationEvidence.InventoryFingerprint(fixture.Root, files));
    }

    [TestMethod]
    [DataRow("host-nonzero")]
    [DataRow("host-killed")]
    [DataRow("missing-callback")]
    [DataRow("custom-error")]
    [DataRow("unknown-launcher")]
    [DataRow("parent-argv")]
    [DataRow("monitor-before-host")]
    [DataRow("missing-host")]
    public void KernelOrParentFaultsCannotBecomeAQualifiedCancellationWitness(string mode)
    {
        using var fixture = new KernelFixture();
        fixture.Change(mode);
        Assert.ThrowsExactly<InvalidDataException>(() => HealthcheckMutationFiniteCancellationEvidence.Kernels(fixture.Traces(), fixture.Logs()));
    }

    [TestMethod]
    public void ReviewedNativeCaseBindsOneOriginalTimeoutWithoutChangingItsIdentityOrRawStatus()
    {
        Finite.Spec spec = HealthcheckMutationFiniteAssertionCatalog.IdleWithdrawal;
        Assert.AreEqual("9db0d80438c109d715d48ee3e1564edc7cd9575cd6329475532b77c3f9f3a233", spec.Original.Identity);
        Assert.AreEqual("Timeout", spec.Original.Status);
        Assert.AreEqual(11504, spec.Original.Start);
        Assert.AreEqual(11547, spec.Original.End);
        Assert.AreEqual("Retired", spec.AssertionMessage.Split("expected: ", StringSplitOptions.None)[1].Split('\n')[0]);
        Finite.Document(Document(), spec);
    }

    [TestMethod]
    [DataRow("schema")]
    [DataRow("proof-kind")]
    [DataRow("windows")]
    [DataRow("original-report")]
    [DataRow("original-identity")]
    [DataRow("approval")]
    [DataRow("unknown-capture")]
    [DataRow("missing-marker")]
    public void ACallerFlagOrSimilarDefinitionCannotAuthorizeTheFiniteProof(string mode)
    {
        JsonObject document = Document();
        switch (mode)
        {
            case "schema": document["schema"] = "other"; break;
            case "proof-kind": document["proofKind"] = "OptimizedJitSourceRemoval"; break;
            case "windows": document["osLane"] = "windows"; break;
            case "original-report": document["original"]!["reportSha256"] = Hash('b'); break;
            case "original-identity": document["original"]!["definitionIdentity"] = Hash('b'); break;
            case "approval": document["accepted"] = true; break;
            case "unknown-capture": document["capture"]!["restored"] = true; break;
            case "missing-marker": document["capture"]!.AsObject().Remove("preFreshMarker"); break;
            default: throw new InvalidOperationException();
        }
        Assert.ThrowsExactly<InvalidDataException>(() => Finite.Document(document, HealthcheckMutationFiniteAssertionCatalog.IdleWithdrawal));
    }

    [TestMethod]
    public void TheRelaxedBudgetAndSelectedCohortAreTheOnlyFiniteConfigurationChanges()
    {
        (JsonObject parent, JsonObject finite) = Configurations();
        string unchanged = parent.ToJsonString();
        Finite.Configuration(parent, finite, "Healthcheck/Test.cs", 0, 5);
        Assert.AreEqual(unchanged, parent.ToJsonString());
        Assert.IsFalse(finite["stryker-config"]!.AsObject().ContainsKey("coverage-analysis"));
    }

    [TestMethod]
    [DataRow("timeout")]
    [DataRow("coverage")]
    [DataRow("range")]
    [DataRow("extra-range")]
    [DataRow("filter")]
    [DataRow("threshold")]
    [DataRow("bail")]
    [DataRow("project")]
    public void FiniteSettingsCannotChangeTheNativeBudgetOrDetectorContractSilently(string mode)
    {
        (JsonObject parent, JsonObject finite) = Configurations(); JsonObject value = finite["stryker-config"]!.AsObject();
        switch (mode)
        {
            case "timeout": value["additional-timeout"] = 10000; break;
            case "coverage": value["coverage-analysis"] = "off"; break;
            case "range": value["mutate"]![0] = "Healthcheck/Test.cs{1..5}"; break;
            case "extra-range": value["mutate"]!.AsArray().Add("Healthcheck/Other.cs"); break;
            case "filter": value["test-case-filter"] = "FullyQualifiedName~Other"; break;
            case "threshold": value["thresholds"]!["break"] = 0; break;
            case "bail": value["disable-bail"] = false; break;
            case "project": value["test-projects"]![0] = "Other.csproj"; break;
            default: throw new InvalidOperationException();
        }
        Assert.ThrowsExactly<InvalidDataException>(() => Finite.Configuration(parent, finite, "Healthcheck/Test.cs", 0, 5));
    }

    [TestMethod]
    public void AFiniteReportJoinsTheOriginalDefinitionWhileOtherFilesStayExplicitPlaceholders()
    {
        var fixture = new SourceFixture(); string raw = fixture.Report.ToJsonString();
        Definition[] definitions = fixture.Parse();
        Assert.HasCount(1, definitions);
        Assert.AreEqual(fixture.Spec.Original.Identity, definitions[0].Identity);
        Assert.AreEqual("Killed", definitions[0].Status);
        Assert.AreEqual("Timeout", fixture.Original.Definitions[0].Status);
        Assert.AreEqual(raw, fixture.Report.ToJsonString());
    }

    [TestMethod]
    [DataRow("wrong-original-report")]
    [DataRow("wrong-finite-report")]
    [DataRow("wrong-original-status")]
    [DataRow("wrong-original-id")]
    [DataRow("different-replacement")]
    [DataRow("timeout")]
    [DataRow("killer")]
    [DataRow("outside-source")]
    [DataRow("new-testable-sibling")]
    public void OneFiniteSourceCannotSupplyAnUnboundOrAdditionalTestableDefinition(string mode)
    {
        var fixture = new SourceFixture();
        switch (mode)
        {
            case "wrong-original-report": fixture.Original = fixture.Original with { ReportSha256 = Hash('c') }; break;
            case "wrong-finite-report": fixture.ReportHash = Hash('c'); break;
            case "wrong-original-status": fixture.Original = fixture.Original with { Definitions = [fixture.Spec.Original with { Status = "Survived" }] }; break;
            case "wrong-original-id": fixture.Original = fixture.Original with { Definitions = [fixture.Spec.Original with { Id = "999" }] }; break;
            case "different-replacement": fixture.Mutant["replacement"] = "{}"; break;
            case "timeout": fixture.Mutant["status"] = "Timeout"; break;
            case "killer": fixture.Mutant["killedBy"]![0] = Guid.NewGuid().ToString(); break;
            case "outside-source": fixture.Report["files"]!["/native/Healthcheck/Pipes/Test.cs"]!["source"] = "pipe"; break;
            case "new-testable-sibling":
                JsonObject sibling = fixture.Mutant.DeepClone().AsObject(); sibling["id"] = "2"; sibling["replacement"] = "{}";
                fixture.Report["files"]!["/native/Healthcheck/Test.cs"]!["mutants"]!.AsArray().Add(sibling); break;
            default: throw new InvalidOperationException();
        }
        Assert.ThrowsExactly<InvalidDataException>(() => fixture.Parse());
    }

    [TestMethod]
    public void DirectReceivingFailureRequiresTheExactActualResultBeyondTheNormalHostObservation()
    {
        using var fixture = new WitnessFixture();
        Finite.NativeWitness(fixture.Audit(), [fixture.Target], fixture.Spec, [fixture.HostPath]);
    }

    [TestMethod]
    [DataRow("native-issue")]
    [DataRow("host-incomplete")]
    [DataRow("semantic-only")]
    [DataRow("wrong-guid")]
    [DataRow("unrelated-assertion")]
    [DataRow("wrong-method")]
    [DataRow("wrong-source")]
    [DataRow("cleanup-origin")]
    [DataRow("wrong-outcome")]
    [DataRow("duplicate-result")]
    [DataRow("unpinned-host")]
    public void AFiniteNativeLabelCannotReplaceItsCausalReceivingAssertion(string mode)
    {
        using var fixture = new WitnessFixture(); NativeAuditResult audit = fixture.Audit(); string[] logs = [fixture.HostPath];
        switch (mode)
        {
            case "native-issue": audit = audit with { Issues = [new("NativeAbortOrCancel", "synthetic abort", [fixture.Target.Id])] }; break;
            case "host-incomplete": audit = audit with { NormalHostCount = 0 }; break;
            case "semantic-only": audit = audit with { AssertionKilledIds = [], ReviewedSemanticKilledIds = [fixture.Target.Id] }; break;
            case "wrong-guid": fixture.Result["TestCase"]!["Id"] = Guid.NewGuid().ToString(); break;
            case "unrelated-assertion": fixture.Result["ErrorMessage"] = "Assertion failed. Unrelated content."; break;
            case "wrong-method": fixture.Result["TestCase"]!["FullyQualifiedName"] = "Other.Method"; break;
            case "wrong-source": fixture.Result["TestCase"]!["CodeFilePath"] = "/other/Test.cs"; break;
            case "cleanup-origin": fixture.Result["ErrorStackTrace"] = ((string)fixture.Result["ErrorStackTrace"]!).Replace("() in", ".DisposeAsync() in", StringComparison.Ordinal); break;
            case "wrong-outcome": fixture.Result["Outcome"] = 1; break;
            case "duplicate-result": fixture.Duplicate = true; break;
            case "unpinned-host": logs = []; break;
            default: throw new InvalidOperationException();
        }
        fixture.Write();
        Assert.ThrowsExactly<InvalidDataException>(() => Finite.NativeWitness(audit, [fixture.Target], fixture.Spec, logs));
    }

    [TestMethod]
    [DataRow("2026-10-22T06:02:40Z")]
    [DataRow("2026-10-22T05:58:20.4235504Z")]
    [DataRow("2026-10-22T10:55:05.9199515+00:00")]
    public void ActualCaptureTimestampShapesRemainExplicitUtcObservations(string timestamp) => Assert.AreEqual(TimeSpan.Zero, Finite.Utc(timestamp).Offset);

    [TestMethod]
    public void ARealManagedCoreIdentityDoesNotAcceptAnArbitraryManagedAssembly()
    {
        Assert.AreNotEqual(Guid.Empty, Finite.CoreIdentity(typeof(HealthcheckService).Assembly.Location));
        Assert.ThrowsExactly<InvalidDataException>(() => Finite.CoreIdentity(typeof(HealthcheckMutationFiniteAssertionTests).Assembly.Location));
    }

    [TestMethod]
    public void MissingCaptureStagesStayVisibleAsDeficitsRatherThanInventedFreshBinaryRestoration()
    {
        using var fixture = new WitnessFixture();
        string backup = Path.Combine(fixture.Root, "backup"), source = Path.Combine(fixture.Root, "source"), test = Path.Combine(fixture.Root, "test");
        foreach (string path in new[] { backup, source, test }) { File.Copy(typeof(HealthcheckService).Assembly.Location, path); }
        string paths = Path.Combine(fixture.Root, "paths.txt"), nativeRoot = HealthcheckMutationFiniteAssertionCatalog.NativeRoot;
        File.WriteAllText(paths, nativeRoot[..^"src/CIS.Core".Length] + "tests/CIS.Core.UnitTests/bin/Release/net10.0/CIS.Core.dll.stryker-unchanged\n");
        JsonObject observation = Finite.Capture(backup, source, test, paths, nativeRoot, fixture.Spec, null, null);
        Assert.HasCount(2, observation["proofDeficits"]!.AsArray());
        Assert.AreEqual(false, (bool?)observation["historicalFreshBinaryIdentityVerified"]);
        Assert.AreEqual(true, (bool?)observation["sourceAndTestCoreEqualActualBackup"]);
        Assert.ThrowsExactly<InvalidDataException>(() => Finite.Capture(backup, backup, test, paths, nativeRoot, fixture.Spec, null, null));
    }

    private static JsonObject CancellationDocument()
    {
        Finite.Spec spec = HealthcheckMutationFiniteAssertionCatalog.CurrentCancellation;
        JsonObject Section(params string[] names) => new(names.Select(name => KeyValuePair.Create<string, JsonNode?>(name, new JsonObject())));
        return new JsonObject
        {
            ["schema"] = HealthcheckMutationFiniteCancellationEvidence.Schema,
            ["proofKind"] = Finite.ProofKind,
            ["osLane"] = "ubuntu",
            ["original"] = new JsonObject { ["reportSha256"] = spec.OriginalReportSha256, ["definitionIdentity"] = spec.Original.Identity },
            ["finite"] = new JsonObject
            {
                ["report"] = new JsonObject(),
                ["configuration"] = new JsonObject(),
                ["nativeSourceRoot"] = HealthcheckMutationFiniteAssertionCatalog.CancellationNativeRoot,
                ["nativeProcess"] = new JsonObject(),
                ["kernelTraces"] = new JsonArray()
            },
            ["sources"] = Section("beforeRoot", "beforeManifest", "afterRoot", "afterManifest"),
            ["logsManifest"] = new JsonObject(),
            ["toolchain"] = Section("strykerCore", "strykerCli", "objectModel"),
            ["baselines"] = Section("initialTrx", "initialProcess", "freshTrx", "freshProcess", "freshBuildProcess"),
            ["capture"] = Section("receipt", "marker", "bodyRoot", "controllerSource", "controllerAssembly", "backupAbsence", "sourceAfterNative", "sourceAfterFresh")
        };
    }

    private static JsonObject CancellationProcess()
    {
        string task = HealthcheckMutationFiniteAssertionCatalog.TaskRoot, run = HealthcheckMutationFiniteAssertionCatalog.CancellationRoot;
        return new JsonObject
        {
            ["name"] = "native-process",
            ["actualExitCode"] = 0,
            ["timedOut"] = false,
            ["startedUtc"] = "2026-10-22T11:56:24Z",
            ["finishedUtc"] = "2026-10-22T12:01:24Z",
            ["outerBudgetSeconds"] = 1800,
            ["executable"] = new JsonObject { ["path"] = "/usr/bin/strace" },
            ["cwd"] = HealthcheckMutationFiniteAssertionCatalog.CancellationNativeRoot,
            ["explicitEnvironment"] = new JsonObject
            {
                ["CisMutationTesting"] = "true",
                ["UseSharedCompilation"] = "false",
                ["MSBUILDDISABLENODEREUSE"] = "1",
                ["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] = "1",
                ["DOTNET_ROOT"] = task + "/dotnet"
            },
            ["argv"] = new JsonArray("-ff", "-ttt", "-s", "4096", "-e", "trace=process", "-o", run + "/actual-process", task + "/dotnet/dotnet", "exec",
                "--fx-version", "8.0.31", "--roll-forward", "Disable", task + "/release-next-stryker-4.16.0/Stryker.CLI.dll", "--config-file", run + "/unit.json",
                "--output", run + "/native", "--log-to-file", "--diag")
        };
    }

    private sealed class KernelFixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "cis-finite-kernel-receiving-" + Guid.NewGuid().ToString("N"));
        private readonly string runner, discoverer;
        internal KernelFixture()
        {
            Directory.CreateDirectory(root); runner = Path.Combine(root, "Runner 1-log.txt"); discoverer = Path.Combine(root, "SpecificTestDiscoverer-log.txt");
            string task = HealthcheckMutationFiniteAssertionCatalog.TaskRoot;
            File.WriteAllText(Trace(961017), "1 execve(\"dotnet\", [\"dotnet\", \"--fx-version\", \"8.0.31\", \"" + task + "/release-next-stryker-4.16.0/Stryker.CLI.dll\"], []) = 0\n"
                + string.Join('\n', new[] { 961585, 961773, 961797 }.Select(pid => "2 vfork() = " + pid)) + "\n10 exit_group(0) = ?\n10 +++ exited with 0 +++\n");
            File.WriteAllText(Trace(961513), "2 vfork() = 961562\n10 exit_group(0) = ?\n10 +++ exited with 0 +++\n");
            File.WriteAllText(Trace(961515), "10 exit_group(1) = ?\n10 +++ exited with 1 +++\n");
            foreach (int pid in new[] { 961562, 961585, 961773, 961797 })
            {
                int monitor = pid == 961562 ? 961513 : 961515;
                string controller = monitor == 961513 ? discoverer : runner;
                File.WriteAllText(Host(pid), "TpTrace Verbose: 0 : " + pid + ", synthetic\nArguments: [--parentprocessid, " + monitor + "]\nRuntime location: " + task + "/dotnet/shared/Microsoft.NETCore.App/10.0.12\n");
                File.WriteAllText(Trace(pid), "3 execve(\"dotnet\", [\"dotnet\", \"testhost.dll\", \"--parentprocessid\", \"" + monitor + "\"], []) = 0\n9 exit_group(0) = ?\n9 +++ exited with 0 +++\n");
                if (monitor == 961515)
                {
                    var callback = new JsonObject { ["Payload"] = new JsonObject { ["HostProcessId"] = pid, ["ErrorMessage"] = null } };
                    File.AppendAllText(controller, "synthetic (TestExecution.CustomTestHostLaunchCallback) -> " + callback.ToJsonString() + "\n");
                }
                File.AppendAllText(controller, "synthetic Testhost processId: " + pid + " exited with exitcode: 0 error: ''\n");
            }
        }
        private string Trace(int pid) => Path.Combine(root, "actual-process." + pid);
        private string Host(int pid) => Path.Combine(root, (pid == 961562 ? "SpecificTestDiscoverer" : "Runner 1") + "-log.host.synthetic-" + pid + ".txt");
        internal string[] Traces() => Directory.GetFiles(root, "actual-process.*");
        internal string[] Logs() => Directory.GetFiles(root, "*.txt");
        internal void Change(string mode)
        {
            string host = Trace(961797), text = File.ReadAllText(host);
            switch (mode)
            {
                case "host-nonzero": File.WriteAllText(host, text.Replace("exited with 0", "exited with 1", StringComparison.Ordinal)); break;
                case "host-killed": File.WriteAllText(host, text.Replace("+++ exited with 0 +++", "+++ killed by SIGKILL +++", StringComparison.Ordinal)); break;
                case "missing-callback": File.WriteAllText(runner, File.ReadAllText(runner).Replace("Testhost processId: 961797", "Testhost processId: 1", StringComparison.Ordinal)); break;
                case "custom-error": File.WriteAllText(runner, File.ReadAllText(runner).Replace("\"ErrorMessage\":null", "\"ErrorMessage\":\"launch failed\"", StringComparison.Ordinal)); break;
                case "unknown-launcher": File.Move(Trace(961017), Trace(1)); break;
                case "parent-argv": File.WriteAllText(host, text.Replace("\"961515\"", "\"1\"", StringComparison.Ordinal)); break;
                case "monitor-before-host": File.WriteAllText(Trace(961515), "8 exit_group(1) = ?\n8 +++ exited with 1 +++\n"); break;
                case "missing-host": File.Delete(Host(961797)); break;
                default: throw new InvalidOperationException();
            }
        }
        public void Dispose() => Directory.Delete(root, true);
    }

    private sealed class PortablePdbFixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "cis-portable-pdb-receiving-" + Guid.NewGuid().ToString("N"));
        internal string Assembly => Path.Combine(root, "CIS.Core.dll");
        internal string Symbols => Path.Combine(root, "CIS.Core.pdb");
        internal PortablePdbFixture()
        {
            Directory.CreateDirectory(root); string actual = typeof(HealthcheckService).Assembly.Location;
            File.Copy(actual, Assembly); File.Copy(Path.ChangeExtension(actual, ".pdb"), Symbols);
        }
        internal void Change(string mode)
        {
            byte[] image = File.ReadAllBytes(Assembly), symbols = File.ReadAllBytes(Symbols);
            using var peStream = new MemoryStream(image, writable: false); using var pe = new PEReader(peStream);
            DebugDirectoryEntry[] entries = pe.ReadDebugDirectory().ToArray();
            DebugDirectoryEntry codeView = entries.Single(entry => entry.Type == DebugDirectoryEntryType.CodeView), checksum = entries.Single(entry => entry.Type == DebugDirectoryEntryType.PdbChecksum);
            using var pdbStream = new MemoryStream(symbols, writable: false); using MetadataReaderProvider provider = MetadataReaderProvider.FromPortablePdbStream(pdbStream);
            int idOffset = provider.GetMetadataReader().DebugMetadataHeader!.IdStartOffset;
            int rva = pe.PEHeaders.PEHeader!.DebugTableDirectory.RelativeVirtualAddress;
            SectionHeader section = pe.PEHeaders.SectionHeaders.Single(value => rva >= value.VirtualAddress && rva < value.VirtualAddress + value.SizeOfRawData);
            int directory = section.PointerToRawData + rva - section.VirtualAddress;
            int EntryOffset(DebugDirectoryEntry entry) => directory + Array.IndexOf(entries, entry) * 28;
            switch (mode)
            {
                case "pdb-id": symbols[idOffset] ^= 1; break;
                case "pdb-payload": symbols[^1] ^= 1; break;
                case "pe-codeview": image[codeView.DataPointer + 4] ^= 1; break;
                case "pe-checksum": image[checksum.DataPointer + 7] ^= 1; break;
                case "missing-checksum": BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(EntryOffset(checksum) + 12, 4), 0); break;
                case "duplicate-checksum":
                    DebugDirectoryEntry other = entries.First(entry => entry.Type is not (DebugDirectoryEntryType.CodeView or DebugDirectoryEntryType.PdbChecksum));
                    BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(EntryOffset(other) + 12, 4), (int)DebugDirectoryEntryType.PdbChecksum); break;
                case "unsupported-algorithm": image[checksum.DataPointer] = (byte)'X'; break;
                case "missing-codeview": BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(EntryOffset(codeView) + 12, 4), 0); break;
                case "age": BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(codeView.DataPointer + 20, 4), 2); break;
                case "malformed-pdb": symbols[0] ^= 1; break;
                default: throw new InvalidOperationException();
            }
            File.WriteAllBytes(Assembly, image); File.WriteAllBytes(Symbols, symbols);
        }
        public void Dispose() => Directory.Delete(root, true);
    }

    private sealed class PostFreshFixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "cis-finite-postfresh-receiving-" + Guid.NewGuid().ToString("N"));
        internal JsonArray Index { get; } = new();
        internal JsonObject[] Originals { get; }
        internal JsonObject Process { get; } = new() { ["finishedUtc"] = "2026-10-22T12:02:35Z" };
        internal PostFreshFixture()
        {
            var originals = new List<JsonObject>();
            foreach (string role in HealthcheckMutationFiniteCancellationEvidence.BodyRoles.Where(role => !role.EndsWith(".stryker-unchanged", StringComparison.Ordinal)))
            {
                string path = Path.Combine(Root, role); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                // Explicit parser fixtures: real managed Core bytes only for
                // MVID validation, synthetic inert bytes for other file roles.
                if (role.EndsWith("/CIS.Core.dll", StringComparison.Ordinal)) { File.Copy(typeof(HealthcheckService).Assembly.Location, path); }
                else { File.WriteAllText(path, "synthetic parser role: " + role); }
                var row = new JsonObject
                {
                    ["relativeRole"] = role,
                    ["actualOrigin"] = HealthcheckMutationFiniteAssertionCatalog.CancellationRoot + "/source/" + role,
                    ["path"] = HealthcheckMutationFiniteAssertionCatalog.CancellationRoot + "/fresh-after/bodies/" + role,
                    ["sha256"] = Finite.Hash(path),
                    ["bytes"] = new FileInfo(path).Length,
                    ["observedUtc"] = "2026-10-22T12:02:36Z"
                };
                if (role.EndsWith("/CIS.Core.dll", StringComparison.Ordinal)) { row["mvid"] = Finite.CoreIdentity(path).ToString(); }
                Index.Add(row); originals.Add(row.DeepClone().AsObject());
            }
            Originals = originals.ToArray();
        }
        public void Dispose() => Directory.Delete(Root, true);
    }

    private static JsonObject Document()
    {
        Finite.Spec spec = HealthcheckMutationFiniteAssertionCatalog.IdleWithdrawal;
        JsonObject Section(params string[] names) => new(names.Select(name => KeyValuePair.Create<string, JsonNode?>(name, new JsonObject())));
        return new JsonObject
        {
            ["schema"] = Finite.Schema,
            ["proofKind"] = Finite.ProofKind,
            ["osLane"] = "ubuntu",
            ["original"] = new JsonObject { ["reportSha256"] = spec.OriginalReportSha256, ["definitionIdentity"] = spec.Original.Identity },
            ["finite"] = Section("report", "configuration", "nativeSourceRoot", "nativeExit", "invocation", "processTrace"),
            ["sources"] = Section("beforeRoot", "beforeManifest", "afterRoot", "afterManifest"),
            ["toolchain"] = Section("strykerCore", "strykerCli", "objectModel"),
            ["baselines"] = Section("initialTrx", "initialExit", "freshTrx", "freshExit"),
            ["capture"] = Section("backup", "backupPaths", "copyInventory", "preFreshMarker", "sourceCore", "testCore", "unitAssembly", "unitDeps", "unitRuntimeConfig"),
            ["logsManifest"] = new JsonObject()
        };
    }
    private static (JsonObject Parent, JsonObject Finite) Configurations()
    {
        var parent = new JsonObject
        {
            ["stryker-config"] = new JsonObject
            {
                ["additional-timeout"] = 10000,
                ["mutate"] = new JsonArray("Healthcheck/Test.cs"),
                ["test-case-filter"] = "FullHealthcheck",
                ["test-projects"] = new JsonArray("../../tests/CIS.Core.UnitTests/CIS.Core.UnitTests.csproj"),
                ["mutation-level"] = "Complete",
                ["configuration"] = "Release",
                ["test-runner"] = "vstest",
                ["target-framework"] = "net10.0",
                ["concurrency"] = 2,
                ["disable-bail"] = true,
                ["thresholds"] = new JsonObject { ["high"] = 100, ["low"] = 90, ["break"] = 90 }
            }
        };
        JsonObject finite = parent.DeepClone().AsObject(); finite["stryker-config"]!["additional-timeout"] = 60000;
        finite["stryker-config"]!["mutate"] = new JsonArray("Healthcheck/Test.cs{0..5}"); finite["stryker-config"]!["test-case-filter"] = HealthcheckMutationFiniteAssertionCatalog.Filter;
        return (parent, finite);
    }
    private static string Hash(char value) => new(value, 64);
    private static string Sha(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    // These are explicitly synthetic receiving fixtures. No fabricated native
    // normality field is used by the maintained reader as evidence authority.
    private sealed class SourceFixture
    {
        internal Source[] Sources { get; } = [new("Healthcheck/Test.cs", Sha("guard"), "unit-runtime", "guard"), new("Healthcheck/Pipes/Test.cs", Sha("pipe"), "pipes", "pipe"), new("Healthcheck/AspNetCore/Test.cs", Sha("http"), "aspnet", "http")];
        internal Finite.Spec Spec { get; }
        internal Partition Original { get; set; }
        internal string ReportHash { get; set; } = Hash('b');
        internal JsonObject Report { get; }
        internal JsonObject Mutant => Report["files"]!["/native/Healthcheck/Test.cs"]!["mutants"]![0]!.AsObject();
        internal SourceFixture(bool cancellation = false)
        {
            Finite.Spec selected = cancellation ? HealthcheckMutationFiniteAssertionCatalog.CurrentCancellation : HealthcheckMutationFiniteAssertionCatalog.IdleWithdrawal;
            var original = new Definition("1", Sources[0].Path, Sources[0].Sha256, 0, 5, "Statement mutation", "guard", ";", selected.Original.Status, selected.Original.KilledBy);
            Spec = selected with { Original = original, OriginalReportSha256 = Hash('a'), FiniteReportSha256 = ReportHash };
            Original = new("unit-runtime", Hash('a'), [Sources[0]], [original], [], []);
            Report = new JsonObject
            {
                ["schemaVersion"] = "2",
                ["files"] = new JsonObject
                {
                    ["/native/Healthcheck/Test.cs"] = new JsonObject
                    {
                        ["source"] = "guard",
                        ["mutants"] = new JsonArray(new JsonObject
                        {
                            ["id"] = "1",
                            ["status"] = "Killed",
                            ["mutatorName"] = "Statement mutation",
                            ["replacement"] = ";",
                            ["killedBy"] = new JsonArray(Finite.TargetGuids(Spec).Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()),
                            ["location"] = new JsonObject { ["start"] = new JsonObject { ["line"] = 1, ["column"] = 1 }, ["end"] = new JsonObject { ["line"] = 1, ["column"] = 6 } }
                        })
                    },
                    ["/native/Healthcheck/Pipes/Test.cs"] = new JsonObject { ["source"] = "File ignored by mutate filter", ["mutants"] = new JsonArray() },
                    ["/native/Healthcheck/AspNetCore/Test.cs"] = new JsonObject { ["source"] = "File ignored by mutate filter", ["mutants"] = new JsonArray() }
                }
            };
        }
        internal Definition[] Parse() => Finite.Definitions(Report, ReportHash, "/native", Sources, Original, Spec);
    }
    private sealed class WitnessFixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "cis-finite-receiving-" + Guid.NewGuid().ToString("N"));
        internal string HostPath => Path.Combine(Root, "synthetic-host.txt");
        internal Finite.Spec Spec { get; }
        internal Definition Target => Spec.Original with { Status = "Killed", KilledBy = Finite.TargetGuids(Spec) };
        internal JsonObject Result { get; }
        internal JsonObject? SecondResult { get; set; }
        internal bool Duplicate { get; set; }
        internal WitnessFixture(bool cancellation = false)
        {
            Spec = cancellation ? HealthcheckMutationFiniteAssertionCatalog.CurrentCancellation : HealthcheckMutationFiniteAssertionCatalog.IdleWithdrawal;
            Directory.CreateDirectory(Root);
            string file = "/synthetic/" + Spec.HarnessPath;
            Result = new JsonObject
            {
                ["TestCase"] = new JsonObject { ["Id"] = Spec.TargetGuid, ["FullyQualifiedName"] = Spec.TargetMethod, ["CodeFilePath"] = file },
                ["Outcome"] = 2,
                ["ErrorMessage"] = Spec.AssertionMessage,
                ["ErrorStackTrace"] = "at " + Spec.TargetMethod + "() in " + file + ":line 27\nat Microsoft.VisualStudio.TestPlatform.MSTest.TestAdapter.Execution.TestMethodInfo.ExecuteInternalAsync(Object[] arguments)"
            };
            if (cancellation) { SecondResult = Result.DeepClone().AsObject(); SecondResult["TestCase"]!["Id"] = Spec.AdditionalTargetGuids![0]; }
            Write();
        }
        internal void Write()
        {
            var results = new JsonArray(Result.DeepClone()); if (SecondResult is not null) { results.Add(SecondResult.DeepClone()); }
            if (Duplicate) { results.Add(Result.DeepClone()); }
            var message = new JsonObject { ["MessageType"] = "TestExecution.StatsChange", ["Payload"] = new JsonObject { ["NewTestResults"] = results } };
            File.WriteAllText(HostPath, "synthetic receiving data: TestRequestHandler.SendData: sending data from testhost: " + message.ToJsonString());
        }
        internal NativeAuditResult Audit() => new([Target.Id], [], 1, 1, new JsonObject
        {
            ["outcomeMetadata"] = new JsonObject { ["failed"] = 2 },
            ["witnessSamples"] = new JsonArray(Finite.TargetGuids(Spec).Select(guid => (JsonNode?)new JsonObject
            { ["mutantId"] = Target.Id, ["guid"] = guid, ["method"] = Spec.TargetMethod, ["hostPath"] = HostPath, ["actualOsExitCode"] = 0 }).ToArray())
        });
        public void Dispose() => Directory.Delete(Root, true);
    }
}
