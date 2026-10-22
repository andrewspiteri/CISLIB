using System.IO.Pipes;
using System.Text;
using CIS.Healthcheck;
using CIS.Healthcheck.Pipes;
using CIS.Healthcheck.Reporting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthPipeStartupCompletionQualificationTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);

    [TestMethod]
    public async Task AnActualSuccessfulSingleSlotExchangeConfirmsTheSavedStartCompletion()
    {
        var service = new HealthcheckService(new(), new HealthApplicationIdentity { Name = "startup-proof", InstanceId = Guid.NewGuid() });
        service.MarkStartupCompleted();
        var source = new CountingSource(service);
        string pipe = "hc-start-" + Guid.NewGuid().ToString("N");
        var server = new HealthPipeServer(source, new HealthcheckOptions
        {
            PipeEnabled = true,
            PipeName = pipe,
            PipeMaxClients = 1,
            PipeRequestTimeoutMs = 1000
        });
        using var cancellation = new CancellationTokenSource(Deadline);
        try
        {
            HealthSnapshot before = service.CaptureSnapshot();
            Task started = server.StartAsync();
            // Do not await this task before the independent real milestone:
            // omission of TrySetResult must reach an assertion, not an await timeout.
            using (var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
            {
                await client.ConnectAsync(cancellation.Token);
                await HealthReportProtocol.WriteFrameAsync(client,
                    Encoding.UTF8.GetBytes("{\"protocolVersion\":1,\"view\":\"health\"}"), 512, cancellation.Token);
                byte[] reply = await HealthReportProtocol.ReadFrameAsync(client, 4194304, cancellation.Token);
                HealthParsedResponse parsed = HealthReportProtocol.ParseResponse(reply, HealthReportView.Health, 4194304);
                Assert.IsFalse(parsed.IsError);
                Assert.IsTrue(parsed.SelectedHealthy);
            }
            Assert.AreEqual(1, source.Captures);
            // With one actual slot, a complete valid response demonstrates that
            // the sole endpoint was published and its worker admitted. This
            // checks successful startup notification after real work, rather
            // than promising synchronous completion for arbitrary Start calls.
            Assert.IsTrue(started.IsCompletedSuccessfully, "A successful actual exchange must not leave its startup notification pending.");
            HealthSnapshot after = service.CaptureSnapshot();
            Assert.AreEqual(before.Revision, after.Revision);
            Assert.AreEqual(before.HealthStatus, after.HealthStatus);
            Assert.AreEqual(before.Ready, after.Ready);
            Assert.AreEqual(before.Application.IsStopping, after.Application.IsStopping);
        }
        finally
        {
            // The saved startup task is deliberately not joined: the exact
            // omission leaves only that notification pending, not owned work.
            try
            {
                try { await server.StopAsync().WaitAsync(Deadline); }
                finally { await server.DisposeAsync().AsTask().WaitAsync(Deadline); }
            }
            finally { await service.DisposeAsync().AsTask().WaitAsync(Deadline); }
        }
    }

    private sealed class CountingSource(HealthcheckService service) : IHealthSnapshotSource
    {
        private int captures;
        internal int Captures => Volatile.Read(ref captures);
        public HealthSnapshot CaptureSnapshot() { Interlocked.Increment(ref captures); return service.CaptureSnapshot(); }
    }
}
