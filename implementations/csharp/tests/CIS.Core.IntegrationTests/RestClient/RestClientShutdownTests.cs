using CIS.Heartbeat;
using CIS.RestClient;
using CIS.Tests.UnitTests.RestClient;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.RestClient;

[TestClass]
public sealed class RestClientShutdownTests
{
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task OrderedHostStopCancelsRestBeforeWaitingForHeartbeatAndKeepsDependenciesThroughCleanup(bool startup, bool metricsFirst)
    {
        await using var server = await RestHttpLocalServer.StartAsync(context => context.Response.WriteAsync("done", context.RequestAborted));
        var dependency = new Dependency();
        var cleanup = new HeldCleanupHandler(dependency);
        var options = new RestClientOptions("catalog") { ResponseFormat = RestResponseFormat.Text };
        options.Handlers.Add(cleanup);
        var subscriber = new Poller();
        Action<IServiceCollection> configure = services =>
        {
            services.AddSingleton(_ => dependency);
            services.AddSingleton<IHostedService>(root => new DependencyLifetime(root.GetRequiredService<Dependency>()));
        };
        await using (var host = startup
            ? RestClientMetricsHost.Create(options, metricsFirst, configure, heartbeat: true, startup: true, restLast: metricsFirst)
            : RestClientMetricsHost.CreateHeartbeatHost(options, metricsFirst, configure, restLast: metricsFirst))
        {
            HeartbeatOwner? owner = null; Task? stop = null;
            try
            {
                HeartbeatRuntime heartbeat = host.Services.GetRequiredService<HeartbeatRuntime>();
                owner = heartbeat.CreateOwner();
                subscriber.Bind(host.Rest(), server.Address);
                owner.Service.Register(subscriber, new() { Interval = TimeSpan.FromMilliseconds(1) });
                await host.StartAsync();
                await cleanup.Entered.Task.WaitAsync(RestHttpTestSupport.Deadline);
                if (startup)
                {
                    string[] held = await host.ScrapeAsync();
                    Assert.AreEqual(1d, RestClientMetricSamples.Sum(held, "cis_restclient_active_operations"));
                    Assert.AreEqual(0d, RestClientMetricSamples.Sum(held, "cis_restclient_operation_duration_seconds_count"));
                }
                stop = host.StopAsync(new CancellationToken(canceled: true));
                await cleanup.CancellationObserved.Task.WaitAsync(RestHttpTestSupport.Deadline);
                Assert.IsFalse(stop.IsCompleted, "A canceled host wait is not actual REST/HB cleanup settlement.");
                Assert.IsFalse(dependency.Stopping.Task.IsCompleted, "Earlier dependent lifecycle teardown must remain behind the bridge.");
                Assert.AreEqual(0, dependency.Disposals);
                Assert.IsFalse(subscriber.ExecutionToken.IsCancellationRequested, "Heartbeat shutdown does not cancel execution tokens; REST owns this cutoff.");
                RestResult<string> rejected = await host.Rest().Get<string>(server.Address).WaitAsync(RestHttpTestSupport.Deadline);
                Assert.IsFalse(rejected.Success); Assert.AreEqual(RestFailureKind.Stopping, rejected.Context.FailureKind);
                Assert.AreEqual(0, rejected.Context.AttemptCount);
                cleanup.Release.TrySetResult();
                await RestClientMetricsHost.ObserveStopAsync(stop);
                RestResult<string> result = await subscriber.Result.Task.WaitAsync(RestHttpTestSupport.Deadline);
                Assert.IsFalse(result.Success); Assert.AreEqual(RestFailureKind.Stopping, result.Context.FailureKind);
                Assert.IsTrue(cleanup.DependencyWasUsable);
                Assert.AreEqual(1, cleanup.Disposals);
                Assert.AreEqual(1, subscriber.Calls); Assert.AreEqual(0, subscriber.Errors);
                Assert.IsTrue(dependency.Stopping.Task.IsCompleted);
                Assert.AreEqual(0, dependency.Disposals, "Stop must drain; actual root disposal still owns service disposal.");
            }
            finally
            {
                cleanup.Release.TrySetResult();
                await RestClientMetricsHost.ObserveStopAsync(stop ?? host.StopAsync(CancellationToken.None));
                if (owner is not null) { await owner.DisposeAsync().AsTask().WaitAsync(RestHttpTestSupport.Deadline); }
                GC.KeepAlive(subscriber);
            }
        }
        Assert.AreEqual(1, dependency.Disposals);
    }

