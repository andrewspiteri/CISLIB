using Azure.Core;
using Azure.Identity;
using CIS.CommonTesting;
using CIS.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.Security.Entra;

[TestClass]
public sealed class EntraRecheckCancellationTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task JoiningCancellationWaitsForSharedSdkSettlementAndPreservesItsOriginalFault(bool sdkFaults)
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var original = new InvalidOperationException("Shared SDK fixture fault.");
        CancellationToken sdkToken = default;
        int calls = 0;
        int sdkCancellations = 0;
        using var joiningCancellation = new CancellationTokenSource();
        using var credential = new ControlledWorkloadCredential(async token =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                throw new CredentialUnavailableException("Required warmup fixture failure.");
            }
            sdkToken = token;
            using CancellationTokenRegistration registration = token.Register(() => Interlocked.Increment(ref sdkCancellations));
            entered.TrySetResult();
            try
            {
                await release.Task;
                if (sdkFaults)
                {
                    throw original;
                }
                return new AccessToken("shared-recheck-result", clock.GetUtcNow().AddMinutes(5));
            }
            finally
            {
                settled.TrySetResult();
            }
        });
        EntraWorkloadFixture fixture = await EntraWorkloadFixture.StartAsync(borrowed: _ => credential,
            required: true, clock: clock);
        Task? first = null;
        Task? joining = null;
        try
        {
            Assert.AreEqual(1, credential.Calls);
            clock.Advance(TimeSpan.FromSeconds(1));
            ISecurityCredentialControl control = fixture.Services.GetRequiredKeyedService<ISecurityCredentialControl>(
                EntraNativeAuthenticationTests.Profile);
            first = control.RequestSourceRecheckAsync().AsTask();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            joining = control.RequestSourceRecheckAsync(joiningCancellation.Token).AsTask();

            joiningCancellation.Cancel();

            Assert.IsFalse(first.IsCompleted);
            Assert.IsFalse(joining.IsCompleted,
                "A canceled joining wait must retain the actual shared SDK owner until settlement.");
            Assert.IsFalse(settled.Task.IsCompleted);
            Assert.IsFalse(sdkToken.IsCancellationRequested,
                "The joining caller does not own cancellation of another caller's shared acquisition.");
            Assert.AreEqual(0, Volatile.Read(ref sdkCancellations));
            Assert.AreEqual(2, credential.Calls, "Warmup and one shared recovery are the only SDK acquisitions.");

            release.TrySetResult();
            if (sdkFaults)
            {
                InvalidOperationException firstFault = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => first);
                InvalidOperationException joiningFault = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => joining);
                Assert.AreSame(original, firstFault);
                Assert.AreSame(original, joiningFault,
                    "The shared SDK's original fault takes precedence over joining cancellation.");
            }
            else
            {
                await first.WaitAsync(TimeSpan.FromSeconds(10));
                OperationCanceledException canceled = await Assert.ThrowsAsync<OperationCanceledException>(() => joining);
                Assert.AreEqual(joiningCancellation.Token, canceled.CancellationToken);
                Assert.IsTrue(fixture.Health.CaptureSnapshot().Ready,
                    "The canceled join does not undo the shared SDK's successful availability result.");
            }
            Assert.IsTrue(settled.Task.IsCompletedSuccessfully);
            Assert.AreEqual(2, credential.Calls);
            Assert.AreEqual(0, Volatile.Read(ref sdkCancellations));
        }
        finally
        {
            release.TrySetResult();
            var cleanupFailures = new List<Exception>();
            foreach (Task? pending in new[] { first, joining })
            {
                if (pending is null)
                {
                    continue;
                }
                try
                {
                    await pending;
                }
                catch (Exception error) when (sdkFaults && ReferenceEquals(error, original))
                {
                }
                catch (OperationCanceledException error) when (ReferenceEquals(pending, joining)
                    && error.CancellationToken == joiningCancellation.Token)
                {
                }
                catch (Exception error)
                {
                    cleanupFailures.Add(error);
                }
            }
            try
            {
                await fixture.DisposeAsync();
            }
            catch (Exception error) when (sdkFaults && ReferenceEquals(error, original))
            {
                // Runtime disposal also joins and retains the same already asserted recovery fault.
            }
            catch (Exception error)
            {
                cleanupFailures.Add(error);
            }
            if (cleanupFailures.Count != 0)
            {
                throw new AggregateException("Shared recheck fixture cleanup failed.", cleanupFailures);
            }
        }
        Assert.AreEqual(0, credential.Disposals, "The host must not dispose the borrowed SDK credential.");
    }
}
