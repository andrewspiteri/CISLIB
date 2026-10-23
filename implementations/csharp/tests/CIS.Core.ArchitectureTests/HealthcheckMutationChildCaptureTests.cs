using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.Verification.HealthcheckMutationChildren;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using C = CIS.Verification.HealthcheckMutationChildren.HealthcheckMutationChildCapture;
using P = CIS.Verification.HealthcheckMutationChildren.HealthcheckMutationChildProtocol;


namespace CIS.Tests.ArchitectureTests;

// These are journal/parser receiving controls, never synthetic native launches.
[TestClass]
[DoNotParallelize]
public sealed class HealthcheckMutationChildCaptureTests
{
    [TestMethod]
    public void BoundIntentIsDurableBeforeAnyLaunchDirectoryOrStageIsCreated()
    {
        using var f = new Fixture();
        Assert.IsTrue(File.Exists(f.IntentPath));
        Assert.IsFalse(Directory.Exists(f.LaunchDirectory));
        Assert.IsFalse(Directory.Exists(f.StageDirectory));
        C.Intent actual = P.Read<C.Intent>(f.IntentPath);
        Assert.AreEqual(f.Intent, actual);
        Assert.AreEqual(P.Hash(f.SessionPath), actual.SessionSha256);
        Assert.AreEqual(f.Intent.ParentCoreSha256, actual.ParentCoreSha256);
    }

    [TestMethod]
    public void ExactFivePhaseReceivingChainBindsAllRoleReceiptsAndCompletes()
    {
        using var f = new Fixture(); f.Complete();
        C.AuditResult result = f.Audit();
        Assert.HasCount(0, result.Failures);
        Assert.HasCount(1, result.Rows);
        Assert.IsTrue(result.Rows[0].NormalComplete);
        Assert.AreEqual(f.Intent.ParentPid, result.Rows[0].ParentPid);
        Assert.AreEqual(-1, result.Rows[0].ActiveMutant);
        Assert.IsFalse(result.HistoricalLegacyCaptureClaim);
    }

    [TestMethod]
    public void FailedPreparationRetainsBoundAttributionWithoutInventingAChild()
    {
        using var f = new Fixture();
        f.Journal.Append("preparation-failed", failure: "receiving control stage-copy failure");
        C.AuditResult result = f.Audit();
        Assert.HasCount(1, result.Failures);
        Assert.HasCount(1, result.Rows);
        Assert.IsFalse(result.Rows[0].NormalComplete);
        Assert.AreEqual(f.Intent.ParentPid, result.Rows[0].ParentPid);
        Assert.AreEqual(f.Intent.ParentCoreSha256, result.Rows[0].ParentCoreSha256);
        StringAssert.Contains(result.Failures[0], f.Id);
    }

    [TestMethod]
    public void ANewRecorderBodyCannotDowngradeByClearingSessionAndBothJournalDirectories()
    {
        using var f = new Fixture(); f.Complete();
        string actualBody = typeof(HealthcheckMutationChildSupport).Assembly.Location;
        Assert.AreEqual(1, C.RecorderVersion(actualBody, P.Hash(actualBody)));
        f.Session = f.Session with { BoundIntentVersion = 0 }; f.SaveSession();
        P.Launch launch = P.Read<P.Launch>(f.LaunchPath) with { SessionSha256 = P.Hash(f.SessionPath) };
        f.Rewrite(f.LaunchPath, launch);
        Directory.Delete(Path.Combine(f.Root, "intents"), recursive: true);
        Directory.Delete(Path.Combine(f.Root, "capture-phases"), recursive: true);
        InvalidOperationException failure = Assert.ThrowsExactly<InvalidOperationException>(() => f.Audit());
        StringAssert.Contains(failure.Message, "recorder body forbids");
    }

