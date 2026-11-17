using System.Diagnostics.Metrics;
using System.Net;
using CIS.Metrics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Metrics;

[TestClass]
public sealed class CollectionCallbackTests
{
    [TestMethod]
    public async Task ExpiredScrapeDoesNotCertifyCallbackSettlementAndHostStopKeepsItsOwnerUsable()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        await using var host = CollectionTestHost.Create(options => options.ScrapeTimeout = TimeSpan.FromMilliseconds(200));
        host.Release.Add(release.Set);
        bool ownerDisposed = false;
        bool ownerWasUsable = false;
        int calls = 0;
        host.Metrics.RegisterObservableGauge(new("app.callback.state"), () =>
        {
            Interlocked.Increment(ref calls);
            entered.Set();
            if (!release.Wait(CollectionTestHost.Deadline)) { throw new TimeoutException("Controlled callback release was not supplied."); }
            ownerWasUsable = !ownerDisposed;
            return [new(1)];
        });
        await host.StartAsync();
        Task<HttpResponseMessage> request = host.Client.GetAsync("/_metrics");
        Assert.IsTrue(await Task.Run(() => entered.Wait(CollectionTestHost.Deadline)));
        using HttpResponseMessage expired = await request.WaitAsync(CollectionTestHost.Deadline);
        Assert.AreEqual(HttpStatusCode.RequestTimeout, expired.StatusCode);
        Task stop = host.App.StopAsync(CancellationToken.None);
        Assert.IsTrue(host.Context.IsStopping);
        Assert.IsFalse(stop.IsCompleted, "The request expired, but actual callback settlement remains held.");
        Assert.IsFalse(ownerDisposed);
        release.Set();
        await stop.WaitAsync(CollectionTestHost.Deadline);
        ownerDisposed = true;
        Assert.AreEqual(1, calls);
        Assert.IsTrue(ownerWasUsable, "The actual callback must have finished while its owner remained usable.");

        // The borrowed meter can still be inspected before root disposal. A later external collection must not touch its owner.
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, source) =>
        {
            if (ReferenceEquals(instrument.Meter, host.Context.CisMeter)) { source.EnableMeasurementEvents(instrument); }
        };
        listener.SetMeasurementEventCallback<double>(static (_, _, _, _) => { });
        listener.Start();
        listener.RecordObservableInstruments();
        Assert.AreEqual(1, calls);
    }
}
