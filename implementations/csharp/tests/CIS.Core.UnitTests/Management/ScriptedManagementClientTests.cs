using CIS.CommonTesting;
using CIS.Management;
using CIS.RestClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Management;

[TestClass]
public sealed class ScriptedManagementClientTests
{
    private const string Id = "6d2222a6-2ffd-4010-bf15-bd22a8b70f4b";
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [TestMethod]
    public async Task ExplicitScriptsPreserveExactNativeFailureAndOriginalKeyWithBoundedSafeRecords()
    {
        var script = new ScriptedManagementClient(1);
        RestResult<ManagementMetadataResult> exchange = RestResult<ManagementMetadataResult>.Failed("client.timeout", "Unknown",
            new RestResultContext(failureKind: RestFailureKind.Timeout));
        var result = new ManagementCallResult<ManagementMetadataResult>(exchange, operationKey: "original-key");
        script.EnqueueResult(ScriptedManagementCallKind.MetadataReplace, result);
        Assert.ThrowsExactly<InvalidOperationException>(() => script.EnqueueResult(ScriptedManagementCallKind.MetadataReplace, result));

        ManagementCallResult<ManagementMetadataResult> first = await script.ReplaceMetadataAsync(Id, "original-key", "\"m1-1\"", new(1, "Display"));
        script.EnqueueResult(ScriptedManagementCallKind.MetadataOperation, result);
        ManagementCallResult<ManagementMetadataResult> lookup = await script.GetMetadataOperationAsync(Id, "original-key");

        Assert.AreSame(result, first);
        Assert.AreSame(exchange, lookup.Exchange);
        Assert.AreEqual("original-key", lookup.OperationKey);
        Assert.AreEqual(2, script.TotalCalls);
        Assert.AreEqual(1, script.DroppedCalls);
        Assert.HasCount(1, script.Calls);
        Assert.AreEqual(ScriptedManagementCallKind.MetadataOperation, script.Calls[0].Kind);
        Assert.IsNull(typeof(ScriptedManagementCall).GetProperty("Body"));
        Assert.IsNull(typeof(ScriptedManagementCall).GetProperty("Credentials"));
        Assert.IsNull(typeof(ScriptedManagementCall).GetProperty("Options"));
    }

    [TestMethod]
    public async Task PrecancellationConsumesNothingAndUnexpectedOceRemainsTheOriginalFault()
    {
        var script = new ScriptedManagementClient();
        var original = new OperationCanceledException("Selected programming fault.");
        script.EnqueueException(ScriptedManagementCallKind.ServiceInfo, original);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Task<ManagementCallResult<ManagementServiceInfo>> before = script.GetServiceInfoAsync(cancellationToken: canceled.Token);
        try
        {
            await before;
            Assert.Fail("Pre-cancellation must remain cancellation.");
        }
        catch (OperationCanceledException) when (before.IsCanceled)
        {
        }
        Assert.AreEqual(0, script.TotalCalls);

        Task<ManagementCallResult<ManagementServiceInfo>> fault = script.GetServiceInfoAsync();
        OperationCanceledException caught = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => fault);
        Assert.AreSame(original, caught);
        Assert.IsTrue(fault.IsFaulted);
        Assert.IsFalse(fault.IsCanceled);
    }

    [TestMethod]
    public async Task KindTypeAndExhaustionNeverSupplyDefaultSuccess()
    {
        var script = new ScriptedManagementClient();
        script.EnqueueResult(ScriptedManagementCallKind.Application, Info());
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => script.GetServiceInfoAsync());
        script.EnqueueResult(ScriptedManagementCallKind.ServiceInfo, Application());
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => script.GetServiceInfoAsync());
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => script.GetServiceInfoAsync());
        Assert.AreEqual(2, script.TotalCalls);
    }

    [TestMethod]
    public async Task MissingRequiredIdsAndKeysDoNotConsumeOrRecordAScriptStep()
    {
        var script = new ScriptedManagementClient();
        ManagementCallResult<ManagementServiceInfo> result = Info();
        script.EnqueueResult(ScriptedManagementCallKind.ServiceInfo, result);

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => script.GetApplicationAsync(null!));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => script.CloseRegistrationAsync(null!));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => script.GetMetadataOperationAsync(Id, null!));
        Assert.AreEqual(0, script.TotalCalls);
        Assert.HasCount(0, script.Calls);
        Assert.AreSame(result, await script.GetServiceInfoAsync());
        Assert.AreEqual(1, script.TotalCalls);
    }

    [TestMethod]
    public async Task HeldNoncooperativeWorkRemainsOwnedAcrossCancellationAndClear()
    {
        var script = new ScriptedManagementClient();
        ManagementCallResult<ManagementServiceInfo> result = Info();
        PendingManagementCall pending = script.EnqueuePending(ScriptedManagementCallKind.ServiceInfo, result, observeCancellation: false);
        using var cancel = new CancellationTokenSource();
        Task<ManagementCallResult<ManagementServiceInfo>> call = script.GetServiceInfoAsync(cancellationToken: cancel.Token);
        await pending.Entered.WaitAsync(Deadline);
        cancel.Cancel();
        script.Clear();
        Assert.IsFalse(call.IsCompleted);
        pending.Release();
        Assert.AreSame(result, await call.WaitAsync(Deadline));
        Assert.AreEqual(0, script.TotalCalls);
    }

    [TestMethod]
    public async Task CooperativeHeldWorkObservesActualCallerCancellation()
    {
        var script = new ScriptedManagementClient();
        PendingManagementCall pending = script.EnqueuePending(ScriptedManagementCallKind.ServiceInfo, Info());
        using var cancel = new CancellationTokenSource();
        Task<ManagementCallResult<ManagementServiceInfo>> call = script.GetServiceInfoAsync(cancellationToken: cancel.Token);
        await pending.Entered.WaitAsync(Deadline);
        cancel.Cancel();
        try
        {
            await call.WaitAsync(Deadline);
            Assert.Fail("The held script must observe its selected token.");
        }
        catch (OperationCanceledException) when (call.IsCanceled)
        {
        }
        finally
        {
            pending.Release();
        }
    }

    private static ManagementCallResult<ManagementServiceInfo> Info() => new(RestResult<ManagementServiceInfo>.FromValue(
        new("1", [1], []), new RestResultContext(200, hasValue: true)));

    private static ManagementCallResult<ManagementApplication> Application() => new(RestResult<ManagementApplication>.FromValue(
        new(Id, "orders", "Production", new("Orders"), "1", "\"m1-1\""), new RestResultContext(200, hasValue: true)));
}