    [TestMethod]
    [DataRow("session-only")]
    [DataRow("journal-only")]
    [DataRow("unknown-version")]
    public void OtherCaptureDowngradesOrUnknownVersionsAlsoFailClosed(string mode)
    {
        using var f = new Fixture(); f.Complete();
        if (mode == "journal-only") { Directory.Delete(Path.Combine(f.Root, "intents"), recursive: true); }
        else { f.Session = f.Session with { BoundIntentVersion = mode == "session-only" ? 0 : 2 }; f.SaveSession(); }
        Assert.ThrowsExactly<InvalidOperationException>(() => f.Audit());
    }

    [TestMethod]
    [DataRow("missing-intent")]
    [DataRow("orphan-launch")]
    [DataRow("orphan-phases")]
    [DataRow("empty-launch")]
    [DataRow("missing-phase")]
    [DataRow("extra-phase")]
    [DataRow("selector")]
    [DataRow("owner")]
    [DataRow("parent-pid")]
    [DataRow("core")]
    [DataRow("target-body")]
    [DataRow("stage")]
    [DataRow("launch-hash")]
    [DataRow("previous-hash")]
    [DataRow("child-pid")]
    [DataRow("parent-exit")]
    [DataRow("forced-parent")]
    [DataRow("timed-out-parent")]
    [DataRow("parent-recorder-body")]
    public void MissingOrTamperedReceivingEvidenceCannotCompleteTheCapture(string mode)
    {
        using var f = new Fixture(); f.Complete();
        switch (mode)
        {
            case "missing-intent": File.Delete(f.IntentPath); break;
            case "orphan-launch": Directory.CreateDirectory(Path.Combine(f.Root, "launches", Guid.NewGuid().ToString("N"))); break;
            case "orphan-phases": Directory.CreateDirectory(Path.Combine(f.Root, "capture-phases", Guid.NewGuid().ToString("N"))); break;
            case "empty-launch": File.Delete(f.LaunchPath); break;
            case "missing-phase": File.Delete(f.Phase(5, "lifecycle-completed")); break;
            case "extra-phase": File.Copy(f.Phase(5, "lifecycle-completed"), f.Phase(6, "lifecycle-completed")); break;
            case "selector": f.Edit(f.IntentPath, node => node["ActiveMutant"] = 7); break;
            case "owner": f.Edit(f.LaunchPath, node => node["StageOwnerId"] = Guid.NewGuid().ToString("N")); break;
            case "parent-pid": f.Edit(f.LaunchPath, node => node["ParentPid"] = f.Intent.ParentPid + 1); break;
            case "core": f.Edit(f.LaunchPath, node => node["ParentCoreSha256"] = new string('b', 64)); break;
            case "target-body": f.Edit(f.LaunchPath, node => node["Stage"]!["OriginalFiles"]![0]!["Sha256"] = new string('b', 64)); break;
            case "stage": f.Edit(f.Phase(1, "stage-ready"), node => node["EvidenceSha256"] = new string('b', 64)); break;
            case "launch-hash": f.Edit(f.Phase(2, "launch-ready"), node => node["EvidenceSha256"] = new string('b', 64)); break;
            case "previous-hash": f.Edit(f.Phase(2, "launch-ready"), node => node["PreviousSha256"] = new string('b', 64)); break;
            case "child-pid": f.Edit(f.ParentPath, node => node["Pid"] = 7654321); break;
            case "parent-exit": f.Edit(f.ParentPath, node => node["ExitCode"] = 2); break;
            case "forced-parent": f.Edit(f.ParentPath, node => node["ForcedStop"] = true); break;
            case "timed-out-parent": f.Edit(f.ParentPath, node => node["TimedOut"] = true); break;
            case "parent-recorder-body": f.Edit(f.LaunchPath, node => node["ParentAssemblySha256"] = new string('b', 64)); break;
            default: throw new InvalidOperationException("Unknown capture receiving control.");
        }
        bool rejected = false;
        try { rejected = f.Audit().Failures.Length > 0; }
        catch (InvalidOperationException) { rejected = true; }
        Assert.IsTrue(rejected, mode);
    }

