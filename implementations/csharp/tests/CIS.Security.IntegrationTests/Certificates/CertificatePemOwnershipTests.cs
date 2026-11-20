using CIS.CommonTesting;
using CIS.RestClient;
using CIS.RestClient.Resilience;
using CIS.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.Security.Certificates;

[TestClass]
[DoNotParallelize]
public sealed class CertificatePemOwnershipTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);

    [TestMethod]
    public async Task FileReloadRetainsTheOldBodyDefersTheThirdChangeAndAdoptsTheNewestAfterDrain()
    {
        using var certificates = new CertificateFixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using CertificateTestServer server = await CertificateTestServer.StartAsync(certificates, business: async context =>
        {
            await context.Response.WriteAsync(context.Connection.ClientCertificate!.Thumbprint + "|" + context.Connection.Id);
            await context.Response.Body.FlushAsync();
            if (context.Request.Query.ContainsKey("hold"))
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(context.RequestAborted);
            }
        });
        await using var source = new PemSourceFixture(certificates, $"https://localhost:{server.BusinessPort}");
        Task<RestResult<string>>? held = null;
        try
        {
            await source.Host.StartAsync();
            held = source.Client.Get<string>("/business?hold=1");
            await entered.Task.WaitAsync(Deadline);
            var second = certificates.Leaf(certificates.Root);
            source.Write(second);
            await source.PollAsync();
            RestResult<string> current = await source.SecondClient.Get<string>("/business");
            Assert.IsTrue(current.Success);
            string currentValue = current.Value ?? throw new AssertFailedException("The actual TLS peer response must contain its certificate and connection.");
            Assert.AreEqual(second.Thumbprint, currentValue.Split('|')[0]);
            int keys = SecurityTestAssemblySetup.NativeKeys.LiveKeyCount;
            var third = certificates.Leaf(certificates.Root);
            source.Write(third);
            await source.PollAsync();
            await source.PollAsync();
            Assert.AreEqual(keys, SecurityTestAssemblySetup.NativeKeys.LiveKeyCount,
                "A deferred changed file must not construct a third private key.");
            var newest = certificates.Leaf(certificates.Root);
            source.Write(newest);
            await source.PollAsync();
            release.TrySetResult();
            RestResult<string> old = await held.WaitAsync(Deadline);
            string oldValue = old.Value ?? throw new AssertFailedException("The held old TLS response must settle with its actual peer data.");
            Assert.AreEqual(source.Initial!.Thumbprint, oldValue.Split('|')[0]);
            Assert.AreNotEqual(oldValue.Split('|')[1], currentValue.Split('|')[1],
                "The adopted handler uses a distinct actual TLS connection.");
            await source.PollAsync();
            RestResult<string> final = await source.Client.Get<string>("/business");
            Assert.IsTrue(final.Success);
            string finalValue = final.Value ?? throw new AssertFailedException("The newest TLS generation must return its actual certificate.");
            Assert.AreEqual(newest.Thumbprint, finalValue.Split('|')[0]);
            Assert.AreEqual(3, source.Reloads.Count(value => value == SecurityCertificateReloadOutcome.Adopted));
            Assert.AreEqual(2, source.Reloads.Count(value => value == SecurityCertificateReloadOutcome.Deferred),
                "Each changed candidate is deferred once; the unchanged repeated poll is coalesced.");
        }
        finally
        {
            release.TrySetResult();
            if (held is not null)
            {
                await held.WaitAsync(Deadline);
            }
        }
    }

    [TestMethod]
    public async Task HostStoppingWaitsForActualResponseCleanupBeforeDisposingTheNativeCredential()
    {
        using var certificates = new CertificateFixture();
        await using CertificateTestServer server = await CertificateTestServer.StartAsync(certificates);
        int baseline = SecurityTestAssemblySetup.NativeKeys.LiveKeyCount;
        var held = new HeldCleanup();
        await using var source = new PemSourceFixture(certificates, $"https://localhost:{server.BusinessPort}",
            configureClient: options => options.Handlers.Add(held));
        Task<RestResult<string>>? call = null;
        Task? stop = null;
        try
        {
            await source.Host.StartAsync();
            ISecurityCredentialControl control = source.Host.Services.GetRequiredKeyedService<ISecurityCredentialControl>(PemSourceFixture.Profile);
            call = source.Client.Get<string>("/business");
            await held.Entered.Task.WaitAsync(Deadline);
            int active = SecurityTestAssemblySetup.NativeKeys.LiveKeyCount;
            stop = source.Host.StopAsync();
            Assert.IsFalse(stop.IsCompleted, "The native host stop must join the still-owned response cleanup.");
            Assert.AreEqual(active, SecurityTestAssemblySetup.NativeKeys.LiveKeyCount);
            held.Release.TrySetResult();
            await stop.WaitAsync(Deadline);
            RestResult<string> result = await call.WaitAsync(Deadline);
            Assert.AreEqual(RestFailureKind.Stopping, result.Context.FailureKind);
            await Assert.ThrowsExactlyAsync<ObjectDisposedException>(async () => await control.RequestSourceRecheckAsync());
            await source.DisposeAsync();
            Assert.AreEqual(baseline, SecurityTestAssemblySetup.NativeKeys.LiveKeyCount);
        }
        finally
        {
            held.Release.TrySetResult();
            if (stop is not null)
            {
                await stop.WaitAsync(Deadline);
            }
            if (call is not null)
            {
                await call.WaitAsync(Deadline);
            }
        }
    }

    [TestMethod]
    public async Task RealPollyRetrySelectsThePemGenerationAdoptedDuringItsExistingDelay()
    {
        using var certificates = new CertificateFixture();
        var received = new List<string>();
        int calls = 0;
        await using CertificateTestServer server = await CertificateTestServer.StartAsync(certificates, business: async context =>
        {
            lock (received)
            {
                received.Add(context.Connection.ClientCertificate!.Thumbprint);
            }
            context.Response.StatusCode = Interlocked.Increment(ref calls) == 1 ? 503 : 200;
            await context.Response.WriteAsync(context.Connection.ClientCertificate!.Thumbprint);
        });
        var clock = new DelayClock(certificates.Now);
        await using var source = new PemSourceFixture(certificates, $"https://localhost:{server.BusinessPort}",
            clock: clock, configure: services => services.AddCisRestClientResilience(new RestRetryPolicy("pem-retry", 1,
                TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), [503], backoff: RestRetryBackoff.Constant, useJitter: false)),
            configureClient: options => options.RetryPolicy = RestRetrySelection.Named("pem-retry"));
        await source.Host.StartAsync();
        Task<RestResult<string>> call = source.Client.Get<string>("/business");
        try
        {
            await clock.DelayEntered.Task.WaitAsync(Deadline);
            var replacement = certificates.Leaf(certificates.Root);
            source.Write(replacement);
            await source.PollAsync();
            clock.Advance(TimeSpan.FromSeconds(2));
            RestResult<string> result = await call.WaitAsync(Deadline);
            Assert.IsTrue(result.Success);
            Assert.AreEqual(2, result.Context.AttemptCount);
            CollectionAssert.AreEqual(new[] { source.Initial!.Thumbprint, replacement.Thumbprint }, received.ToArray());
        }
        finally
        {
            clock.Advance(TimeSpan.FromSeconds(3));
            await call.WaitAsync(Deadline);
        }
    }

    private sealed class DelayClock(DateTimeOffset now) : ManualTimeProvider(now)
    {
        internal TaskCompletionSource DelayEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            ITimer timer = base.CreateTimer(callback, state, dueTime, period);
            if (dueTime == TimeSpan.FromSeconds(2))
            {
                DelayEntered.TrySetResult();
            }
            return timer;
        }
    }

    private sealed class HeldCleanup : IRestExchangeHandler
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<IRestResponse> HandleAsync(RestRequestContext request, RestExchange next, CancellationToken cancellationToken)
            => new HeldResponse(await next(request, cancellationToken), this);

        private sealed class HeldResponse(IRestResponse response, HeldCleanup owner) : IRestResponse
        {
            public int HttpStatusCode => response.HttpStatusCode;
            public IReadOnlyDictionary<string, IReadOnlyList<string>> Headers => response.Headers;
            public Stream Body => response.Body;
            public async ValueTask DisposeAsync()
            {
                owner.Entered.TrySetResult();
                await owner.Release.Task;
                // The existing exchange pipeline separately owns its actual next response.
            }
        }
    }
}
