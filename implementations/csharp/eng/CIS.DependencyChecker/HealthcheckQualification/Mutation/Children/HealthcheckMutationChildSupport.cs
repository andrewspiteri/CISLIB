using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using static CIS.Verification.HealthcheckMutationChildren.HealthcheckMutationChildProtocol;

namespace CIS.Verification.HealthcheckMutationChildren;

internal static class HealthcheckMutationChildSupport
{
    // Read from the exact parent recorder PE by the auditor. Removing mutable
    // session fields/directories cannot downgrade a body compiled for capture1.
    internal const int RequiredBoundIntentVersion = 1;
    private static readonly object Gate = new();
    private static readonly string StageOwnerId = Guid.NewGuid().ToString("N");
    private static readonly Dictionary<string, Stage> Stages = new(StringComparer.Ordinal);
    private static readonly Dictionary<Process, ParentObservation> Observations = [];

    internal static void ObserveStarted(Process child)
    {
        DateTimeOffset observedStartedUtc = DateTimeOffset.UtcNow;
        string? path = Context(child);
        if (path is null) { return; }
        try
        {
            Launch launch = Read<Launch>(path);
            Require(launch.Schema == Schema && launch.ParentPid == Environment.ProcessId && launch.StageOwnerId == StageOwnerId,
                "The actual parent launched an unrelated child context.");
            lock (Gate) { Observations.Add(child, new ParentObservation(child, path, launch, observedStartedUtc)); }
        }
        catch (Exception error)
        {
            Write(Path.Combine(Path.GetDirectoryName(path)!, "parent-observer-failure.json"), new { error = error.ToString() });
            throw;
        }
    }

    internal static void MarkForcedStop(Process child, string reason) => Observation(child)?.Mark("forced-stop", reason);
    internal static void MarkTimeout(Process child, string operation) => Observation(child)?.Mark("timeout", operation);
    internal static void CompleteLifecycle(Process child)
    {
        ParentObservation? observation = Observation(child);
        if (observation is null) { return; }
        try { observation.Complete(); }
        finally { lock (Gate) { Observations.Remove(child); } }
    }

    private static string? Context(Process child)
    {
        child.StartInfo.Environment.TryGetValue(LaunchVariable, out string? path);
        Require(!string.IsNullOrEmpty(path) || string.IsNullOrEmpty(Environment.GetEnvironmentVariable(SessionVariable)),
            "A prepared mutation child bypassed its launch context.");
        return string.IsNullOrEmpty(path) ? null : path;
    }

    private static ParentObservation? Observation(Process child)
    {
        if (Context(child) is null) { return null; }
        lock (Gate)
        {
            Require(Observations.TryGetValue(child, out ParentObservation? observation), "The real child was not observed immediately after Process.Start.");
            return observation;
        }
    }

    private sealed class ParentObservation
    {
        private readonly object gate = new();
        private readonly Process child;
        private readonly string path, directory;
        private readonly Launch launch;
        private readonly HealthcheckMutationChildCapture.Journal capture;
        private readonly EventHandler handler;
        private readonly int pid;
        private readonly DateTimeOffset started, observedStarted;
        private bool recorded, completed, forced, timedOut;
        internal ParentObservation(Process child, string path, Launch launch, DateTimeOffset observedStartedUtc)
        {
            this.child = child; this.path = path; this.launch = launch; directory = Path.GetDirectoryName(path)!;
            pid = child.Id; started = child.StartTime.ToUniversalTime(); observedStarted = observedStartedUtc;
            capture = HealthcheckMutationChildCapture.Open(Read<Session>(launch.SessionPath), launch);
            capture.Append("process-started", childPid: pid, childStartedUtc: started, observedUtc: observedStartedUtc);
            handler = (_, _) =>
            {
                lock (gate)
                {
                    if (completed) { return; }
                    try { RecordExit(); }
                    catch (Exception error)
                    {
                        try { Failure(error); }
                        catch (Exception) { /* A missing receipt still fails audit; do not crash the native test host. */ }
                    }
                }
            };
            child.Exited += handler; child.EnableRaisingEvents = true;
            lock (gate) { if (child.HasExited) { RecordExit(); } }
        }
        internal void Mark(string kind, string reason)
        {
            lock (gate)
            {
                Require(!completed && reason.Length is > 0 and <= 128 && !reason.Any(char.IsControl), "Invalid lifecycle marker.");
                if (kind == "forced-stop") { forced = true; } else { timedOut = true; }
                string marker = Path.Combine(directory, kind + ".json");
                if (!File.Exists(marker)) { Write(marker, new { schema = Schema, launchId = launch.Id, pid, reason, observedUtc = DateTimeOffset.UtcNow }); }
                capture.Append(kind, marker, Hash(marker), childPid: pid, failure: reason);
            }
        }
        internal void Complete()
        {
            lock (gate)
            {
                try
                {
                    Require(child.HasExited, "The parent disposed a live child without a completed bounded exit."); RecordExit();
                    string parentExit = Path.Combine(directory, "parent-exit.json");
                    capture.Append("lifecycle-completed", parentExit, Hash(parentExit), childPid: pid, exitCode: child.ExitCode);
                }
                catch (Exception error) { Failure(error); throw; }
                finally { completed = true; child.Exited -= handler; }
            }
        }
        private void RecordExit()
        {
            if (recorded || !child.HasExited) { return; }
            Write(Path.Combine(directory, "parent-exit.json"), new ParentExitReceipt(Schema, launch.Id, Hash(path),
                Environment.ProcessId, pid, child.StartInfo.FileName, child.StartInfo.WorkingDirectory,
                child.StartInfo.ArgumentList.ToArray(), started, observedStarted, child.ExitTime.ToUniversalTime(), DateTimeOffset.UtcNow,
                child.ExitCode, forced, timedOut));
            string parentExit = Path.Combine(directory, "parent-exit.json");
            capture.Append("parent-exit", parentExit, Hash(parentExit), childPid: pid, exitCode: child.ExitCode);
            recorded = true;
        }
        private void Failure(Exception error)
        {
            string failure = Path.Combine(directory, "parent-observer-failure.json");
            if (!File.Exists(failure)) { Write(failure, new { error = error.ToString(), pid }); }
            capture.Append("observer-failed", failure, Hash(failure), childPid: pid, failure: error.ToString());
        }
    }

