using CIS.CommonTesting;
using CIS.Management;
using CIS.RestClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Tests.UnitTests.Management.ManagementConfigurationClientTestSupport;

namespace CIS.Tests.UnitTests.Management;

[TestClass]
public sealed class ScriptedManagementSchedulerClientTests
{
    [TestMethod]
    public async Task ScriptsAndSafeRecordsAreBoundedAndPreCanceledCallsPreserveTheNextStep()
    {
        var client = new ScriptedManagementSchedulerClient(2);
        var expected = new ManagementSchedulerCallResult<ManagementPage<ManagementSchedulerRun>>(
            RestResult<ManagementPage<ManagementSchedulerRun>>.FromValue(new([]), new(200, hasValue: true)));
        client.EnqueueResult(ScriptedManagementSchedulerCallKind.GetRuns, expected);
        client.EnqueueResult(ScriptedManagementSchedulerCallKind.GetRuns, expected);
        Assert.ThrowsExactly<InvalidOperationException>(() => client.EnqueueResult(ScriptedManagementSchedulerCallKind.GetRuns, expected));
        await Assert.ThrowsAsync<OperationCanceledException>(() => client.GetRunsAsync(AppId, new(), cancellationToken: new(canceled: true)));
        Assert.AreEqual(0L, client.TotalCalls);
        Assert.AreSame(expected, await client.GetRunsAsync(AppId, new()));
        Assert.AreSame(expected, await client.GetRunsAsync(AppId, new()));
        client.EnqueueResult(ScriptedManagementSchedulerCallKind.GetRuns, expected);
        await client.GetRunsAsync(AppId, new());
        Assert.AreEqual(3L, client.TotalCalls);
        Assert.AreEqual(1L, client.DroppedCalls);
        Assert.HasCount(2, client.Calls);
        Assert.IsTrue(client.Calls.All(call => call.ApplicationId == AppId && call.OperationKey is null));
    }

    [TestMethod]
    public async Task PendingCallsSurviveClearAndOriginalFaultsAreNotConvertedToValues()
    {
        var client = new ScriptedManagementSchedulerClient();
        var expected = new ManagementConfigurationCallResult<ManagementConfigurationActions>(
            RestResult<ManagementConfigurationActions>.FromValue(new(Source, []), new(200, hasValue: true)));
        PendingManagementCall pending = client.EnqueuePending(ScriptedManagementSchedulerCallKind.GetSourceActions, expected, observeCancellation: false);
        using var cancellation = new CancellationTokenSource();
        Task<ManagementConfigurationCallResult<ManagementConfigurationActions>> call = client.GetSourceActionsAsync(Source, cancellationToken: cancellation.Token);
        await pending.Entered.WaitAsync(Deadline);
        client.Clear();
        cancellation.Cancel();
        Assert.IsFalse(call.IsCompleted);
        pending.Release();
        Assert.AreSame(expected, await call.WaitAsync(Deadline));
        var original = new OperationCanceledException("Unrelated programming fault");
        client.EnqueueException(ScriptedManagementSchedulerCallKind.GetSourceActions, original);
        Task<ManagementConfigurationCallResult<ManagementConfigurationActions>> failed = client.GetSourceActionsAsync(Source);
        Assert.AreSame(original, await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => failed));
        Assert.IsTrue(failed.IsFaulted);
    }

    [TestMethod]
    public async Task WrongApiAndWrapperFailWithoutInventingANativeResult()
    {
        var client = new ScriptedManagementSchedulerClient();
        var result = new ManagementCallResult<ManagementPage<ManagementCapabilityDefinition>>(
            RestResult<ManagementPage<ManagementCapabilityDefinition>>.FromValue(new([]), new(200, hasValue: true)));
        client.EnqueueResult(ScriptedManagementSchedulerCallKind.GetCapabilityDefinitions, result);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => client.ListTasksAsync(AppId));
        client.EnqueueResult(ScriptedManagementSchedulerCallKind.GetCapabilityDefinitions, new object());
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => client.GetCapabilityDefinitionsAsync(AppId));
        Assert.AreEqual(2L, client.TotalCalls);
    }
}

