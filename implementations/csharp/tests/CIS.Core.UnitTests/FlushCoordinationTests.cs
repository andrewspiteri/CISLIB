using System.Text.Json.Nodes;
using CIS.Logging.Runtime;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed partial class FlushCoordinationTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task EmptyOutputsStillHonorTheCallerLifetime(bool cancelled)
    {
        using var lifetime = new CancellationTokenSource(); if (cancelled) { lifetime.Cancel(); }
        var boundary = new RuntimeFlushBoundary(DateTimeOffset.UtcNow, 0, 0, null, null);
        if (cancelled)
        {
            try { await boundary.WaitForWriterFlushAsync(lifetime.Token); Assert.Fail("Expected cancellation."); }
            catch (OperationCanceledException error) { Assert.AreEqual(lifetime.Token, error.CancellationToken); }
        }
        else { Assert.IsTrue(await boundary.WaitForWriterFlushAsync(lifetime.Token)); }
    }

    [TestMethod]
    public async Task AFailedFirstOutputStillWaitsForTheIndependentFileFlush()
    {
        var console = new OutputFlushRegistration(1); console.Resolve(false);
        var file = new OutputFlushRegistration(2);
        var boundary = new RuntimeFlushBoundary(DateTimeOffset.UtcNow, 0, 0, new(1, 0, 0, 0, console), new(2, 0, 0, 0, file));
        using var lifetime = new CancellationTokenSource(); Task<bool> wait = boundary.WaitForWriterFlushAsync(lifetime.Token);
        Assert.IsFalse(wait.IsCompleted); Assert.AreEqual(1, FlushCoordinationFixture.Continuations(file.WriterFlushCompletion));
        file.Resolve(true); Assert.IsFalse(await wait.WaitAsync(Limit));
    }

    [TestMethod]
    public async Task CancellationOnTheFileWaitDoesNotRetainTheEarlierConsoleCaller()
    {
        var console = new OutputFlushRegistration(1); var file = new OutputFlushRegistration(2);
        var boundary = new RuntimeFlushBoundary(DateTimeOffset.UtcNow, 0, 0, new(1, 0, 0, 0, console), new(2, 0, 0, 0, file));
        using var lifetime = new CancellationTokenSource(); Task<bool> wait = boundary.WaitForWriterFlushAsync(lifetime.Token);
        Assert.AreEqual(1, FlushCoordinationFixture.Continuations(console.WriterFlushCompletion)); Assert.AreEqual(0, FlushCoordinationFixture.Continuations(file.WriterFlushCompletion));
        console.Resolve(true);
        using var guard = new CancellationTokenSource(Limit);
        while (FlushCoordinationFixture.Continuations(file.WriterFlushCompletion) != 1) { await Task.Delay(1, guard.Token); }
        lifetime.Cancel(); try { await wait.WaitAsync(Limit); Assert.Fail("Expected cancellation."); } catch (OperationCanceledException error) { Assert.AreEqual(lifetime.Token, error.CancellationToken); }
        Assert.AreEqual(0, FlushCoordinationFixture.Continuations(console.WriterFlushCompletion)); Assert.AreEqual(0, FlushCoordinationFixture.Continuations(file.WriterFlushCompletion));
        Assert.IsFalse(file.WriterFlushCompletion.IsCompleted); file.Resolve(true);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AlreadyCancelledLifetimeWinsEvenForAnAlreadyResolvedWriter(bool success)
    {
        var registration = new OutputFlushRegistration(1); registration.Resolve(success);
        using var lifetime = new CancellationTokenSource(); lifetime.Cancel();
        OperationCanceledException error = Assert.ThrowsExactly<OperationCanceledException>(() => registration.WaitForWriterFlushAsync(lifetime.Token));
        Assert.AreEqual(lifetime.Token, error.CancellationToken);
        Assert.AreEqual(success, await registration.WriterFlushCompletion);
    }

    [TestMethod]
    public async Task ACallerCanWaitAgainAfterItsOwnCancellation()
    {
        var registration = new OutputFlushRegistration(1); using var first = new CancellationTokenSource();
        Task<bool> cancelled = registration.WaitForWriterFlushAsync(first.Token); first.Cancel();
        try { await cancelled; Assert.Fail("Expected cancellation."); } catch (OperationCanceledException) { }
        Assert.AreEqual(0, FlushCoordinationFixture.Continuations(registration.WriterFlushCompletion));
        using var second = new CancellationTokenSource(); Task<bool> survivor = registration.WaitForWriterFlushAsync(second.Token);
        Assert.AreEqual(1, FlushCoordinationFixture.Continuations(registration.WriterFlushCompletion)); registration.Resolve(true);
        Assert.IsTrue(await survivor.WaitAsync(Limit));
    }
}