    // Call after constructing the real launcher's ProcessStartInfo and before adding its DLL argument.
    internal static string Prepare(string assemblyPath, ProcessStartInfo start)
    {
        string? sessionPath = Environment.GetEnvironmentVariable(SessionVariable);
        if (string.IsNullOrEmpty(sessionPath))
        {
            Require(Controller(Assembly.Load(new AssemblyName("CIS.Core"))) is null,
                "Instrumented Healthcheck children require an explicit child proof session.");
            return assemblyPath;
        }

        Session session = Read<Session>(sessionPath);
        HealthcheckMutationChildCapture.Journal? capture = null;
        string? launchDirectory = null;
        try
        {
            Require(session.BoundIntentVersion == HealthcheckMutationChildCapture.Version,
                "New child launchers require source/selector/Core-bound capture intents.");
            Require(session.Schema == Schema && session.CoverageAnalysisOff, "Invalid or coverage-selected child qualification session.");
            Require(Environment.GetEnvironmentVariable("CisMutationTesting") == "true", "Child qualification requires serialized mutation tests.");
            Require(Under(session.OutputRoot, sessionPath) && Path.GetFileName(sessionPath) == "session.json", "Unexpected child session location.");
            // HTTP tests can reach their first real child before any parent test has used a Core type.
            Assembly core = Assembly.Load(new AssemblyName("CIS.Core"));
            Type? controller = Controller(core);
            Require(controller is not null || session.AllowInitialUninstrumentedBaseline, "An instrumented-phase child launch loaded uninstrumented Core.");
            string file = Path.GetFileName(assemblyPath);
            Require(file is "CIS.Healthcheck.HttpFixture.dll" or "CisHealthProbe.dll", "Unknown Healthcheck child executable.");
            Require(!start.UseShellExecute && start.RedirectStandardInput && start.RedirectStandardOutput && start.RedirectStandardError,
                "The real bounded child launcher must retain all redirected streams.");
            Require(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("STRYKER_MUTANT_FILE")), "This proof lane requires the pinned VSTest environment selector.");
            int active = controller is null ? -1 : Active(controller);
            string? selectorName = Environment.GetEnvironmentVariable("STRYKER_MUTANT_ID_CONTROL_VAR");
            string? selectorValue = string.IsNullOrEmpty(selectorName) ? null : Environment.GetEnvironmentVariable(selectorName);
            // The VSTest collector selects the actual parent field per test. Its fallback
            // environment value can stay -1 in a session containing several mutants.
            string coreHash = Hash(core.Location);
            Guid mvid = Mvid(core.Location);
            Require(mvid == core.ManifestModule.ModuleVersionId, "The loaded parent Core differs from its on-disk origin.");
            capture = HealthcheckMutationChildCapture.Begin(sessionPath, session, core, StageOwnerId, controller,
                active, selectorName, selectorValue, assemblyPath);
            Require(capture.Binding.ParentCoreSha256 == coreHash && capture.Binding.ParentCoreMvid == mvid,
                "The actual parent Core changed while binding the launch intent.");
            string id = capture.Id;
            launchDirectory = Path.Combine(session.OutputRoot, "launches", id);
            Require(!Directory.Exists(launchDirectory) && !File.Exists(launchDirectory), "A new launch directory is required.");
            Directory.CreateDirectory(launchDirectory);
            Stage stage;
            lock (Gate)
            {
                string key = Path.GetFullPath(assemblyPath) + ":" + coreHash;
                if (!Stages.TryGetValue(key, out stage!))
                {
                    stage = CopyStage(session, assemblyPath, core, coreHash, mvid, controller);
                    Stages.Add(key, stage);
                }
                VerifyFiles(stage.Directory, stage.DeployedFiles);
            }
            capture.Append("stage-ready", stage.Directory, Digest(System.Text.Json.JsonSerializer.Serialize(stage, JsonOptions)));
            var launch = new Launch(Schema, id, Path.GetFullPath(sessionPath), Hash(sessionPath), DateTimeOffset.UtcNow,
                Environment.ProcessId, StageOwnerId, typeof(HealthcheckMutationChildSupport).Assembly.Location,
                Hash(typeof(HealthcheckMutationChildSupport).Assembly.Location), core.Location, coreHash, mvid, controller?.FullName,
                active, selectorName, selectorValue, controller is not null, stage);
            string launchPath = Path.Combine(launchDirectory, "launch.json");
            Write(launchPath, launch);
            capture.Append("launch-ready", launchPath, Hash(launchPath));
            // Never alter the parent environment. Every process receives one immutable selector.
            start.Environment.Remove("STRYKER_MUTANT_FILE");
            start.Environment.Remove("STRYKER_COVERAGE_FILE");
            start.Environment["STRYKER_MUTANT_ID_CONTROL_VAR"] = SelectorVariable;
            start.Environment[SelectorVariable] = active.ToString(CultureInfo.InvariantCulture);
            start.Environment[LaunchVariable] = launchPath;
            start.FileName = session.DotnetPath;
            start.WorkingDirectory = stage.Directory;
            return Path.Combine(stage.Directory, file);
        }
        catch (Exception error)
        {
            // No unbound launch directory is created on the pre-intent path.
            // Bound failures retain attribution but never qualify as children.
            if (capture is not null)
            {
                string? failure = launchDirectory is null ? null : Path.Combine(launchDirectory, "parent-failure.json");
                if (failure is not null) { Write(failure, new { id = capture.Id, error = error.ToString(), parentPid = Environment.ProcessId }); }
                capture.Append("preparation-failed", failure, failure is null ? null : Hash(failure), failure: error.ToString());
            }
            throw;
        }
    }

    private static Stage CopyStage(Session session, string assemblyPath, Assembly core, string coreHash, Guid mvid, Type? controller)
    {
        string original = Path.GetDirectoryName(Path.GetFullPath(assemblyPath))!;
        Require(Under(AppContext.BaseDirectory, original), "The launcher must start from its normal copied fixture output.");
        FilePin[] originalFiles = Files(original);
        Require(originalFiles.Any(pin => pin.Path == "CIS.Core.dll") && originalFiles.Any(pin => pin.Path == Path.GetFileName(assemblyPath)),
            "The normal fixture output is incomplete.");
        string target = Path.Combine(session.OutputRoot, "stages", Environment.ProcessId.ToString(CultureInfo.InvariantCulture),
            StageOwnerId, coreHash, Path.GetFileNameWithoutExtension(assemblyPath));
        Require(!Directory.Exists(target), "A private child stage unexpectedly already exists.");
        Directory.CreateDirectory(target);
        foreach (FilePin pin in originalFiles)
        {
            string source = Input(original, pin.Path), destination = Path.Combine(target, pin.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: false);
            Require(Hash(destination) == pin.Sha256, "A copied fixture dependency changed.");
        }
        File.Copy(core.Location, Path.Combine(target, "CIS.Core.dll"), overwrite: true);
        string pdb = Path.ChangeExtension(core.Location, ".pdb");
        if (File.Exists(pdb)) { File.Copy(pdb, Path.Combine(target, "CIS.Core.pdb"), overwrite: true); }
        FilePin[] deployed = Files(target);
        Require(Hash(Path.Combine(target, "CIS.Core.dll")) == coreHash && Mvid(Path.Combine(target, "CIS.Core.dll")) == mvid,
            "The private child did not receive the loaded parent Core.");
        foreach (FilePin pin in originalFiles.Where(pin => pin.Path is not "CIS.Core.dll" and not "CIS.Core.pdb"))
        {
            Require(deployed.Single(item => item.Path == pin.Path).Sha256 == pin.Sha256, "A non-Core fixture dependency changed during staging.");
        }
        string name = AssemblyName.GetAssemblyName(assemblyPath).Name!;
        Require(name == (Path.GetFileName(assemblyPath) == "CisHealthProbe.dll" ? "CisHealthProbe" : "CIS.Healthcheck.HttpFixture"),
            "The real fixture or probe assembly identity changed.");
        return new Stage(target, original, Path.GetFileName(assemblyPath), name, coreHash, mvid, controller?.FullName, originalFiles, deployed);
    }
}
