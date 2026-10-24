using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Text;
using CIS.Healthcheck;
using CIS.Healthcheck.Pipes;
using CIS.Healthcheck.Reporting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed partial class HealthPipeStartupCompletionQualificationTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);

    [TestMethod]
    public void AnActualSuccessfulSingleSlotExchangeConfirmsTheSavedStartCompletion()
    {
        ReviewSavedStartOwners(RunSavedStartScenario());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static SavedStartObservation RunSavedStartScenario()
    {
        var service = new HealthcheckService(new(), new HealthApplicationIdentity { Name = "startup-proof", InstanceId = Guid.NewGuid() });
        service.MarkStartupCompleted();
        var source = new CountingSource(service);
        var clock = new SavedStartBudgetClock();
        string pipe = "hc-start-" + Guid.NewGuid().ToString("N");
        var options = new HealthcheckOptions
        {
            PipeEnabled = true,
            PipeName = pipe,
            PipeMaxClients = 1,
            PipeRequestTimeoutMs = 1000
        };
        HealthPipeServer? server = null;
        var cancellation = new CancellationTokenSource(Deadline);
        Task? exchange = null, savedStart = null;
        Exception? failure = null;
        try
        {
            HealthSnapshot before = service.CaptureSnapshot();
            StartedOwner owned = CreateServerWithUnjoinedStartup(source, options, clock);
            server = owned.Server;
            Task started = owned.Started;
            savedStart = started;
            // Do not await this task before the independent real milestone:
            // omission of TrySetResult must reach an assertion, not an await timeout.
            exchange = ExchangeAsync(pipe, cancellation.Token);
            JoinSavedStart(exchange);
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
        catch (Exception error) { failure = error; }
        return ReleaseSavedStartOwners(server, service, source, clock, options, cancellation, exchange, savedStart, failure);
    }

    private static async Task ExchangeAsync(string pipe, CancellationToken cancellation)
    {
        using (var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
        {
            await client.ConnectAsync(cancellation);
            await HealthReportProtocol.WriteFrameAsync(client,
                Encoding.UTF8.GetBytes("{\"protocolVersion\":1,\"view\":\"health\"}"), 512, cancellation);
            byte[] reply = await HealthReportProtocol.ReadFrameAsync(client, 4194304, cancellation);
            HealthParsedResponse parsed = HealthReportProtocol.ParseResponse(reply, HealthReportView.Health, 4194304);
            Assert.IsFalse(parsed.IsError);
            Assert.IsTrue(parsed.SelectedHealthy);
        }
    }

    private sealed class CountingSource(HealthcheckService service) : IHealthSnapshotSource
    {
        internal SavedStartSourcePhase Phase { get; } = new();
        internal int Captures => Volatile.Read(ref Phase.Captures);
        public HealthSnapshot CaptureSnapshot()
        {
            Interlocked.Increment(ref Phase.Captures);
            try { return service.CaptureSnapshot(); }
            catch (Exception error) { Phase.RecordFailure(error); throw; }
            finally { Interlocked.Increment(ref Phase.Exits); }
        }
    }
}
