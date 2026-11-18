using CIS.RestClient;
using CIS.RestClient.Resilience;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.RestClient;

[TestClass]
public sealed class ResilienceDelayTests
{
    [TestMethod]
    public void FractionalComputedLocalDelayUsesOneRepresentableBudgetAndNeverShortensEitherMinimum()
    {
        Assert.IsTrue(PollyRestRetryExecutor.TryChooseDelay(TimeSpan.FromTicks(5_005_678), TimeSpan.FromMilliseconds(499),
            TimeSpan.FromSeconds(30), out TimeSpan chosen, out TimeSpan supplemental));
        Assert.AreEqual(TimeSpan.FromMilliseconds(501), chosen);
        Assert.AreEqual(TimeSpan.FromMilliseconds(1), supplemental);
        Assert.IsFalse(PollyRestRetryExecutor.TryChooseDelay(TimeSpan.FromTicks(5_005_678), TimeSpan.Zero,
            TimeSpan.FromMilliseconds(501), out chosen, out supplemental), "The same rounded delay must fit before either wait.");
    }

    [TestMethod]
    public async Task ActualPollyJitterAndSupplementalWaitSumToServerMinimumWithoutEarlyDispatch()
    {
        using var clock = new ResilienceClock();
        using var cancellation = new CancellationTokenSource();
        var policy = new RestRetryPolicy("jitter", 1, TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(5), [503]);
        var executor = new PollyRestRetryExecutor([policy], clock);
        int calls = 0; bool previousDisposed = false;
        Task<RestPolicyResult> run = executor.ExecuteAsync(policy, ResilienceTestSupport.Context(clock, cancellation.Token), (iteration, _) =>
        {
            Assert.AreEqual(calls, iteration); calls++;
            if (iteration == 0) { previousDisposed = true; return ValueTask.FromResult(ResilienceTestSupport.Http("10")); }
            Assert.IsTrue(previousDisposed);
            return ValueTask.FromResult(ResilienceTestSupport.Success());
        }).AsTask();
        try
        {
            ResilienceClock.Wait extra = await clock.NextAsync();
            Assert.IsTrue(previousDisposed);
            Assert.IsTrue(extra.Delay >= TimeSpan.FromSeconds(5) && extra.Delay <= TimeSpan.FromSeconds(10));
            Assert.AreEqual(1, calls);
            clock.Advance(extra.Delay);
            TimeSpan nativeLocal = TimeSpan.FromSeconds(10) - extra.Delay;
            if (nativeLocal > TimeSpan.Zero)
            {
                ResilienceClock.Wait local = await clock.NextAsync();
                Assert.AreEqual(nativeLocal, local.Delay);
                Assert.AreEqual(1, calls);
                clock.Advance(local.Delay);
                await local.Disposed.Task.WaitAsync(ResilienceTestSupport.Deadline);
            }
            RestPolicyResult result = await run.WaitAsync(ResilienceTestSupport.Deadline);
            Assert.AreEqual(RestFailureKind.None, result.Terminal.FailureKind);
            Assert.AreEqual(2, calls);
            Assert.AreEqual(TimeSpan.FromSeconds(10), clock.GetElapsedTime(0));
            await extra.Disposed.Task.WaitAsync(ResilienceTestSupport.Deadline);
        }
        finally { cancellation.Cancel(); await ResilienceTestSupport.SettleAsync(run); }
    }

    [TestMethod]
    [DataRow("short", 1)]
    [DataRow("date", 2)]
    [DataRow("past", 1)]
    [DataRow("invalid", 1)]
    [DataRow("missing", 1)]
    public async Task NativeHeaderParsingUsesServerMinimumOrOneLocalFallback(string kind, int expectedWaits)
    {
        using var clock = new ResilienceClock();
        using var cancellation = new CancellationTokenSource();
        var policy = new RestRetryPolicy("headers", 1, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), [503],
            backoff: RestRetryBackoff.Constant, useJitter: false);
        var executor = new PollyRestRetryExecutor([policy], clock);
        string? header = kind switch
        {
            "short" => "1",
            "date" => clock.GetUtcNow().AddSeconds(10).ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            "past" => clock.GetUtcNow().AddDays(-1).ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            "invalid" => "not-a-delay",
            _ => null
        };
        int calls = 0;
        Task<RestPolicyResult> run = executor.ExecuteAsync(policy, ResilienceTestSupport.Context(clock, cancellation.Token), (_, _) =>
            ValueTask.FromResult(calls++ == 0 ? ResilienceTestSupport.Http(header) : ResilienceTestSupport.Success())).AsTask();
        try
        {
            for (int index = 0; index < expectedWaits; index++)
            {
                ResilienceClock.Wait wait = await clock.NextAsync();
                Assert.AreEqual(kind == "date" && index == 0 ? TimeSpan.FromSeconds(8) : TimeSpan.FromSeconds(2), wait.Delay);
                Assert.AreEqual(1, calls); clock.Advance(wait.Delay);
                await wait.Disposed.Task.WaitAsync(ResilienceTestSupport.Deadline);
            }
            Assert.AreEqual(RestFailureKind.None, (await run.WaitAsync(ResilienceTestSupport.Deadline)).Terminal.FailureKind);
            Assert.AreEqual(expectedWaits, clock.Created); Assert.AreEqual(2, calls);
        }
        finally { cancellation.Cancel(); await ResilienceTestSupport.SettleAsync(run); }
    }

    [TestMethod]
    [DataRow("10")]
    [DataRow("99999999999999999999999999999999999999999")]
    public async Task InsufficientOrUnrepresentableServerMinimumPreservesTerminal503WithoutAnyWait(string header)
    {
        using var clock = new ResilienceClock();
        using var cancellation = new CancellationTokenSource();
        var policy = new RestRetryPolicy("budget", 1, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), [503], useJitter: false);
        var executor = new PollyRestRetryExecutor([policy], clock);
        int calls = 0;
        RestAttemptOutcome terminal = ResilienceTestSupport.Http(header);
        Task<RestPolicyResult> run = executor.ExecuteAsync(policy, ResilienceTestSupport.Context(clock, cancellation.Token,
            timeout: TimeSpan.FromSeconds(9)), (_, _) => { calls++; return ValueTask.FromResult(terminal); }).AsTask();
        try
        {
            RestPolicyResult result = await run.WaitAsync(ResilienceTestSupport.Deadline);
            Assert.AreSame(terminal, result.Terminal);
            Assert.AreEqual(RestRetryStopReason.InsufficientTimeRemaining, result.StopReason);
            Assert.AreEqual(1, calls); Assert.AreEqual(0, clock.Created);
        }
        finally { cancellation.Cancel(); await ResilienceTestSupport.SettleAsync(run); }
    }
}