    [TestMethod]
    [DataRow("preparation-failed")]
    [DataRow("observer-failed")]
    [DataRow("forced-stop")]
    [DataRow("timeout")]
    public void AnAttributedFailurePhaseNeverBecomesNormalLifecycleEvidence(string phase)
    {
        using var f = new Fixture(); f.Complete();
        f.Journal.Append(phase, failure: "receiving failure marker");
        Assert.IsTrue(f.Audit().Failures.Length > 0);
    }

    [TestMethod]
    public void IntentAndPhaseWritesAreExclusiveAndNeverReplaceExistingEvidence()
    {
        using var f = new Fixture();
        string before = P.Hash(f.IntentPath);
        Assert.ThrowsExactly<IOException>(() => C.BeginBoundInput(f.Session, f.Intent));
        Assert.AreEqual(before, P.Hash(f.IntentPath));
        f.Complete();
        Assert.ThrowsExactly<InvalidOperationException>(() => C.Open(f.Session, P.Read<P.Launch>(f.LaunchPath)));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public void ExactRecorderMetadataDistinguishesLegacyFromCurrentAndUnknownBodies(int version)
    {
        using var f = new Fixture();
        string body = Path.Combine(f.Root, "synthetic-recorder-" + version + ".dll");
        WriteMetadataControl(body, version == 0 ? null : version);
        if (version == 2) { Assert.ThrowsExactly<InvalidOperationException>(() => C.RecorderVersion(body, P.Hash(body))); }
        else { Assert.AreEqual(version, C.RecorderVersion(body, P.Hash(body))); }
        Assert.ThrowsExactly<InvalidOperationException>(() => C.RecorderVersion(body, new string('b', 64)));
    }

    // Minimal synthetic PE parser fixtures only; no subject or child executes.
    private static void WriteMetadataControl(string path, int? version)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString("capture-control.dll"), metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        metadata.AddAssembly(metadata.GetOrAddString("Capture.MetadataControl"), new Version(1, 0, 0, 0), default, default, 0, AssemblyHashAlgorithm.None);
        metadata.AddTypeDefinition(TypeAttributes.NotPublic, default, metadata.GetOrAddString("<Module>"), default,
            MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Sealed,
            metadata.GetOrAddString("CIS.Verification.HealthcheckMutationChildren"), metadata.GetOrAddString("HealthcheckMutationChildSupport"), default,
            MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        if (version is int value)
        {
            FieldDefinitionHandle field = metadata.AddFieldDefinition(FieldAttributes.Assembly | FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.HasDefault,
                metadata.GetOrAddString("RequiredBoundIntentVersion"), metadata.GetOrAddBlob(new byte[] { 0x06, 0x08 }));
            metadata.AddConstant(field, value);
        }
        var builder = new ManagedPEBuilder(PEHeaderBuilder.CreateLibraryHeader(), new MetadataRootBuilder(metadata), new BlobBuilder(), flags: CorFlags.ILOnly);
        var blob = new BlobBuilder(); builder.Serialize(blob);
        using FileStream stream = new(path, FileMode.CreateNew); blob.WriteContentTo(stream);
    }

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "hc-bound-intent-" + Guid.NewGuid().ToString("N"));
        internal string Id => Intent.Id;
        internal string SessionPath => Path.Combine(Root, "session.json");
        internal string IntentPath => Path.Combine(Root, "intents", Id + ".json");
        internal string LaunchDirectory => Path.Combine(Root, "launches", Id);
        internal string LaunchPath => Path.Combine(LaunchDirectory, "launch.json");
        internal string ParentPath => Path.Combine(LaunchDirectory, "parent-exit.json");
        internal string StageDirectory => Path.Combine(Root, "stages", "receiving-only");
        internal P.Session Session { get; set; }
        internal C.Intent Intent { get; }
        internal C.Journal Journal { get; }

