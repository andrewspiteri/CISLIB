using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.Healthcheck;
using CIS.Healthcheck.Pipes;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

// Compiled only by the explicit Linux identity cohort. No sudo or process-wide
// identity/environment changes occur inside a testhost or mutation worker.
[TestClass]
[TestCategory("LinuxIdentityQualification")]
public sealed class HealthPipeForeignOwnerBoundaryTests
{
    private const string Request = "{\"protocolVersion\":1,\"view\":\"health\"}";
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    [TestMethod]
    public async Task CurrentUserOnlyRejectsAnAccessibleForeignOwnerBeforeSendingARequest()
    {
        if (!OperatingSystem.IsLinux()) { throw new InvalidDataException("The opted-in foreign-owner cohort requires actual Linux execution."); }
        string manifestPath = Environment.GetEnvironmentVariable("CIS_HEALTHCHECK_IDENTITY_MANIFEST")
            ?? throw new InvalidDataException("An explicit prepared Linux identity context is required.");
        JsonObject manifest = Read(manifestPath);
        string journals = Text(manifest, "journalRoot");
        string attempt = Path.Combine(journals, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(attempt);
        File.SetUnixFileMode(attempt, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        Write("started.json", new { schema = "cis.healthcheck.foreign-owner-started/v1", role = "client-guard", pid = Environment.ProcessId, manifestSha256 = Hash(manifestPath), startedUtc = DateTimeOffset.UtcNow });

        string helper = Path.Combine(attempt, "helper"), pipe = "cis-foreign-" + Guid.NewGuid().ToString("N");
        string socket = Path.Combine(Path.GetTempPath(), "CoreFxPipe_" + pipe);
        Task<ProcessResult>? execution = null;
        JsonObject? ready = null, terminal = null;
        ProcessResult? helperResult = null;
        Exception? infrastructure = null, productFailure = null;
        int? productExit = null;
        string productOutput = "", productError = "";
        using var helperCancellation = new CancellationTokenSource();
        using var output = new MemoryStream();
        using var error = new StringWriter();
        using var productCancellation = new CancellationTokenSource();
        Task<int>? productExecution = null;
        bool productOperationSettled = true;
        try
        {
            Require(OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("CisLinuxIdentityQualification") == "true", "The explicit Linux identity flag is absent.");
            Require(Uids(Environment.ProcessId).All(uid => uid == "0"), "The instrumented testhost must actually run as UID0.");
            Require(Text(manifest, "schema") == "cis.healthcheck.foreign-owner-context/v1" && Text(manifest, "runtime") == ".NET 10.0.12", "Unexpected identity context.");
            Require(SamePath(Text(manifest, "temporaryRoot"), Path.GetTempPath()), "Parent and peer must use the same existing active TMP root.");
            foreach (JsonNode? pin in manifest["files"]!.AsArray()) { Require(Hash(Text(pin!.AsObject(), "path")) == Text(pin.AsObject(), "sha256"), "Immutable positive-control bytes changed."); }
            Require(Encoding.UTF8.GetByteCount(socket) < 108, "The actual Unix socket pathname exceeds its platform bound.");
            Directory.CreateDirectory(helper);
            File.SetUnixFileMode(helper, (UnixFileMode)511);
            string dotnet = Text(manifest, "dotnet"), runuser = Text(manifest, "runuser"), host = Text(manifest, "host"), probe = Text(manifest, "probe");
            var childEnvironment = Environment.GetEnvironmentVariables().Keys.Cast<string>()
                .Where(key => key.StartsWith("CIS_HEALTHCHECK_", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(key => key, _ => (string?)null, StringComparer.Ordinal);
            execution = ProcessRunner.RunAsync(runuser,
                ["-u", "nobody", "--", "/bin/sh", "-c", "umask 000; exec \"$@\"", "cis-foreign-peer", dotnet,
                    "exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", host, "raw-foreign-owner", pipe, helper],
                Path.GetDirectoryName(host)!, TimeSpan.FromSeconds(35), childEnvironment, maximumCapturedBytesPerStream: 16384,
                cancellationToken: helperCancellation.Token);
            ready = await WaitForObjectAsync(Path.Combine(helper, "ready.json"), execution);
            int helperPid = ready["pid"]!.GetValue<int>();
            Require(Uids(helperPid).All(uid => uid == "65534"), "The real peer must have nobody real/effective/saved/filesystem UID65534.");
            Require(Text(ready, "runtime") == ".NET 10.0.12" && Text(ready, "fixtureSha256") == Hash(host)
                && Text(ready, "coreSha256") == Text(manifest, "coreSha256") && Text(ready, "pipe") == pipe
                && Text(ready, "pipesSha256") == Text(manifest, "pipesSha256") && Text(ready, "pipesSha256") == Hash(typeof(NamedPipeClientStream).Assembly.Location)
                && Text(ready, "socket") == socket && SamePath(Text(ready, "temporaryRoot"), Path.GetTempPath()), "The ready peer differs from its immutable context.");
            ProcessResult metadata = await ProcessRunner.RunAsync(Text(manifest, "stat"), ["--format=%u:%a:%F", "--", socket], helper, Limit);
            Require(metadata.ExitCode == 0 && metadata.OutputDrained && metadata.StandardOutput.Trim() == "65534:777:socket", "The real peer endpoint must be an accessible UID65534 socket0777.");

            // Root can actually exchange valid frames through the kernel. The
            // following product assertion therefore exercises the BCL UID policy.
            using (var unrestricted = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous))
            using (var cancellation = new CancellationTokenSource(Limit))
            {
                await unrestricted.ConnectAsync(cancellation.Token);
                byte[] request = Encoding.UTF8.GetBytes(Request), header = new byte[4];
                BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)request.Length);
                await unrestricted.WriteAsync(header, cancellation.Token);
                await unrestricted.WriteAsync(request, cancellation.Token);
                await unrestricted.FlushAsync(cancellation.Token);
                await unrestricted.ReadExactlyAsync(header, cancellation.Token);
                uint size = BinaryPrimitives.ReadUInt32LittleEndian(header);
                Require(size is > 0 and <= 65536, "Invalid independent positive response size.");
                byte[] response = new byte[(int)size];
                await unrestricted.ReadExactlyAsync(response, cancellation.Token);
                Require((string?)JsonNode.Parse(response)?["snapshot"]?["healthStatus"] == "Healthy", "The unrestricted real exchange was not healthy.");
            }
            ProcessResult sameOwner = await ProcessRunner.RunAsync(runuser,
                ["-u", "nobody", "--", dotnet, "exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", probe, "--pipe", pipe, "--timeout-ms", "1000"],
                Path.GetDirectoryName(probe)!, Limit, childEnvironment, maximumCapturedBytesPerStream: 16384);
            Require(sameOwner.ExitCode == 0 && sameOwner.OutputDrained && sameOwner.StandardError.Length == 0
                && (string?)JsonNode.Parse(sameOwner.StandardOutput)?["healthStatus"] == "Healthy", "The immutable same-owner production probe did not receive the valid response.");
            JsonObject positive = await WaitForCountersAsync(Path.Combine(helper, "progress.json"), 2, execution);
            Require((int?)positive["expectedRequests"] == 2 && (int?)positive["responses"] == 2, "Both independent positive controls must finish valid request/response exchanges.");
            Write("controls.json", new
            {
                schema = "cis.healthcheck.foreign-owner-controls/v1",
                pipe,
                socket,
                rootUidLine = File.ReadLines("/proc/self/status").Single(line => line.StartsWith("Uid:", StringComparison.Ordinal)),
                helperUidLine = File.ReadLines("/proc/" + helperPid + "/status").Single(line => line.StartsWith("Uid:", StringComparison.Ordinal)),
                socketStat = metadata.StandardOutput.Trim(),
                unrestrictedRootHealthy = true,
                activeCorePath = typeof(HealthProbeClient).Assembly.Location,
                activeCoreSha256 = Hash(typeof(HealthProbeClient).Assembly.Location),
                sameOwnerPid = sameOwner.ProcessId,
                sameOwnerExitCode = sameOwner.ExitCode,
                sameOwnerOutputDrained = sameOwner.OutputDrained,
                sameOwnerOutput = sameOwner.StandardOutput,
                sameOwnerError = sameOwner.StandardError,
                positiveCounters = positive
            });

            // Only this call uses the instrumented assembly loaded by this root
            // testhost. Keep its result/exception outside infrastructure handling.
            try
            {
                productCancellation.CancelAfter(TimeSpan.FromSeconds(3));
                productExecution = HealthProbeClient.RunAsync(["--pipe", pipe, "--timeout-ms", "1000"], output, error, productCancellation.Token);
                productOperationSettled = false;
                productExit = await productExecution.WaitAsync(TimeSpan.FromSeconds(5));
                productOutput = Encoding.UTF8.GetString(output.ToArray());
                productError = error.ToString();
            }
            catch (Exception exception) { productFailure = exception; }
        }
        catch (Exception exception) { MarkFailure(exception); }
        finally
        {
            try
            {
                if (execution is not null)
                {
                    File.WriteAllText(Path.Combine(helper, "stop"), "stop");
                    helperResult = await execution.WaitAsync(Limit);
                    Write("helper-process.json", new { helperResult.ProcessId, helperResult.ExitCode, helperResult.OutputDrained, helperResult.StandardOutput, helperResult.StandardError });
                    terminal = Read(Path.Combine(helper, "terminal.json"));
                    Require(helperResult.ExitCode == 0 && helperResult.OutputDrained && helperResult.StandardError.Length == 0
                        && (bool?)terminal["healthy"] == true && (bool?)terminal["socketRemoved"] == true && !File.Exists(socket)
                        && !File.Exists(Path.Combine(helper, "failure.json")), "The foreign peer failed its actual drain/socket cleanup.");
                    int helperPid = terminal["pid"]!.GetValue<int>();
                    Require(ready is not null && helperPid == (int?)ready["pid"] && !Directory.Exists("/proc/" + helperPid), "The observed foreign peer is still alive after cleanup.");
                }
            }
            catch (Exception exception)
            {
                MarkFailure(exception);
                if (execution is not null && !execution.IsCompleted)
                {
                    await helperCancellation.CancelAsync();
                    try { await execution.WaitAsync(TimeSpan.FromSeconds(6)); }
                    catch (Exception cleanupError) { MarkFailure(cleanupError); }
                }
            }
            try
            {
                await productCancellation.CancelAsync();
                if (productExecution is not null)
                {
                    try { await productExecution.WaitAsync(TimeSpan.FromSeconds(5)); }
                    catch (Exception exception)
                    {
                        if (!productExecution.IsCompleted) { throw new InvalidDataException("The actual instrumented operation did not settle after its real peer and cancellation were closed.", exception); }
                        productFailure ??= exception;
                    }
                    productOperationSettled = productExecution.IsCompleted;
                }
            }
            catch (Exception exception) { MarkFailure(exception); }
            Write("terminal.json", new
            {
                schema = "cis.healthcheck.foreign-owner-terminal/v1",
                role = "client-guard",
                pid = Environment.ProcessId,
                cleanupHealthy = infrastructure is null && helperResult is not null && terminal is not null && productOperationSettled,
                productOperationSettled,
                manifestSha256 = Hash(manifestPath),
                helperTerminalSha256 = File.Exists(Path.Combine(helper, "terminal.json")) ? Hash(Path.Combine(helper, "terminal.json")) : null,
                helperExitCode = helperResult?.ExitCode,
                helperOutputDrained = helperResult?.OutputDrained,
                productExit,
                productOutput,
                productError,
                productException = productFailure?.ToString(),
                counters = terminal?["counters"]?.DeepClone()
            });
        }
        if (infrastructure is not null) { ExceptionDispatchInfo.Capture(infrastructure).Throw(); }
        if (productFailure is not null) { ExceptionDispatchInfo.Capture(productFailure).Throw(); }
        Assert.AreEqual(2, productExit, "The instrumented UID0 client must enforce CurrentUserOnly against the actually accessible UID65534 peer.");
        Assert.AreEqual("", productOutput);
        StringAssert.Contains(productError, "TransportOrProtocolFailure");
        Assert.AreEqual(2, (int?)terminal!["counters"]!["requests"], "The guarded attempt must send zero additional request frames.");
        Console.WriteLine("CIS-FOREIGN-OWNER " + attempt);

        void MarkFailure(Exception exception)
        {
            infrastructure = infrastructure is null ? exception : new AggregateException(infrastructure, exception);
            Write("failure.json", new { schema = "cis.healthcheck.foreign-owner-failure/v1", pid = Environment.ProcessId, failure = infrastructure.ToString() });
        }
        void Write(string name, object value) => File.WriteAllText(Path.Combine(attempt, name), JsonSerializer.Serialize(value));
    }

    [TestMethod]
    public void ForeignPeerRejectionPreservesTheActualOwnersAdmission()
    {
        if (!OperatingSystem.IsLinux()) { throw new InvalidDataException("The opted-in owner-recovery cohort requires actual Linux execution."); }
        string manifest = Environment.GetEnvironmentVariable("CIS_HEALTHCHECK_IDENTITY_MANIFEST") ?? throw new InvalidDataException("An explicit prepared identity context is required.");
        using var fixture = new ServerRecoveryFixture(manifest);
        RunAndReleaseActualServer(fixture);
        var collection = Stopwatch.StartNew();
        while (fixture.Owner is { IsAlive: true } && collection.Elapsed < TimeSpan.FromSeconds(5))
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            if (fixture.Owner.IsAlive) { Thread.Sleep(1); }
        }
        bool resourceReleased = fixture.Owner?.IsAlive != true && !File.Exists(fixture.Socket);
        if (!resourceReleased)
        {
            fixture.Save("cleanup-hold.json", new { schema = "cis.healthcheck.foreign-owner-product-cleanup-hold/v1", role = "server-recovery", ownerAlive = fixture.Owner?.IsAlive, socketExists = File.Exists(fixture.Socket) });
        }
        fixture.Save("terminal.json", new
        {
            schema = "cis.healthcheck.foreign-owner-terminal/v1",
            role = "server-recovery",
            pid = Environment.ProcessId,
            manifestSha256 = Hash(manifest),
            cleanupHealthy = fixture.Infrastructure is null && fixture.AllHelpersStopped && resourceReleased,
            productOperationSettled = resourceReleased,
            actualOwnerReleased = resourceReleased,
            managedStopSettled = fixture.Completion.Stop,
            managedDisposeSettled = fixture.Completion.Dispose,
            endpointCleanupRequired = fixture.ForcedEndpointCleanup,
            capturesAfterForeign = fixture.CapturesAfterForeign,
            targetSocketPrepared = fixture.TargetSocketPrepared,
            targetExchangeRequested = fixture.TargetExchangeRequested,
            capturesAfterOwner = fixture.CapturesAfterOwner,
            target = fixture.Target,
            ownerProbeHealthy = fixture.OwnerProbeHealthy,
            productException = fixture.ProductFailure?.ToString()
        });
        if (fixture.Infrastructure is not null) { ExceptionDispatchInfo.Capture(fixture.Infrastructure).Throw(); }
        Assert.IsTrue(resourceReleased, "The actual instrumented owner/socket must be released before any native outcome can be admitted.");
        if (fixture.ProductFailure is not null) { ExceptionDispatchInfo.Capture(fixture.ProductFailure).Throw(); }
        Assert.IsNotNull(fixture.Target);
        Assert.IsTrue((bool)fixture.Target["connected"]!, "The actual foreign client must reach the socket before UID enforcement is observed.");
        Assert.IsFalse((bool)fixture.Target["healthy"]!);
        Assert.AreEqual(0, fixture.CapturesAfterForeign, "A rejected foreign client cannot capture a health snapshot.");
        Assert.IsTrue(fixture.OwnerProbeHealthy, "The actual owner's valid probe must remain usable after the foreign rejection.");
        Assert.AreEqual(1, fixture.CapturesAfterOwner);
        Assert.IsTrue(fixture.Completion.Stop && fixture.Completion.Dispose && !fixture.ForcedEndpointCleanup,
            "The actual managed Stop/Dispose notifications must complete; collection or emergency physical cleanup does not manufacture settlement.");
        Console.WriteLine("CIS-FOREIGN-OWNER " + fixture.Attempt);
    }

