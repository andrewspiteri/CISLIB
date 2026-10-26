using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using static CIS.Verification.HealthcheckMutationChildren.HealthcheckMutationChildProtocol;

namespace CIS.Verification.HealthcheckMutationChildren;

// Additive capture protocol. Existing v4 child receipts remain unchanged. A
// missing/failed intent can explain a failure, never make that launch qualify.
internal static class HealthcheckMutationChildCapture
{
    internal const int Version = 1;
    internal const string CaptureSchema = "cis.healthcheck.bound-child-intent/v1-development";
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly string[] NormalOrder = ["stage-ready", "launch-ready", "process-started", "parent-exit", "lifecycle-completed"];
    private static readonly string[] FailureKinds = ["preparation-failed", "observer-failed", "forced-stop", "timeout"];

    internal sealed record Intent(string Schema, string Id, string SessionPath, string SessionSha256,
        DateTimeOffset CreatedUtc, int ParentPid, string StageOwnerId, string ParentAssemblyPath,
        string ParentAssemblySha256, string ParentCorePath, string ParentCoreSha256, Guid ParentCoreMvid,
        string? ControllerType, int ActiveMutant, string? OriginalSelectorName, string? OriginalSelectorValue,
        bool Instrumented, string TargetAssemblyPath, string TargetAssemblySha256);
    internal sealed record Transition(string Schema, string Id, string IntentSha256, int Sequence,
        string PreviousSha256, string Phase, DateTimeOffset ObservedUtc, int ParentPid, string StageOwnerId,
        string? EvidencePath = null, string? EvidenceSha256 = null, int? ChildPid = null,
        DateTimeOffset? ChildStartedUtc = null, int? ExitCode = null, string? Failure = null);
    internal sealed record AuditRow(string Id, int? ParentPid, int? ActiveMutant, string? ParentCoreSha256,
        bool NormalComplete, string? Failure);
    internal sealed record AuditResult(int Version, int IntentCount, int LaunchDirectoryCount, AuditRow[] Rows,
        string[] Failures, bool HistoricalLegacyCaptureClaim);

    internal static void PrepareRoots(string output)
    {
        Require(Directory.Exists(output), "A prepared capture output root is required.");
        foreach (string name in new[] { "intents", "capture-phases" })
        {
            string path = Path.Combine(output, name);
            Require(!Directory.Exists(path) && !File.Exists(path), "A fresh capture root is required.");
            Directory.CreateDirectory(path);
            NoLink(path);
        }
    }

    // The caller obtained Core and the actual selected field from the real
    // loaded assembly. All origin bodies and module identity are read here,
    // before allocating a launch directory or doing expensive stage copies.
    internal static Journal Begin(string sessionPath, Session session, Assembly core, string stageOwner,
        Type? controller, int active, string? selectorName, string? selectorValue, string targetAssembly)
    {
        Require(session.BoundIntentVersion == Version, "New child launchers require the bound-intent capture protocol.");
        Require(session.Schema == Schema && session.CoverageAnalysisOff
            && PathEquals(sessionPath, Path.Combine(session.OutputRoot, "session.json")), "Unexpected capture session.");
        Require(core.ManifestModule.ModuleVersionId == Mvid(core.Location), "The actual loaded parent Core origin changed.");
        string parentAssembly = typeof(HealthcheckMutationChildSupport).Assembly.Location;
        var intent = new Intent(CaptureSchema, Guid.NewGuid().ToString("N"), Path.GetFullPath(sessionPath), Hash(sessionPath),
            DateTimeOffset.UtcNow, Environment.ProcessId, stageOwner, parentAssembly, Hash(parentAssembly),
            core.Location, Hash(core.Location), core.ManifestModule.ModuleVersionId, controller?.FullName, active,
            selectorName, selectorValue, controller is not null, Path.GetFullPath(targetAssembly), Hash(targetAssembly));
        return BeginBoundInput(session, intent);
    }

