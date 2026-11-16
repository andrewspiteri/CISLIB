using CIS.Healthcheck;
using CIS.Heartbeat;
using CIS.Heartbeat.Healthcheck;
using CIS.Heartbeat.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.BusinessCases.Heartbeat;

[TestClass]
public sealed class HeartbeatPollingStories
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);

    [TestMethod]
    public async Task ApplicationRetainsOriginalRequestBudgetWhileRecoveringAndPublishingUsableData()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var expired = new Request("expired", now.AddSeconds(-10), now.AddSeconds(-5));
        var fresh = new Request("fresh", now.AddSeconds(-1), now.AddHours(1));
        var health = new HealthcheckService(new HealthcheckOptions());
        IAvailabilityMonitor monitor = health.CreateAvailability(new AvailabilityMonitorOptions { IsCritical = true, Name = "FX feed" });
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Logging.ClearProviders();
        builder.Services.AddScoped(_ => new Publisher());
        builder.Services.AddScoped(provider => new Poller(provider.GetRequiredService<Publisher>(), expired, fresh));
        builder.Services.AddCisHeartbeat(new HeartbeatOptions { MaxConcurrent = 1 });
        IHost host = builder.Build();
        HeartbeatRuntime runtime = host.Services.GetRequiredService<HeartbeatRuntime>();
        var scope = new HeartbeatScope(runtime, host.Services.GetRequiredService<IServiceScopeFactory>());
        Poller poller = scope.Services.GetRequiredService<Poller>();
        try
        {
            scope.Service.Register(poller, new HeartbeatRegistrationOptions { Interval = TimeSpan.FromMilliseconds(1), Name = "request queue" },
                new HeartbeatHealthcheckOptions { Monitor = monitor, SuccessPublishesUsableData = true }, poller.CanPollAsync);
            health.MarkStartupCompleted();
            await host.StartAsync().WaitAsync(Deadline);
            await poller.RecoveryEntered.Task.WaitAsync(Deadline);
            Assert.AreEqual(1, poller.ExpiredRejected, "The subscriber owns business expiry before calling its dependency.");
            Assert.AreEqual(2, poller.RecoveryInvocation, "An ordinary false result must not call OnError.");
            Assert.AreEqual(1, poller.Errors);
            HealthMonitorSnapshot waiting = health.CaptureSnapshot().Monitors.Single(item => item.MonitorId == monitor.MonitorId);
            Assert.AreEqual(1L, waiting.Outcomes!.Failed, "The first false result is recorded while later asynchronous recovery is still running.");
            poller.RecoveryRelease.TrySetResult();
            await poller.NextCondition.Task.WaitAsync(Deadline);

            // The next condition can start only after all three earlier executions have settled.
            HealthMonitorSnapshot recovered = health.CaptureSnapshot().Monitors.Single(item => item.MonitorId == monitor.MonitorId);
            Assert.AreEqual(2L, recovered.Outcomes!.Failed);
            Assert.AreEqual(1L, recovered.Outcomes.Successful);
            Assert.AreEqual(HealthExecutionOutcome.Succeeded, recovered.Outcomes.LastOutcome);
            Assert.AreEqual(MonitorState.Healthy, recovered.State);
            Assert.IsTrue(health.CaptureSnapshot().Ready);
            Assert.AreSame(fresh, poller.Publisher.Published);
            CollectionAssert.AreEqual(new[] { expired, fresh, fresh }, poller.Observed.ToArray());
            Assert.AreEqual(now.AddSeconds(-1), fresh.OriginatedAt);
            Assert.AreEqual(now.AddHours(1), fresh.ExpiresAt);
            Assert.AreEqual(1, poller.Errors, "The application retained and retried its pending request; Heartbeat supplied only recurring invocations.");
            Assert.IsFalse(runtime.InfrastructureFailure.IsCompleted);

            Task scopeDisposal = scope.DisposeAsync().AsTask();
            Assert.IsFalse(scopeDisposal.IsCompleted, "Owner closure still waits for the accepted condition.");
            Assert.AreEqual(0, poller.Publisher.DisposalCalls);
            poller.ConditionRelease.TrySetResult();
            await scopeDisposal.WaitAsync(Deadline);
            Assert.AreEqual(1, poller.Publisher.DisposalCalls);
            Assert.AreEqual(1L, health.CaptureSnapshot().Monitors.Single().Outcomes!.Skipped);
        }
        finally
        {
            Task drain = runtime.StopAsync();
            poller.RecoveryRelease.TrySetResult();
            poller.ConditionRelease.TrySetResult();
            await drain.WaitAsync(Deadline);
            try
            {
                await host.StopAsync().WaitAsync(Deadline);
                await scope.DisposeAsync().AsTask().WaitAsync(Deadline);
                await monitor.DisposeAsync().AsTask().WaitAsync(Deadline);
                await health.DisposeAsync().AsTask().WaitAsync(Deadline);
            }
            finally
            {
                if (host is IAsyncDisposable asynchronous) { await asynchronous.DisposeAsync().AsTask().WaitAsync(Deadline); }
                else { host.Dispose(); }
            }
        }
    }

    private sealed record Request(string Id, DateTimeOffset OriginatedAt, DateTimeOffset ExpiresAt);

    private sealed class Poller(Publisher publisher, Request expired, Request fresh) : IHeartbeatSubscriber
    {
        internal readonly TaskCompletionSource RecoveryEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource RecoveryRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource NextCondition = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ConditionRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly List<Request> Observed = [];
        internal Publisher Publisher => publisher;
        internal int ExpiredRejected, RecoveryInvocation, Errors;
        private int invocations;
        private bool recovered;

        internal async Task<bool> CanPollAsync(CancellationToken cancellationToken)
        {
            if (invocations < 3) { return true; }
            NextCondition.TrySetResult();
            await ConditionRelease.Task.ConfigureAwait(false);
            return false;
        }

        public Task<bool> OnElapsedAsync(CancellationToken cancellationToken)
        {
            Request pending = ++invocations == 1 ? expired : fresh;
            Observed.Add(pending);
            if (DateTimeOffset.UtcNow >= pending.ExpiresAt) { ExpiredRejected++; return Task.FromResult(false); }
            // Keeping this same request pending is explicit application recovery policy.
            if (!recovered) { throw new InvalidOperationException("Temporary FX dependency failure."); }
            publisher.Publish(pending);
            return Task.FromResult(true);
        }

        public async Task OnErrorAsync(Exception exception)
        {
            Errors++;
            RecoveryInvocation = invocations;
            RecoveryEntered.TrySetResult();
            await RecoveryRelease.Task.ConfigureAwait(false);
            recovered = true;
        }
    }

    private sealed class Publisher : IAsyncDisposable
    {
        internal Request? Published;
        internal int DisposalCalls;
        internal void Publish(Request request)
        {
            ObjectDisposedException.ThrowIf(DisposalCalls != 0, this);
            Published = request;
        }
        public ValueTask DisposeAsync() { DisposalCalls++; return ValueTask.CompletedTask; }
    }
}