        internal Fixture()
        {
            Directory.CreateDirectory(Root); Directory.CreateDirectory(Path.Combine(Root, "launches"));
            C.PrepareRoots(Root);
            string body = typeof(HealthcheckMutationChildSupport).Assembly.Location;
            string core = typeof(object).Assembly.Location;
            Session = new(P.Schema, Root, Root, body, P.Hash(body), body, P.Hash(body), new string('a', 64),
                Environment.ProcessPath!, P.Hash(Environment.ProcessPath!), core, P.Hash(core), [], DateTimeOffset.UtcNow.AddSeconds(-1), true, true, BoundIntentVersion: 1);
            SaveSession();
            string target = Path.Combine(Root, "CisHealthProbe.dll"); File.Copy(body, target);
            Intent = new(C.CaptureSchema, Guid.NewGuid().ToString("N"), SessionPath, P.Hash(SessionPath), DateTimeOffset.UtcNow,
                Environment.ProcessId, Guid.NewGuid().ToString("N"), body, P.Hash(body), core, P.Hash(core), P.Mvid(core),
                null, -1, null, null, false, target, P.Hash(target));
            Journal = C.BeginBoundInput(Session, Intent);
        }

        internal void Complete()
        {
            Directory.CreateDirectory(LaunchDirectory); Directory.CreateDirectory(StageDirectory);
            var stage = new P.Stage(StageDirectory, Root, "CisHealthProbe.dll", "CisHealthProbe", Intent.ParentCoreSha256,
                Intent.ParentCoreMvid, null, [new("CisHealthProbe.dll", Intent.TargetAssemblySha256)], [new("CisHealthProbe.dll", Intent.TargetAssemblySha256)]);
            Journal.Append("stage-ready", StageDirectory, P.Digest(JsonSerializer.Serialize(stage, P.JsonOptions)));
            var launch = new P.Launch(P.Schema, Id, SessionPath, Intent.SessionSha256, DateTimeOffset.UtcNow,
                Intent.ParentPid, Intent.StageOwnerId, Intent.ParentAssemblyPath, Intent.ParentAssemblySha256,
                Intent.ParentCorePath, Intent.ParentCoreSha256, Intent.ParentCoreMvid, null, -1, null, null, false, stage);
            Rewrite(LaunchPath, launch); Journal.Append("launch-ready", LaunchPath, P.Hash(LaunchPath));
            DateTimeOffset started = DateTimeOffset.UtcNow;
            Journal.Append("process-started", childPid: 7654320, childStartedUtc: started, observedUtc: started);
            DateTimeOffset exited = DateTimeOffset.UtcNow;
            var parent = new P.ParentExitReceipt(P.Schema, Id, P.Hash(LaunchPath), Intent.ParentPid, 7654320,
                Session.DotnetPath, StageDirectory, [], started, started, exited, DateTimeOffset.UtcNow, 0, false, false);
            Rewrite(ParentPath, parent);
            Journal.Append("parent-exit", ParentPath, P.Hash(ParentPath), childPid: parent.Pid, exitCode: 0);
            Journal.Append("lifecycle-completed", ParentPath, P.Hash(ParentPath), childPid: parent.Pid, exitCode: 0);
        }

        internal C.AuditResult Audit() => C.Audit(Session, SessionPath, Directory.GetDirectories(Path.Combine(Root, "launches")));
        internal string Phase(int sequence, string phase) => Path.Combine(Root, "capture-phases", Id, sequence.ToString("D3", System.Globalization.CultureInfo.InvariantCulture) + "-" + phase + ".json");
        internal void SaveSession() => Rewrite(SessionPath, Session);
        internal void Rewrite<T>(string path, T value) => File.WriteAllText(path, JsonSerializer.Serialize(value, P.JsonOptions), new UTF8Encoding(false, true));
        internal void Edit(string path, Action<JsonObject> change)
        { JsonObject value = JsonNode.Parse(File.ReadAllBytes(path))!.AsObject(); change(value); Rewrite(path, value); }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