    // Receiving seam for bounded journal/parser tests. It supplies no native
    // execution witness. Production enters through Begin's actual-origin reads.
    internal static Journal BeginBoundInput(Session session, Intent intent)
    {
        ValidateIntent(session, intent);
        Require(PathEquals(intent.SessionPath, Path.Combine(session.OutputRoot, "session.json"))
            && Hash(intent.SessionPath) == intent.SessionSha256, "Bound intent session changed before writing.");
        string intentRoot = Root(session, "intents"), phaseRoot = Root(session, "capture-phases");
        string path = Path.Combine(intentRoot, intent.Id + ".json");
        // Flush(true) completes before the launch directory can be created.
        DurableWrite(path, intent);
        string directory = Path.Combine(phaseRoot, intent.Id);
        Require(!Directory.Exists(directory) && !File.Exists(directory), "Capture phases unexpectedly already exist.");
        Directory.CreateDirectory(directory);
        return new Journal(path, intent, directory, 0, Hash(path));
    }

    internal static Journal Open(Session session, Launch launch)
    {
        Require(session.BoundIntentVersion == Version, "Missing new launch capture version.");
        string path = Path.Combine(Root(session, "intents"), launch.Id + ".json");
        Intent intent = Read<Intent>(Input(Root(session, "intents"), launch.Id + ".json"));
        ValidateIntent(session, intent); MatchLaunch(intent, launch);
        string directory = Path.Combine(Root(session, "capture-phases"), intent.Id);
        Transition[] transitions = ReadChain(directory, intent, Hash(path));
        Require(transitions.Length == 2 && transitions.Select(row => row.Phase).SequenceEqual(NormalOrder.Take(2), StringComparer.Ordinal),
            "The parent must observe a prepared launch exactly once.");
        return new Journal(path, intent, directory, 2, Hash(Path.Combine(directory, "002-launch-ready.json")));
    }

    internal sealed class Journal
    {
        private readonly object gate = new();
        private readonly string intentPath, directory;
        private readonly Intent intent;
        private int sequence;
        private string previous;

        internal Journal(string intentPath, Intent intent, string directory, int sequence, string previous)
        { this.intentPath = intentPath; this.intent = intent; this.directory = directory; this.sequence = sequence; this.previous = previous; }

        internal string Id => intent.Id;
        internal Intent Binding => intent;

        internal void Append(string phase, string? evidencePath = null, string? evidenceSha256 = null,
            int? childPid = null, DateTimeOffset? childStartedUtc = null, int? exitCode = null, string? failure = null,
            DateTimeOffset? observedUtc = null)
        {
            lock (gate)
            {
                Require(NormalOrder.Contains(phase, StringComparer.Ordinal) || FailureKinds.Contains(phase, StringComparer.Ordinal), "Unknown capture phase.");
                Require(sequence < 12, "The bounded child capture phase count was exceeded.");
                string intentSha = Hash(intentPath);
                var transition = new Transition(CaptureSchema, intent.Id, intentSha, sequence + 1, previous, phase,
                    observedUtc ?? DateTimeOffset.UtcNow, intent.ParentPid, intent.StageOwnerId, evidencePath, evidenceSha256,
                    childPid, childStartedUtc, exitCode, failure);
                string path = Path.Combine(directory, (sequence + 1).ToString("D3", System.Globalization.CultureInfo.InvariantCulture) + "-" + phase + ".json");
                DurableWrite(path, transition);
                previous = Hash(path); sequence++;
            }
        }
    }

