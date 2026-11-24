using CIS.CommonTesting;
using CIS.Management;
using CIS.RestClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Tests.UnitTests.Management.ManagementConfigurationClientTestSupport;

namespace CIS.Tests.UnitTests.Management;

[TestClass]
public sealed class ScriptedManagementConfigurationClientTests
{
    [TestMethod]
    public async Task FiniteScriptAndSafeRecordsRemainBoundedAndPreCanceledCallsDoNotConsumeTheNextStep()
    {
        var script = new ScriptedManagementConfigurationClient(2);
        ManagementConfigurationCallResult<ManagementConfigurationCatalog> result = CatalogResult();
        script.EnqueueResult(ScriptedManagementConfigurationCallKind.Catalog, result);
        script.EnqueueResult(ScriptedManagementConfigurationCallKind.Catalog, result);
        Assert.ThrowsExactly<InvalidOperationException>(() => script.EnqueueResult(ScriptedManagementConfigurationCallKind.Catalog, result));
        await Assert.ThrowsAsync<OperationCanceledException>(() => script.GetCatalogAsync(cancellationToken: new CancellationToken(canceled: true)));
        Assert.AreEqual(0L, script.TotalCalls);
        Assert.AreSame(result, await script.GetCatalogAsync());
        Assert.AreSame(result, await script.GetCatalogAsync());
        script.EnqueueResult(ScriptedManagementConfigurationCallKind.Catalog, result);
        await script.GetCatalogAsync();
        Assert.HasCount(2, script.Calls);
        Assert.AreEqual(3L, script.TotalCalls);
        Assert.AreEqual(1L, script.DroppedCalls);
        Assert.IsTrue(script.Calls.All(call => call.Kind == ScriptedManagementConfigurationCallKind.Catalog && call.ResourceId is null));
    }

    [TestMethod]
    public async Task ClearDoesNotAbandonHeldActualCallsAndUnrelatedCancellationRemainsAnOriginalFault()
    {
        var script = new ScriptedManagementConfigurationClient();
        ManagementConfigurationCallResult<ManagementConfigurationCatalog> result = CatalogResult();
        PendingManagementCall pending = script.EnqueuePending(ScriptedManagementConfigurationCallKind.Catalog, result, observeCancellation: false);
        using var cancellation = new CancellationTokenSource();
        Task<ManagementConfigurationCallResult<ManagementConfigurationCatalog>> held = script.GetCatalogAsync(cancellationToken: cancellation.Token);
        await pending.Entered.WaitAsync(Deadline);
        script.Clear();
        cancellation.Cancel();
        Assert.IsFalse(held.IsCompleted);
        pending.Release();
        Assert.AreSame(result, await held.WaitAsync(Deadline));
        var original = new OperationCanceledException("A provider programming fault");
        script.EnqueueException(ScriptedManagementConfigurationCallKind.Catalog, original);
        Task<ManagementConfigurationCallResult<ManagementConfigurationCatalog>> faulted = script.GetCatalogAsync();
        Assert.AreSame(original, await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => faulted));
        Assert.IsTrue(faulted.IsFaulted);
    }

    [TestMethod]
    public async Task SnapshotFactoryReceivesIndependentTargetOutsideTheScriptGateWithoutRecordingPayloadsOrEtags()
    {
        var script = new ScriptedManagementConfigurationClient();
        ManagementConfigurationSnapshot snapshot = Value<ManagementConfigurationSnapshot>("configuration-snapshot-valid");
        script.EnqueueSnapshot(target =>
        {
            Assert.AreEqual(Target(), target);
            Assert.IsTrue(Task.Run(() => script.EnqueueResult(ScriptedManagementConfigurationCallKind.Catalog, CatalogResult())).Wait(Deadline));
            return new(RestResult<ManagementConfigurationSnapshot>.FromValue(snapshot, new(200, hasValue: true)));
        });
        Assert.AreSame(snapshot, (await script.GetRegistrationConfigurationAsync(Target(), snapshot)).Exchange.Value);
        ScriptedManagementConfigurationCall call = script.Calls.Single();
        Assert.IsTrue(call.HasRetainedSnapshot);
        Assert.AreEqual(Target().RegistrationId, call.RegistrationId);
        Assert.AreEqual("sample.settings", call.ConfigurationId);
        Assert.AreEqual("1", call.SchemaVersion);
    }

    private static ManagementConfigurationCallResult<ManagementConfigurationCatalog> CatalogResult()
        => new(RestResult<ManagementConfigurationCatalog>.FromValue(Value<ManagementConfigurationCatalog>("configuration-catalog-valid"), new(200, hasValue: true)));
}