    // No returned async state machine, task or fixture field strongly retains
    // the actual server. Pending notification cycles may then be distinguished
    // from actual queued/accepting owners and physical socket work.
    [MethodImpl(MethodImplOptions.NoInlining)]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private static void RunAndReleaseActualServer(ServerRecoveryFixture fixture)
    {
        HealthcheckService? service = null;
        HealthPipeServer? server = null;
        var actualEndpoints = new List<NamedPipeServerStream>();
        try
        {
            fixture.PreparePositiveControls();
            try
            {
                service = new HealthcheckService(new HealthcheckOptions(), new HealthApplicationIdentity { Name = "foreign-owner-recovery", InstanceId = Guid.NewGuid() });
                service.MarkStartupCompleted();
                var source = new RecoverySource(service);
                server = new HealthPipeServer(source, new HealthcheckOptions { PipeEnabled = true, PipeName = fixture.TargetPipe, PipeMaxClients = 1 }, null,
                    (name, clients, first) =>
                    {
                        NamedPipeServerStream actual = HealthPipeEndpoint.Create(name, clients, first);
                        actualEndpoints.Add(actual);
                        return actual;
                    });
                fixture.Owner = new WeakReference(server);
                server.StartAsync().WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
                fixture.ObserveTargetSocket();
                fixture.Target = fixture.ObserveForeignTarget();
                fixture.CapturesAfterForeign = Volatile.Read(ref source.Captures);
                fixture.OwnerProbeHealthy = fixture.Probe("owner-probe", fixture.TargetPipe);
                fixture.CapturesAfterOwner = Volatile.Read(ref source.Captures);
            }
            catch (Exception exception) { fixture.ProductFailure = exception; }
        }
        catch (Exception exception) { fixture.MarkInfrastructure(exception); }
        finally
        {
            fixture.StopHelpers();
            if (server is not null)
            {
                Task stopping, disposing;
                try { stopping = server.StopAsync(); } catch (Exception exception) { stopping = Task.FromException(exception); }
                try { disposing = server.DisposeAsync().AsTask(); } catch (Exception exception) { disposing = Task.FromException(exception); }
                ObserveNotification(stopping, fixture.Completion, isDispose: false);
                ObserveNotification(disposing, fixture.Completion, isDispose: true);
                try { Task.WhenAll(stopping, disposing).WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult(); }
                catch (Exception exception) { fixture.ProductFailure ??= exception; }
                fixture.ForcedEndpointCleanup = !stopping.IsCompletedSuccessfully || !disposing.IsCompletedSuccessfully;
            }
            // These are the actual OS endpoints returned by actual Create,
            // retained only for emergency cleanup after product observations.
            // No managed completion or private state is modified.
            foreach (NamedPipeServerStream endpoint in actualEndpoints)
            {
                try { endpoint.Dispose(); }
                catch (Exception exception) { fixture.ProductFailure ??= exception; }
            }
            actualEndpoints.Clear();
            if (service is not null)
            {
                try { service.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult(); }
                catch (Exception exception) { fixture.ProductFailure ??= exception; }
            }
        }

        static void ObserveNotification(Task actual, RecoveryCompletion completion, bool isDispose)
        {
            _ = actual.ContinueWith(static (completed, state) =>
            {
                var observed = ((RecoveryCompletion Completion, bool Dispose))state!;
                _ = completed.Exception;
                if (completed.IsCompletedSuccessfully)
                {
                    if (observed.Dispose) { observed.Completion.SetDispose(); } else { observed.Completion.SetStop(); }
                }
            }, (completion, isDispose), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    private sealed class RecoverySource(HealthcheckService service) : IHealthSnapshotSource
    {
        internal int Captures;
        public HealthSnapshot CaptureSnapshot() { Interlocked.Increment(ref Captures); return service.CaptureSnapshot(); }
    }

    private sealed class RecoveryCompletion
    {
        private int stopped, disposed;
        internal bool Stop => Volatile.Read(ref stopped) == 1;
        internal bool Dispose => Volatile.Read(ref disposed) == 1;
        internal void SetStop() => Volatile.Write(ref stopped, 1);
        internal void SetDispose() => Volatile.Write(ref disposed, 1);
    }

    private sealed class ServerRecoveryFixture : IDisposable
    {
        private readonly JsonObject manifest;
        private readonly string host, dotnet, runuser;
        private readonly Dictionary<string, Task<ProcessResult>> helpers = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> helperDirectories = new(StringComparer.Ordinal);
        private readonly CancellationTokenSource helperCancellation = new();
        private readonly Dictionary<string, string?> childEnvironment;
        internal string Attempt { get; }
        internal string TargetPipe { get; } = "cis-foreign-target-" + Guid.NewGuid().ToString("N");
        internal string Socket => Path.Combine(Path.GetTempPath(), "CoreFxPipe_" + TargetPipe);
        internal WeakReference? Owner;
        internal Exception? Infrastructure, ProductFailure;
        internal readonly RecoveryCompletion Completion = new();
        internal bool ForcedEndpointCleanup, OwnerProbeHealthy, AllHelpersStopped, TargetSocketPrepared, TargetExchangeRequested;
        internal int CapturesAfterForeign = -1, CapturesAfterOwner = -1;
        internal JsonObject? Target;

        internal ServerRecoveryFixture(string manifestPath)
        {
            manifest = Read(manifestPath);
            Attempt = Path.Combine(Text(manifest, "journalRoot"), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Attempt);
            if (OperatingSystem.IsLinux()) { File.SetUnixFileMode(Attempt, (UnixFileMode)493); }
            Save("started.json", new { schema = "cis.healthcheck.foreign-owner-started/v1", role = "server-recovery", pid = Environment.ProcessId, manifestSha256 = Hash(manifestPath) });
            host = Text(manifest, "host"); dotnet = Text(manifest, "dotnet"); runuser = Text(manifest, "runuser");
            childEnvironment = Environment.GetEnvironmentVariables().Keys.Cast<string>().Where(key => key.StartsWith("CIS_HEALTHCHECK_", StringComparison.OrdinalIgnoreCase)).ToDictionary(key => key, _ => (string?)null, StringComparer.Ordinal);
        }
        internal void PreparePositiveControls()
        {
            Require(OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("CisLinuxIdentityQualification") == "true" && Uids(Environment.ProcessId).All(uid => uid == "0"), "The actual root identity cohort is required.");
            Require(SamePath(Text(manifest, "temporaryRoot"), Path.GetTempPath()), "Recovery parent/peers must share the existing active TMP.");
            foreach (JsonNode? pin in manifest["files"]!.AsArray()) { Require(Hash(Text(pin!.AsObject(), "path")) == Text(pin.AsObject(), "sha256"), "An immutable recovery positive-control input changed."); }
            Require(Encoding.UTF8.GetByteCount(Socket) < 108, "The actual target socket exceeds the Unix path bound.");
            string positivePipe = "cis-foreign-positive-" + Guid.NewGuid().ToString("N");
            Start("baseline-peer", false, ["raw-foreign-owner", positivePipe]);
            JsonObject peer = Ready("baseline-peer", "0");
            ProcessResult mode = Run(Text(manifest, "stat"), ["--format=%u:%a:%F", "--", (string)peer["socket"]!]);
            Require(mode.ExitCode == 0 && mode.StandardOutput.Trim() == "0:777:socket", "The immutable root peer must actually be accessible socket0777.");
            Require(Probe("baseline-probe", positivePipe), "The immutable production owner probe must validate the positive peer's actual response.");
            Start("foreign-client", true, ["raw-foreign-client", positivePipe, TargetPipe]);
            JsonObject client = Ready("foreign-client", "65534");
            Require((string?)client["positivePipe"] == positivePipe && (string?)client["targetPipe"] == TargetPipe, "The foreign client changed its declared positive/target pipes.");
            JsonObject positive = WaitForObjectAsync(Path.Combine(helperDirectories["foreign-client"], "positive.json"), helpers["foreign-client"]).GetAwaiter().GetResult();
            Require((bool?)positive["healthy"] == true && (bool?)positive["connected"] == true, "The same immutable foreign client must first exchange valid frames with the unrestricted root peer.");
            JsonObject counters = WaitForCountersAsync(Path.Combine(helperDirectories["baseline-peer"], "progress.json"), 2, helpers["baseline-peer"]).GetAwaiter().GetResult();
            Require((int?)counters["expectedRequests"] == 2, "The real shipping probe and foreign raw client must send the same valid framed request.");
            Save("controls.json", new
            {
                schema = "cis.healthcheck.server-owner-controls/v1",
                rootUidLine = File.ReadLines("/proc/self/status").Single(line => line.StartsWith("Uid:", StringComparison.Ordinal)),
                baselineSocketStat = mode.StandardOutput.Trim(),
                baselineProbeHealthy = true,
                positivePipe,
                targetPipe = TargetPipe,
                activeCorePath = typeof(HealthPipeServer).Assembly.Location,
                activeCoreSha256 = Hash(typeof(HealthPipeServer).Assembly.Location)
            });
            Stop("baseline-peer");
        }
        internal void ObserveTargetSocket()
        {
            // A missing endpoint after actual startup is a product result. Once
            // present, failures of this test's chmod/stat mechanism are fixture
            // failures and must never become native product kills.
            Require(File.Exists(Socket), "Actual startup did not retain its real target endpoint.");
            ProcessResult mode;
            try
            {
                if (OperatingSystem.IsLinux()) { File.SetUnixFileMode(Socket, (UnixFileMode)511); }
                mode = Run(Text(manifest, "stat"), ["--format=%u:%a:%F", "--", Socket]);
                if (!File.Exists(Socket)) { throw new FileNotFoundException("The actual product endpoint departed during setup observation.", Socket); }
                Require(mode.ExitCode == 0 && mode.StandardOutput.Trim() == "0:777:socket", "The fixture could not establish the actual instrumented root endpoint as socket0777.");
                Save("target-socket.json", new { socket = Socket, stat = mode.StandardOutput.Trim(), pipe = TargetPipe });
                TargetSocketPrepared = true;
            }
            catch (Exception exception)
            {
                if (File.Exists(Socket)) { MarkInfrastructure(exception); }
                throw;
            }
        }
        internal JsonObject ObserveForeignTarget()
        {
            try
            {
                File.WriteAllText(Path.Combine(helperDirectories["foreign-client"], "target-go"), "go");
                TargetExchangeRequested = true;
                return WaitForObjectAsync(Path.Combine(helperDirectories["foreign-client"], "target.json"), helpers["foreign-client"]).GetAwaiter().GetResult();
            }
            catch (Exception exception) { MarkInfrastructure(exception); throw; }
        }
        internal bool Probe(string role, string pipe)
        {
            ProcessResult result;
            try
            {
                result = Run(dotnet, ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", Text(manifest, "probe"), "--pipe", pipe, "--timeout-ms", "1000"]);
                Require(result.ExitCode is 0 or 1 or 2, "The immutable probe process did not return a contracted health/probe result.");
            }
            catch (Exception exception) { MarkInfrastructure(exception); throw; }
            Save(role + ".json", new { result.ProcessId, result.ExitCode, result.OutputDrained, result.StandardOutput, result.StandardError });
            return result.ExitCode == 0 && result.StandardError.Length == 0 && (string?)JsonNode.Parse(result.StandardOutput)?["healthStatus"] == "Healthy";
        }
        private void Start(string role, bool nobody, string[] arguments)
        {
            string directory = Path.Combine(Attempt, role); Directory.CreateDirectory(directory);
            if (OperatingSystem.IsLinux()) { File.SetUnixFileMode(directory, (UnixFileMode)511); }
            helperDirectories.Add(role, directory);
            string[] command = ["-c", "umask 000; exec \"$@\"", "cis-owner-recovery", dotnet, "exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", host, .. arguments, directory];
            helpers.Add(role, ProcessRunner.RunAsync(nobody ? runuser : "/bin/sh", nobody ? ["-u", "nobody", "--", "/bin/sh", .. command] : command,
                Path.GetDirectoryName(host)!, TimeSpan.FromSeconds(35), childEnvironment, maximumCapturedBytesPerStream: 16384, cancellationToken: helperCancellation.Token));
        }
        private JsonObject Ready(string role, string uid)
        {
            JsonObject value = WaitForObjectAsync(Path.Combine(helperDirectories[role], "ready.json"), helpers[role]).GetAwaiter().GetResult();
            int pid = (int)value["pid"]!;
            Require(Uids(pid).All(actual => actual == uid) && (string?)value["runtime"] == ".NET 10.0.12"
                && (string?)value["fixtureSha256"] == Hash(host) && (string?)value["coreSha256"] == Text(manifest, "coreSha256")
                && (string?)value["pipesSha256"] == Text(manifest, "pipesSha256") && SamePath((string)value["temporaryRoot"]!, Path.GetTempPath()), "The actual recovery peer UID/runtime/origin differs from the immutable context.");
            Save(role + "-proc.json", new { uidLine = File.ReadLines("/proc/" + pid + "/status").Single(line => line.StartsWith("Uid:", StringComparison.Ordinal)), pid });
            return value;
        }
        private ProcessResult Run(string executable, string[] arguments)
        {
            ProcessResult result = ProcessRunner.RunAsync(executable, arguments, Path.GetDirectoryName(host)!, TimeSpan.FromSeconds(5), childEnvironment, maximumCapturedBytesPerStream: 16384).GetAwaiter().GetResult();
            Require(result.OutputDrained, "An immutable recovery helper did not drain its output.");
            return result;
        }
        private void Stop(string role)
        {
            File.WriteAllText(Path.Combine(helperDirectories[role], "stop"), "stop");
            ProcessResult result = helpers[role].WaitAsync(Limit).GetAwaiter().GetResult();
            JsonObject terminal = Read(Path.Combine(helperDirectories[role], "terminal.json"));
            Save(role + "-process.json", new { result.ProcessId, result.ExitCode, result.OutputDrained, result.StandardOutput, result.StandardError });
            Require(result.ExitCode == 0 && result.OutputDrained && result.StandardError.Length == 0 && (bool?)terminal["healthy"] == true
                && !Directory.Exists("/proc/" + (int)terminal["pid"]!), "An actual recovery helper did not terminate and drain cleanly.");
        }
        internal void StopHelpers()
        {
            foreach (string role in helpers.Keys)
            {
                try { Stop(role); }
                catch (Exception exception) { MarkInfrastructure(exception); }
            }
            if (helpers.Values.Any(task => !task.IsCompleted))
            {
                helperCancellation.Cancel();
                foreach (Task<ProcessResult> task in helpers.Values)
                { try { task.WaitAsync(TimeSpan.FromSeconds(6)).GetAwaiter().GetResult(); } catch (Exception exception) { MarkInfrastructure(exception); } }
            }
            AllHelpersStopped = helpers.Values.All(task => task.IsCompleted);
        }
        internal void MarkInfrastructure(Exception exception)
        {
            Infrastructure = Infrastructure is null ? exception : new AggregateException(Infrastructure, exception);
            Save("failure.json", new { schema = "cis.healthcheck.foreign-owner-failure/v1", role = "server-recovery", failure = Infrastructure.ToString() });
        }
        internal void Save(string name, object value) => File.WriteAllText(Path.Combine(Attempt, name), JsonSerializer.Serialize(value));
        public void Dispose() => helperCancellation.Dispose();
    }

    private static async Task<JsonObject> WaitForObjectAsync(string path, Task<ProcessResult> process)
    {
        var clock = Stopwatch.StartNew();
        while (!File.Exists(path))
        {
            Require(!process.IsCompleted && clock.Elapsed < Limit, "The bounded foreign-peer ready/progress receipt is missing.");
            await Task.Delay(10);
        }
        return Read(path);
    }
    private static async Task<JsonObject> WaitForCountersAsync(string path, int requests, Task<ProcessResult> process)
    {
        var clock = Stopwatch.StartNew();
        while (true)
        {
            JsonObject value = await WaitForObjectAsync(path, process);
            if ((int?)value["responses"] == requests) { return value; }
            Require(clock.Elapsed < Limit && !process.IsCompleted, "The positive fixture exchanges did not complete.");
            await Task.Delay(10);
        }
    }
    private static string[] Uids(int pid) => File.ReadLines("/proc/" + pid + "/status").Single(line => line.StartsWith("Uid:", StringComparison.Ordinal)).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[1..];
    private static bool SamePath(string first, string second) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)) == Path.TrimEndingDirectorySeparator(Path.GetFullPath(second));
    private static string Text(JsonObject value, string name) => (string?)value[name] ?? throw new InvalidDataException("Missing foreign-owner input: " + name);
    private static JsonObject Read(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static void Require(bool condition, string message) { if (!condition) { throw new InvalidDataException(message); } }
}