    internal static AuditResult Audit(Session session, string sessionPath, string[] launchDirectories)
    {
        if (session.BoundIntentVersion == 0)
        {
            Require(!Directory.Exists(Path.Combine(session.OutputRoot, "intents"))
                && !Directory.Exists(Path.Combine(session.OutputRoot, "capture-phases")), "Legacy capture cannot contain a downgraded new intent journal.");
            foreach (string directory in launchDirectories)
            {
                string launchPath = Path.Combine(directory, "launch.json");
                // Missing old launch records remain the existing lifecycle
                // audit's failures; they cannot invent a legacy body binding.
                if (File.Exists(launchPath)) { RequireRecorderVersion(session, Read<Launch>(launchPath)); }
            }
            return new(0, 0, launchDirectories.Length, [], [], HistoricalLegacyCaptureClaim: false);
        }
        Require(session.BoundIntentVersion == Version, "Unknown bound-intent capture version.");
        string intentRoot = Root(session, "intents"), phaseRoot = Root(session, "capture-phases");
        string[] files = Directory.GetFiles(intentRoot);
        Require(files.Length is > 0 and <= 200000 && Directory.GetDirectories(intentRoot).Length == 0, "No bounded flat intent inventory exists.");
        Require(Directory.GetFiles(phaseRoot).Length == 0, "Unexpected loose phase records.");
        var failures = new List<string>();
        var rows = new List<AuditRow>();
        var intended = new HashSet<string>(StringComparer.Ordinal);
        foreach (string file in files.Order(StringComparer.Ordinal))
        {
            Intent? intent = null;
            string id = Path.GetFileNameWithoutExtension(file);
            try
            {
                Require(Path.GetExtension(file) == ".json" && ValidId(id) && intended.Add(id), "Malformed or duplicate intent filename.");
                NoLink(file); intent = Read<Intent>(file); ValidateIntent(session, intent);
                Require(intent.Id == id && PathEquals(intent.SessionPath, sessionPath) && Hash(sessionPath) == intent.SessionSha256, "Intent session/ID binding differs.");
                string launchDirectory = Path.Combine(session.OutputRoot, "launches", id);
                Require(launchDirectories.Any(path => PathEquals(path, launchDirectory)), "Bound intent has no launch directory.");
                NoLink(launchDirectory);
                Launch launch = Read<Launch>(Input(launchDirectory, "launch.json")); MatchLaunch(intent, launch);
                RequireRecorderVersion(session, launch);
                Transition[] transitions = ReadChain(Path.Combine(phaseRoot, id), intent, Hash(file));
                Require(transitions.Length == 5 && transitions.Select(row => row.Phase).SequenceEqual(NormalOrder, StringComparer.Ordinal)
                    && transitions.All(row => row.Failure is null), "Incomplete or failed bound child capture lifecycle.");
                string stageHash = Digest(JsonSerializer.Serialize(launch.Stage, JsonOptions));
                Require(PathEquals(transitions[0].EvidencePath, launch.Stage.Directory) && transitions[0].EvidenceSha256 == stageHash,
                    "Captured stage differs from its exact launch.");
                string launchPath = Path.Combine(launchDirectory, "launch.json"), parentPath = Path.Combine(launchDirectory, "parent-exit.json");
                Require(PathEquals(transitions[1].EvidencePath, launchPath) && transitions[1].EvidenceSha256 == Hash(launchPath), "Captured launch changed.");
                ParentExitReceipt parent = Read<ParentExitReceipt>(Input(launchDirectory, "parent-exit.json"));
                Require(!parent.ForcedStop && !parent.TimedOut && parent.ParentPid == intent.ParentPid && parent.LaunchId == id
                    && parent.Schema == Schema && parent.LaunchSha256 == Hash(launchPath) && parent.Pid > 0,
                    "Captured parent lifecycle is not complete normal evidence.");
                Require(transitions[2].ChildPid == parent.Pid && transitions[2].ChildStartedUtc == parent.StartedUtc
                    && transitions[2].ObservedUtc == parent.ObservedStartedUtc
                    && transitions[2].EvidencePath is null && transitions[2].EvidenceSha256 is null && transitions[2].ExitCode is null,
                    "Captured actual process start differs from the parent receipt.");
                foreach (Transition terminal in transitions.Skip(3))
                {
                    Require(terminal.ChildPid == parent.Pid && terminal.ExitCode == parent.ExitCode && terminal.ChildStartedUtc is null
                        && PathEquals(terminal.EvidencePath, parentPath) && terminal.EvidenceSha256 == Hash(parentPath)
                        && terminal.ObservedUtc >= parent.ObservedUtc, "Captured terminal parent receipt differs.");
                }
                rows.Add(new(id, intent.ParentPid, intent.ActiveMutant, intent.ParentCoreSha256, true, null));
            }
            catch (Exception error) when (error is InvalidOperationException or IOException or JsonException or UnauthorizedAccessException or ArgumentException)
            {
                string detail = "Bound child intent " + id + ", parent=" + intent?.ParentPid + ", selector=" + intent?.ActiveMutant
                    + ", Core=" + intent?.ParentCoreSha256 + ": " + error.Message;
                failures.Add(detail); rows.Add(new(id, intent?.ParentPid, intent?.ActiveMutant, intent?.ParentCoreSha256, false, detail));
            }
        }
        foreach (string directory in launchDirectories)
        {
            if (!intended.Contains(Path.GetFileName(directory))) { failures.Add("Launch directory has no bound intent: " + directory); }
        }
        foreach (string directory in Directory.GetDirectories(phaseRoot))
        {
            if (!intended.Contains(Path.GetFileName(directory))) { failures.Add("Capture phases have no bound intent: " + directory); }
        }
        return new(Version, files.Length, launchDirectories.Length, rows.ToArray(), failures.ToArray(), HistoricalLegacyCaptureClaim: false);
    }