    private sealed class Poller : IHeartbeatSubscriber
    {
        private IRestClient client = null!; private string address = string.Empty;
        private int calls, errors;
        internal TaskCompletionSource<RestResult<string>> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal CancellationToken ExecutionToken { get; private set; }
        internal int Calls => Volatile.Read(ref calls);
        internal int Errors => Volatile.Read(ref errors);
        internal void Bind(IRestClient client, string address) { this.client = client; this.address = address; }
        public async Task<bool> OnElapsedAsync(CancellationToken token)
        {
            ExecutionToken = token; Interlocked.Increment(ref calls);
            RestResult<string> result = await client.Get<string>(address, cancellationToken: token);
            Result.TrySetResult(result); return result.Success;
        }
        public Task OnErrorAsync(Exception error) { Interlocked.Increment(ref errors); Result.TrySetException(error); return Task.CompletedTask; }
    }

    private sealed class Dependency : IDisposable
    {
        private int disposals;
        internal TaskCompletionSource Stopping { get; } = RestHttpTestSupport.Signal();
        internal int Disposals => Volatile.Read(ref disposals);
        internal void Use()
        {
            if (Disposals != 0 || Stopping.Task.IsCompleted) { throw new InvalidOperationException("A dependency was torn down before owned cleanup settled."); }
        }
        public void Dispose() => Interlocked.Increment(ref disposals);
    }

    private sealed class DependencyLifetime(Dependency dependency) : IHostedLifecycleService
    {
        public Task StartingAsync(CancellationToken token) => Task.CompletedTask;
        public Task StartAsync(CancellationToken token) => Task.CompletedTask;
        public Task StartedAsync(CancellationToken token) => Task.CompletedTask;
        public Task StoppingAsync(CancellationToken token) { dependency.Stopping.TrySetResult(); return Task.CompletedTask; }
        public Task StopAsync(CancellationToken token) => Task.CompletedTask;
        public Task StoppedAsync(CancellationToken token) => Task.CompletedTask;
    }

    private sealed class HeldCleanupHandler(Dependency dependency) : IRestExchangeHandler
    {
        internal TaskCompletionSource Entered { get; } = RestHttpTestSupport.Signal();
        internal TaskCompletionSource Release { get; } = RestHttpTestSupport.Signal();
        internal TaskCompletionSource CancellationObserved { get; } = RestHttpTestSupport.Signal();
        private int disposals;
        internal int Disposals => Volatile.Read(ref disposals);
        internal bool DependencyWasUsable { get; private set; }
        private void UseDependency() { dependency.Use(); DependencyWasUsable = true; }
        public async ValueTask<IRestResponse> HandleAsync(RestRequestContext request, RestExchange next, CancellationToken token)
        {
            CancellationTokenRegistration registration = token.UnsafeRegister(_ => CancellationObserved.TrySetResult(), null);
            try { return new HeldResponse(await next(request, token), this, registration); }
            catch { registration.Dispose(); throw; }
        }
        private sealed class HeldResponse(IRestResponse inner, HeldCleanupHandler owner, CancellationTokenRegistration registration) : IRestResponse
        {
            public int HttpStatusCode => inner.HttpStatusCode;
            public IReadOnlyDictionary<string, IReadOnlyList<string>> Headers => inner.Headers;
            public Stream Body => inner.Body;
            public async ValueTask DisposeAsync()
            {
                Interlocked.Increment(ref owner.disposals); owner.Entered.TrySetResult();
                try { await owner.Release.Task; owner.UseDependency(); }
                finally { registration.Dispose(); }
                // The exchange pipeline owns the next response separately; this wrapper borrows it.
            }
        }
    }
}
