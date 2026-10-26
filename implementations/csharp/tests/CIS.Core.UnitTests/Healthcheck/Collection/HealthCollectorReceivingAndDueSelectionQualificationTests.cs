using System.Threading.Channels;
using CIS.Healthcheck;
using CIS.Healthcheck.Internal;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthCollectorReceivingAndDueSelectionQualificationTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);
    private static readonly DateTimeOffset Epoch = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task CopyingALocalMonitorIdentityDoesNotTransferTheRegisteredHandlesOwnership()
    {
        await using var scope = new Scope();
        IConsecutiveFailureMonitor monitor = scope.CreateMonitor("local");
        var owner = scope.CreateOwner();
        var foreignView = new PublicMonitorView(monitor);
        HealthSnapshot before = scope.Service.CaptureSnapshot();

        Assert.ThrowsExactly<ArgumentException>(() =>
        {
            // This valid public interface implementation deliberately copies a
            // real local identity. A second ID lookup cannot establish that the
            // supplied object is the registered handle belonging to the service.
            _ = scope.Runner.Register(foreignView, owner, new());
        });

        HealthSnapshot after = scope.Service.CaptureSnapshot();
        Assert.AreEqual(before.Revision, after.Revision);
        Assert.IsNull(after.Monitors.Single().Collection);
        Assert.AreEqual(0, owner.Acquisitions);

        IHealthCollectionRegistration actual = scope.Runner.Register(monitor, owner, new());
        await scope.Runner.StartAsync().WaitAsync(Deadline);
        Task<HealthCollectionAttempt> collecting = actual.CollectAsync();
        Invocation invocation = await owner.NextAsync(collecting);
        Task<HealthCollectionAttempt> operation = actual.CollectAsync();
        invocation.Release();
        await operation.WaitAsync(Deadline);
        long successful = scope.Snapshot(monitor).Outcomes!.Successful;
        await scope.DisposeAsync();
        Assert.IsTrue(collecting.IsCompleted,
            "The queued caller must receive completion after its actual operation and resources have settled.");
        Assert.AreEqual(HealthCollectionDisposition.Committed, (await collecting).Disposition);
        Assert.AreEqual(1L, successful);
    }

    [TestMethod]
    public async Task ApplicationStoppingRejectsNewStateAttachmentsWhileFinalDirectReportsRemainAvailable()
    {
        await using var scope = new Scope();
        IConsecutiveFailureMonitor monitor = scope.CreateMonitor("final reports");
        scope.Service.BeginStopping();
        HealthSnapshot before = scope.Service.CaptureSnapshot();

        Assert.ThrowsExactly<InvalidOperationException>(() => scope.State.RegisterCollection(
            monitor.MonitorId, Guid.NewGuid(), new HealthCollectorOptions()));

        HealthSnapshot rejected = scope.Service.CaptureSnapshot();
        Assert.AreEqual(before.Revision, rejected.Revision);
        Assert.IsNull(rejected.Monitors.Single().Collection);
        Assert.IsTrue(rejected.Application.IsStopping);
        monitor.ReportSuccess();
        Assert.AreEqual(MonitorState.Healthy, scope.Snapshot(monitor).State);
        Assert.AreEqual(1L, scope.Snapshot(monitor).Outcomes!.Successful);
    }

    [TestMethod]
    [DataRow("reserve")]
    [DataRow("withdraw")]
    public async Task ARejectedNullCollectionTargetIdentifiesTheReceivingArgumentWithoutChangingTheAttachment(string operation)
    {
        await using var scope = new Scope();
        IConsecutiveFailureMonitor monitor = scope.CreateMonitor("receiving target");
        HealthCollectionTarget target = scope.State.RegisterCollection(monitor.MonitorId, Guid.NewGuid(), new());
        HealthSnapshot before = scope.Service.CaptureSnapshot();

        ArgumentNullException rejected = operation == "reserve"
            ? Assert.ThrowsExactly<ArgumentNullException>(() => scope.State.TryReserve(null!))
            : Assert.ThrowsExactly<ArgumentNullException>(() => scope.State.WithdrawCollection(null!));

        // Check the API argument identity, not framework/localized prose. The
        // coordinator must identify its own target input rather than expose a
        // ConditionalWeakTable's unrelated internal key parameter.
        Assert.AreEqual("target", rejected.ParamName);
        Assert.AreEqual(before.Revision, scope.Service.CaptureSnapshot().Revision);
        Assert.IsFalse(target.Retirement.IsCancellationRequested);
        await scope.State.WithdrawCollection(target).WaitAsync(Deadline);
        Assert.AreEqual(HealthCollectionReservationDisposition.Retired, scope.State.TryReserve(target).Disposition);
    }

    // A public adapter can expose a monitor's values and forward its operations;
    // it does not implement or fabricate the library's internal ownership seam.
    private sealed class PublicMonitorView(IHealthMonitor source) : IHealthMonitor
    {
        public Guid MonitorId => source.MonitorId;
        public string? Name => source.Name;
        public bool IsCritical => source.IsCritical;
        public void ReportMeasurement(HealthMeasurement measurement) => source.ReportMeasurement(measurement);
        public void Withdraw() => source.Withdraw();
        public Task WaitForDisposalAsync(CancellationToken waitCancellation = default) => source.WaitForDisposalAsync(waitCancellation);
        public ValueTask DisposeAsync() => source.DisposeAsync();
    }

    private sealed class Scope : IAsyncDisposable
    {
        private readonly List<IConsecutiveFailureMonitor> monitors = [];
        private readonly List<GatedOwner> owners = [];
        internal Scope()
        {
            Service = new(new() { MaxConcurrentCollections = 1 },
                new HealthApplicationIdentity { Name = "collector receiving and due boundaries", InstanceId = Guid.NewGuid() }, Clock);
            Runner = new(Service);
        }
        internal ManualTimeProvider Clock { get; } = new(Epoch);
        internal HealthcheckService Service { get; }
        internal HealthCollectorRunner Runner { get; }
        internal IHealthCollectionState State => ((IHealthStateAccess)Service).CollectionState;
        internal IConsecutiveFailureMonitor CreateMonitor(string name)
        {
            IConsecutiveFailureMonitor monitor = Service.CreateConsecutiveFailures(new()
            {
                IsCritical = true,
                FailureThreshold = 1,
                Name = name
            });
            monitors.Add(monitor);
            return monitor;
        }
        internal GatedOwner CreateOwner()
        {
            var owner = new GatedOwner();
            owners.Add(owner);
            return owner;
        }
        internal HealthMonitorSnapshot Snapshot(IHealthMonitor monitor) => Service.CaptureSnapshot().Monitors
            .Single(item => item.MonitorId == monitor.MonitorId);
        public async ValueTask DisposeAsync()
        {
            // Release also marks the owner so a concurrently admitted real lease
            // is immediately releasable. Failure never strands a gated callback.
            foreach (GatedOwner owner in owners) { owner.ReleaseAll(); }
            try { await Runner.DisposeAsync().AsTask().WaitAsync(Deadline); }
            finally
            {
                try { await Service.DisposeAsync().AsTask().WaitAsync(Deadline); }
                finally { GC.KeepAlive(monitors); }
            }
        }
    }

    private sealed class Invocation
    {
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task Released => release.Task;
        internal void Release() => release.TrySetResult();
    }

    private sealed class GatedOwner : IHealthCollector
    {
        private readonly object gate = new();
        private readonly List<Invocation> invocations = [];
        private readonly Channel<Invocation> entered = Channel.CreateUnbounded<Invocation>();
        private bool releasing;
        private int acquisitions;
        internal int Acquisitions => Volatile.Read(ref acquisitions);
        public bool TryAcquire(out IHealthCollectionLease? lease)
        {
            Interlocked.Increment(ref acquisitions);
            lease = new Lease(this);
            return true;
        }
        internal async Task<Invocation> NextAsync(Task<HealthCollectionAttempt> actualCaller)
        {
            using var entryWait = new CancellationTokenSource();
            Task<Invocation> entry = entered.Reader.ReadAsync(entryWait.Token).AsTask();
            try
            {
                await Task.WhenAny(entry, actualCaller).WaitAsync(Deadline);
                Assert.IsFalse(actualCaller.IsCompleted, "The actual collection cannot settle before its expected blocked callback enters.");
                return await entry.WaitAsync(Deadline);
            }
            finally { entryWait.Cancel(); }
        }
        internal void ReleaseAll()
        {
            lock (gate)
            {
                releasing = true;
                foreach (Invocation invocation in invocations) { invocation.Release(); }
            }
        }
        private async Task<HealthCollectionResult> EvaluateAsync(CancellationToken cancellation)
        {
            var invocation = new Invocation();
            lock (gate)
            {
                invocations.Add(invocation);
                if (releasing) { invocation.Release(); }
            }
            entered.Writer.TryWrite(invocation);
            await invocation.Released;
            cancellation.ThrowIfCancellationRequested();
            return HealthCollectionResult.Execution(HealthExecutionOutcome.Succeeded);
        }
        private sealed class Lease(GatedOwner owner) : IHealthCollectionLease
        {
            public Task<HealthCollectionResult> CollectAsync(CancellationToken lifetimeCancellation) => owner.EvaluateAsync(lifetimeCancellation);
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