    private static void ValidateIntent(Session session, Intent intent)
    {
        Require(session.BoundIntentVersion == Version && intent.Schema == CaptureSchema && ValidId(intent.Id)
            && ValidId(intent.StageOwnerId) && intent.ParentPid > 0 && intent.CreatedUtc >= session.CreatedUtc
            && intent.ActiveMutant >= -1 && intent.Instrumented == (intent.ControllerType is not null)
            && (intent.Instrumented || intent.ActiveMutant == -1) && intent.ParentCoreMvid != Guid.Empty
            && new[] { intent.SessionSha256, intent.ParentAssemblySha256, intent.ParentCoreSha256, intent.TargetAssemblySha256 }.All(IsHash)
            && new[] { intent.SessionPath, intent.ParentAssemblyPath, intent.ParentCorePath, intent.TargetAssemblyPath }.All(Path.IsPathFullyQualified)
            && Path.GetFileName(intent.TargetAssemblyPath) is "CisHealthProbe.dll" or "CIS.Healthcheck.HttpFixture.dll",
            "Invalid source/selector/Core-bound child intent.");
    }

    internal static int RecorderVersion(string path, string expectedSha256)
    {
        NoLink(path);
        Require(IsHash(expectedSha256) && Hash(path) == expectedSha256, "The actual parent recorder body hash differs.");
        using FileStream stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader metadata = pe.GetMetadataReader();
        TypeDefinitionHandle[] types = metadata.TypeDefinitions.Where(handle =>
        {
            TypeDefinition type = metadata.GetTypeDefinition(handle);
            return metadata.GetString(type.Namespace) == "CIS.Verification.HealthcheckMutationChildren"
                && metadata.GetString(type.Name) == "HealthcheckMutationChildSupport";
        }).ToArray();
        Require(types.Length == 1, "The actual parent body lacks its exact recorder type.");
        FieldDefinition[] fields = metadata.GetTypeDefinition(types[0]).GetFields().Select(metadata.GetFieldDefinition)
            .Where(field => metadata.GetString(field.Name) == "RequiredBoundIntentVersion").ToArray();
        if (fields.Length == 0) { return 0; } // Actual old v4 recorder body.
        Require(fields.Length == 1, "Ambiguous parent capture-version marker.");
        FieldDefinition marker = fields[0];
        Require(marker.Attributes.HasFlag(FieldAttributes.Static) && marker.Attributes.HasFlag(FieldAttributes.Literal)
            && marker.Attributes.HasFlag(FieldAttributes.HasDefault) && !marker.GetDefaultValue().IsNil,
            "The parent capture-version marker is not a literal field.");
        BlobReader signature = metadata.GetBlobReader(marker.Signature);
        Require(signature.ReadByte() == 0x06 && signature.ReadByte() == 0x08 && signature.RemainingBytes == 0,
            "The parent capture-version marker is not an Int32 field.");
        Constant constant = metadata.GetConstant(marker.GetDefaultValue());
        Require(constant.TypeCode == ConstantTypeCode.Int32, "The parent capture-version constant has a different type.");
        BlobReader value = metadata.GetBlobReader(constant.Value);
        Require(value.RemainingBytes == 4, "The parent capture-version constant has a different size.");
        int version = value.ReadInt32();
        Require(version == Version, "Unknown actual parent recorder capture version.");
        return version;
    }

    private static void RequireRecorderVersion(Session session, Launch launch) => Require(
        RecorderVersion(launch.ParentAssemblyPath, launch.ParentAssemblySha256) == session.BoundIntentVersion,
        "The actual parent recorder body forbids this capture-session downgrade/version.");

