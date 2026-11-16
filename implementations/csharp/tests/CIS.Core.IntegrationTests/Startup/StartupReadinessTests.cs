using System.Net;
using CIS.Healthcheck;
using CIS.Startup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Startup;

[TestClass]
public sealed class StartupReadinessTests
{
    [TestMethod]
    public async Task BoundHttpAndRealPipeRemainNotReadyUntilSequentialStartupThenHeartbeatActivatesAutomatically()
    {
        var entered = StartupHost.Signal();
        var release = StartupHost.Signal();
        var callbackRelease = StartupHost.Signal();
        var order = new List<string>();
        var first = new StartupParticipant(async _ =>
        { order.Add("prime-entered"); entered.TrySetResult(); await release.Task; order.Add("prime-completed"); });
        var reader = new StartupParticipant(_ => { order.Add("reader"); return Task.CompletedTask; });
        await using var host = new StartupHost(services =>
        { services.AddSingleton<IStartable>(first); services.AddSingleton<IStartable>(reader); });
        host.Release.Add(() => release.TrySetResult());
        host.Release.Add(() => callbackRelease.TrySetResult());
        int conditions = 0;
        var subscriber = new StartupSubscriber(async _ => { await callbackRelease.Task; return true; });
        host.Owner.Service.Register(subscriber, StartupHost.Schedule, _ =>
        { Interlocked.Increment(ref conditions); return Task.FromResult(true); });
        await host.StartPipeAsync();

        Task start = host.StartAsync();
        await entered.Task.WaitAsync(StartupHost.Deadline);
        Assert.IsFalse(start.IsCompleted, "StartedAsync owns the held startup operation while Kestrel is already listening.");
        Assert.AreEqual(0, reader.Calls);
        await host.AssertStatusAsync("/_health", HttpStatusCode.OK);
        await host.AssertStatusAsync("/_health/ready", HttpStatusCode.InternalServerError);
        await host.AssertStatusAsync("/countries", HttpStatusCode.ServiceUnavailable);
        Assert.AreEqual(0, host.BusinessCalls);
        await host.AssertProbeAsync(1, ready: false, startupCompleted: false);
        Assert.AreEqual(0, Volatile.Read(ref conditions));
        Assert.AreEqual(0, subscriber.Calls);

        release.TrySetResult();
        await start.WaitAsync(StartupHost.Deadline);
        await subscriber.Entered.Task.WaitAsync(StartupHost.Deadline);
        Assert.IsFalse(callbackRelease.Task.IsCompleted, "Readiness must not await the first Heartbeat callback's success.");
        CollectionAssert.AreEqual(new[] { "prime-entered", "prime-completed", "reader" }, order);
        Assert.AreEqual(1, reader.Calls);
        Assert.AreEqual(1, Volatile.Read(ref conditions));
        Assert.AreEqual(1, subscriber.Calls);
        await host.AssertStatusAsync("/_health/ready", HttpStatusCode.OK);
        await host.AssertProbeAsync(0, ready: true, startupCompleted: true);
        await host.AssertStatusAsync("/countries", HttpStatusCode.OK);
        Assert.AreEqual(1, host.BusinessCalls);
        callbackRelease.TrySetResult();
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task ExistingCriticalityAndRecoveryChangeReadinessWithoutReplayingSuccessfulStartup(bool critical)
    {
        var participant = new StartupParticipant();
        await using var host = new StartupHost(services => services.AddSingleton<IStartable>(participant));
        await using IConditionMonitor condition = host.Health.CreateCondition(new()
        { IsCritical = critical, InitiallyFlagged = true, Name = "Application data" });
        await host.StartAsync().WaitAsync(StartupHost.Deadline);
        HealthSnapshot snapshot = host.Health.CaptureSnapshot();
        Assert.IsTrue(snapshot.Application.StartupCompleted, "Initialization completion does not wait for aggregate healthy state.");
        Assert.AreEqual(!critical, snapshot.Ready);
        await host.AssertStatusAsync("/_health/ready", critical ? HttpStatusCode.InternalServerError : HttpStatusCode.OK);
        condition.Clear();
        await host.AssertStatusAsync("/_health/ready", HttpStatusCode.OK);
        condition.Flag();
        await host.AssertStatusAsync("/_health/ready", critical ? HttpStatusCode.InternalServerError : HttpStatusCode.OK);
        condition.Clear();
        await host.AssertStatusAsync("/_health/ready", HttpStatusCode.OK);
        Assert.AreEqual(1, participant.Calls);
        Assert.AreEqual(2, host.Health.CaptureSnapshot().MonitorCounts.Active, "One retained startup condition and the application condition.");
    }

    [TestMethod]
    [DataRow("failure")]
    [DataRow("unexpected-cancellation")]
    [DataRow("host-cancellation")]
    public async Task StartupFailureAndKnownHostCancellationPreserveClosedAdmissionAndTheirActualHealthMeaning(string mode)
    {
        using var cancellation = new CancellationTokenSource();
        var entered = StartupHost.Signal();
        var release = StartupHost.Signal();
        Exception expected = mode == "unexpected-cancellation"
            ? new OperationCanceledException("No application shutdown was requested.")
            : new InvalidOperationException("Required priming failed.");
        var first = new StartupParticipant(async token =>
        {
            entered.TrySetResult();
            await release.Task;
            if (mode == "host-cancellation") { token.ThrowIfCancellationRequested(); }
            throw expected;
        });
        var later = new StartupParticipant();
        await using var host = new StartupHost(services =>
        { services.AddSingleton<IStartable>(first); services.AddSingleton<IStartable>(later); });
        host.Release.Add(() => release.TrySetResult());
        var subscriber = new StartupSubscriber();
        host.Owner.Service.Register(subscriber, StartupHost.Schedule);

        Task start = host.StartAsync(cancellation.Token);
        await entered.Task.WaitAsync(StartupHost.Deadline);
        if (mode == "host-cancellation") { cancellation.Cancel(); }
        Assert.IsFalse(start.IsCompleted, "Cancellation does not settle a held participant.");
        release.TrySetResult();
        Exception actual = await ObserveFailureAsync(start);
        if (mode == "host-cancellation") { Assert.IsInstanceOfType<OperationCanceledException>(actual); }
        else { Assert.AreSame(expected, actual); }
        HealthSnapshot snapshot = host.Health.CaptureSnapshot();
        Assert.IsFalse(snapshot.Application.StartupCompleted);
        Assert.IsFalse(snapshot.Ready);
        Assert.AreEqual(mode == "host-cancellation" ? 0 : 1, snapshot.MonitorCounts.CriticalFailing);
        if (mode != "host-cancellation") { Assert.AreEqual("Startup.Failed", snapshot.Monitors.Single().Diagnostic!.Code); }
        Assert.AreEqual(0, later.Calls);
        Assert.AreEqual(0, subscriber.Calls);
        await host.AssertStatusAsync("/_health/ready", HttpStatusCode.InternalServerError);
        await host.AssertStatusAsync("/countries", HttpStatusCode.ServiceUnavailable);
        Assert.AreEqual(0, host.BusinessCalls);
    }

    [TestMethod]
    public async Task AStoppedHeartbeatCannotBeMistakenForSuccessfulActivationAfterAllParticipantsSucceed()
    {
        var participant = new StartupParticipant();
        await using var host = new StartupHost(services => services.AddSingleton<IStartable>(participant));
        await host.Runtime.StopAsync().WaitAsync(StartupHost.Deadline);
        Exception failure = await ObserveFailureAsync(host.StartAsync());
        Assert.IsInstanceOfType<InvalidOperationException>(failure);
        Assert.AreEqual(1, participant.Calls);
        Assert.IsFalse(host.Health.CaptureSnapshot().Application.StartupCompleted);
        Assert.AreEqual(1, host.Health.CaptureSnapshot().MonitorCounts.CriticalFailing);
        await host.AssertStatusAsync("/_health/ready", HttpStatusCode.InternalServerError);
    }

    [TestMethod]
    public async Task StopApplicationFromTheFinalParticipantCannotReopenReadinessOrHeartbeatDispatch()
    {
        IHostApplicationLifetime? lifetime = null;
        var participant = new StartupParticipant(_ => { lifetime!.StopApplication(); return Task.CompletedTask; });
        await using var host = new StartupHost(services => services.AddSingleton<IStartable>(participant));
        lifetime = host.App.Services.GetRequiredService<IHostApplicationLifetime>();
        var subscriber = new StartupSubscriber();
        host.Owner.Service.Register(subscriber, StartupHost.Schedule);
        Assert.IsInstanceOfType<OperationCanceledException>(await ObserveFailureAsync(host.StartAsync()));
        Assert.AreEqual(1, participant.Calls);
        Assert.IsTrue(host.Health.CaptureSnapshot().Application.IsStopping);
        Assert.IsFalse(host.Health.CaptureSnapshot().Application.StartupCompleted);
        Assert.AreEqual(0, host.Health.CaptureSnapshot().MonitorCounts.CriticalFailing);
        Assert.AreEqual(0, subscriber.Calls);
        await host.AssertStatusAsync("/_health/ready", HttpStatusCode.InternalServerError);
        await host.AssertStatusAsync("/countries", HttpStatusCode.ServiceUnavailable);
        Assert.AreEqual(0, host.BusinessCalls);
    }

    private static async Task<Exception> ObserveFailureAsync(Task operation)
    {
        try { await operation.WaitAsync(StartupHost.Deadline); }
        catch (Exception error) when (operation.IsCompleted) { return error; }
        throw new AssertFailedException("Startup unexpectedly succeeded.");
    }
}
