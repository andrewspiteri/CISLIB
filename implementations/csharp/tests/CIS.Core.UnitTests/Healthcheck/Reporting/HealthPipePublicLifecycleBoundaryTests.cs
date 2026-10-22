using System.IO.Pipes;
using CIS.Healthcheck;
using CIS.Healthcheck.Pipes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthPipePublicLifecycleBoundaryTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);

    [TestMethod]
    public async Task ActualFirstAndAdditionalEndpointsSupportAsynchronousDuplexTransport()
    {
        // Unix installs its accepted socket handle during connection. Windows
        // wraps its actual handle immediately, so keep the pre-I/O Windows
        // check before a wrongly synchronous handle could block the test.
        string pipe = "hc-async-" + Guid.NewGuid().ToString("N");
        using NamedPipeServerStream first = HealthPipeEndpoint.Create(pipe, maximumClients: 2, first: true);
        if (OperatingSystem.IsWindows()) { Assert.IsTrue(first.IsAsync, "The first actual Windows endpoint must support asynchronous I/O before acceptance."); }
        Assert.IsTrue(first.CanRead && first.CanWrite, "A request and its response share the actual duplex endpoint.");
        Assert.IsFalse(first.IsConnected);

        using var timeout = new CancellationTokenSource(Deadline);
        using var firstPeer = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var additionalPeer = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var ownedOperations = new List<Task>();
        NamedPipeServerStream? additional = null;
        try
        {
            Task firstAcceptance = first.WaitForConnectionAsync(timeout.Token);
            ownedOperations.Add(firstAcceptance);
            Task firstConnection = firstPeer.ConnectAsync(timeout.Token);
            ownedOperations.Add(firstConnection);
            await Task.WhenAll(firstAcceptance, firstConnection).WaitAsync(Deadline);
            Assert.IsTrue(first.IsConnected && firstPeer.IsConnected);
            Assert.IsTrue(first.IsAsync, "The first actual connected endpoint must support asynchronous I/O.");

            // A Windows client may attach to an instance before its accept is
            // posted. Create the second instance only after the first is truly
            // connected; keep that first connection occupied until both finish.
            additional = HealthPipeEndpoint.Create(pipe, maximumClients: 2, first: false);
            if (OperatingSystem.IsWindows()) { Assert.IsTrue(additional.IsAsync, "An additional actual Windows endpoint must support asynchronous I/O before acceptance."); }
            Assert.IsTrue(additional.CanRead && additional.CanWrite);
            Assert.IsFalse(additional.IsConnected);
            Task additionalAcceptance = additional.WaitForConnectionAsync(timeout.Token);
            ownedOperations.Add(additionalAcceptance);
            Task additionalConnection = additionalPeer.ConnectAsync(timeout.Token);
            ownedOperations.Add(additionalConnection);
            await Task.WhenAll(additionalAcceptance, additionalConnection).WaitAsync(Deadline);
            Assert.IsTrue(additional.IsConnected && additionalPeer.IsConnected);
            Assert.IsTrue(additional.IsAsync, "The additional actual connected endpoint must support asynchronous I/O.");

            await ExchangeAsync(first, firstPeer, 0x31, 0x71);
            await ExchangeAsync(additional, additionalPeer, 0x32, 0x72);
        }
        finally
        {
            try { timeout.Cancel(); }
            finally
            {
                firstPeer.Dispose();
                additionalPeer.Dispose();
                first.Dispose();
                additional?.Dispose();
                // Join real accepts/connects/I/O after closing their actual
                // streams. Preserve any cleanup failure; no detached work or
                // unbounded compiler-generated asynchronous disposal remains.
                await Task.WhenAll(ownedOperations).WaitAsync(Deadline);
            }
        }

        async Task ExchangeAsync(NamedPipeServerStream endpoint, NamedPipeClientStream peer, byte request, byte response)
        {
            byte[] received = new byte[1];
            Task receiving = endpoint.ReadExactlyAsync(received, timeout.Token).AsTask();
            ownedOperations.Add(receiving);
            Task sending = peer.WriteAsync(new byte[] { request }, timeout.Token).AsTask();
            ownedOperations.Add(sending);
            await Task.WhenAll(receiving, sending).WaitAsync(Deadline);
            Assert.AreEqual(request, received[0]);

            byte[] returned = new byte[1];
            Task readingReply = peer.ReadExactlyAsync(returned, timeout.Token).AsTask();
            ownedOperations.Add(readingReply);
            Task writingReply = endpoint.WriteAsync(new byte[] { response }, timeout.Token).AsTask();
            ownedOperations.Add(writingReply);
            await Task.WhenAll(readingReply, writingReply).WaitAsync(Deadline);
            Assert.AreEqual(response, returned[0]);
        }
    }

    [TestMethod]
    [DataRow("unstarted-disabled", "stop")]
    [DataRow("unstarted-enabled", "stop")]
    [DataRow("started-disabled", "stop")]
    [DataRow("unstarted-disabled", "dispose")]
    [DataRow("unstarted-enabled", "dispose")]
    [DataRow("started-disabled", "dispose")]
    public async Task TerminalCallsCloseAdmissionBeforeTheirReturnedWaitIsObserved(string stage, string terminal)
    {
        var service = new HealthcheckService(new(),
            new HealthApplicationIdentity { Name = "pipe lifecycle boundary", InstanceId = Guid.NewGuid() });
        IConditionMonitor monitor = service.CreateCondition(new() { IsCritical = true, InitiallyFlagged = false });
        service.MarkStartupCompleted();
        var source = new CountingSource(service);
        var server = new HealthPipeServer(source, new HealthcheckOptions
        {
            PipeEnabled = stage == "unstarted-enabled",
            PipeName = "hc-terminal-" + Guid.NewGuid().ToString("N")
        });
        Task? terminalWait = null;
        try
        {
            if (stage == "started-disabled")
            {
                await server.StartAsync().WaitAsync(Deadline);
                await server.StartAsync().WaitAsync(Deadline);
            }
            HealthSnapshot before = service.CaptureSnapshot();
            Assert.AreEqual(HealthStatus.Healthy, before.HealthStatus);
            Assert.IsTrue(before.Ready);
            Assert.AreEqual(0, source.Captures);

            terminalWait = terminal == "stop" ? server.StopAsync() : server.DisposeAsync().AsTask();

            // Admission closes as part of the terminal call, before its caller
            // awaits actual settlement. Awaiting first would conceal a missing
            // terminal transition behind an unrelated test-runner timeout.
            Assert.Throws<InvalidOperationException>(() => server.StartAsync());
            HealthSnapshot after = service.CaptureSnapshot();
            Assert.AreEqual(before.Revision, after.Revision);
            Assert.AreEqual(before.HealthStatus, after.HealthStatus);
            Assert.AreEqual(before.Ready, after.Ready);
            Assert.AreEqual(before.Application.StartupCompleted, after.Application.StartupCompleted);
            Assert.AreEqual(before.Application.IsStopping, after.Application.IsStopping);
            Assert.AreEqual(before.MonitorCounts.Active, after.MonitorCounts.Active);
            Assert.AreEqual(0, source.Captures, "Transport lifecycle cannot invoke the application snapshot source.");
            await terminalWait.WaitAsync(Deadline);

            // The transport owns its endpoint lifetime, not application health.
            monitor.Flag();
            Assert.AreEqual(HealthStatus.Unhealthy, service.CaptureSnapshot().HealthStatus);
            monitor.Clear();
            Assert.AreEqual(HealthStatus.Healthy, service.CaptureSnapshot().HealthStatus);
            Assert.IsTrue(service.CaptureSnapshot().Ready);
        }
        finally
        {
            try
            {
                // Use the other real terminal entry point first. A single
                // omitted Stop/Dispose transition must not strand cleanup on
                // the same broken wait that the direct assertion detected.
                Task alternate = terminal == "stop" ? server.DisposeAsync().AsTask() : server.StopAsync();
                await alternate.WaitAsync(Deadline);
                if (terminalWait is not null) { await terminalWait.WaitAsync(Deadline); }
            }
            finally
            {
                try { await server.DisposeAsync().AsTask().WaitAsync(Deadline); }
                finally
                {
                    await service.DisposeAsync().AsTask().WaitAsync(Deadline);
                    GC.KeepAlive(monitor);
                }
            }
        }
    }

    private sealed class CountingSource(HealthcheckService service) : IHealthSnapshotSource
    {
        private int captures;
        internal int Captures => Volatile.Read(ref captures);
        public HealthSnapshot CaptureSnapshot()
        {
            Interlocked.Increment(ref captures);
            return service.CaptureSnapshot();
        }
    }
}