    private static void MatchLaunch(Intent intent, Launch launch)
    {
        Require(launch.Id == intent.Id && launch.ParentPid == intent.ParentPid && launch.StageOwnerId == intent.StageOwnerId
            && PathEquals(launch.SessionPath, intent.SessionPath) && launch.SessionSha256 == intent.SessionSha256
            && launch.CreatedUtc >= intent.CreatedUtc && PathEquals(launch.ParentAssemblyPath, intent.ParentAssemblyPath)
            && launch.ParentAssemblySha256 == intent.ParentAssemblySha256 && PathEquals(launch.ParentCorePath, intent.ParentCorePath)
            && launch.ParentCoreSha256 == intent.ParentCoreSha256 && launch.ParentCoreMvid == intent.ParentCoreMvid
            && launch.ControllerType == intent.ControllerType && launch.ActiveMutant == intent.ActiveMutant
            && launch.OriginalSelectorName == intent.OriginalSelectorName && launch.OriginalSelectorValue == intent.OriginalSelectorValue
            && launch.Instrumented == intent.Instrumented
            && PathEquals(intent.TargetAssemblyPath, Path.Combine(launch.Stage.OriginalDirectory, launch.Stage.AssemblyFile))
            && launch.Stage.OriginalFiles.Count(pin => pin.Path == launch.Stage.AssemblyFile && pin.Sha256 == intent.TargetAssemblySha256) == 1,
            "Launch does not match its durable source/selector/Core-bound intent.");
    }

    private static Transition[] ReadChain(string directory, Intent intent, string intentSha)
    {
        NoLink(directory);
        Require(Directory.GetDirectories(directory).Length == 0, "Nested capture phases are forbidden.");
        string[] paths = Directory.GetFiles(directory).Order(StringComparer.Ordinal).ToArray();
        Require(paths.Length is > 0 and <= 12, "Missing or unbounded capture phase chain.");
        string previous = intentSha; DateTimeOffset priorTime = intent.CreatedUtc;
        var rows = new List<Transition>();
        for (int index = 0; index < paths.Length; index++)
        {
            NoLink(paths[index]); Transition row = Read<Transition>(paths[index]);
            Require(row.Schema == CaptureSchema && row.Id == intent.Id && row.IntentSha256 == intentSha
                && row.Sequence == index + 1 && row.PreviousSha256 == previous && row.ParentPid == intent.ParentPid
                && row.StageOwnerId == intent.StageOwnerId && row.ObservedUtc >= priorTime
                && (NormalOrder.Contains(row.Phase, StringComparer.Ordinal) || FailureKinds.Contains(row.Phase, StringComparer.Ordinal))
                && Path.GetFileName(paths[index]) == (index + 1).ToString("D3", System.Globalization.CultureInfo.InvariantCulture) + "-" + row.Phase + ".json",
                "Capture phase identity/order/hash chain differs.");
            rows.Add(row); previous = Hash(paths[index]); priorTime = row.ObservedUtc;
        }
        return rows.ToArray();
    }

    private static string Root(Session session, string name)
    {
        Require(Directory.Exists(session.OutputRoot), "The capture session root is missing.");
        NoLink(session.OutputRoot);
        string root = Path.Combine(session.OutputRoot, name);
        Require(Directory.Exists(root), "The required bound capture root is missing: " + name);
        NoLink(root); return root;
    }
    private static bool ValidId(string id) => Guid.TryParseExact(id, "N", out Guid guid) && guid != Guid.Empty && guid.ToString("N") == id;
    private static bool IsHash(string hash) => hash.Length == 64 && hash.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static bool PathEquals(string? first, string second) => first is not null && string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), PathComparison);
    private static void NoLink(string path) => Require((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0, "Linked capture evidence is forbidden.");
    private static void DurableWrite<T>(string path, T value)
    {
        byte[] bytes = Utf8.GetBytes(JsonSerializer.Serialize(value, JsonOptions) + "\n");
        Require(bytes.Length <= 1024 * 1024, "A capture record exceeds its bounded size.");
        using FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        stream.Write(bytes); stream.Flush(flushToDisk: true);
    }
}
