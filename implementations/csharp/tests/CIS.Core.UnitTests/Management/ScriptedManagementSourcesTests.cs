using System.Text.Json;
using CIS.CommonTesting;
using CIS.Management;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Management;

[TestClass]
public sealed class ScriptedManagementSourcesTests
{
    [TestMethod]
    public void ObservationCaptureReturnsTheExactSelectedContentWithoutRepairingIt()
    {
        var script = new ScriptedManagementObservationSource();
        IManagementObservationSource source = script;
        var selected = new ManagementObservationContent("future-lifecycle", "selected-health");
        script.EnqueueValue(selected);

        Assert.AreSame(selected, source.CaptureObservation());
        Assert.AreEqual(1L, script.TotalCaptures);
        Assert.AreEqual(0L, script.DroppedCaptures);
        Assert.HasCount(1, script.Captures);
        Assert.AreEqual("observation", script.Captures[0].Kind);
        Assert.ThrowsExactly<InvalidOperationException>(() => source.CaptureObservation());
        Assert.AreEqual(1L, script.TotalCaptures, "Exhaustion must not manufacture a default observation or record an admitted step.");
    }

    [TestMethod]
    public void InventoryCaptureBorrowsTheWholeSelectedCatalogAndRetainsOnlySafeKindMetadata()
    {
        using JsonDocument details = JsonDocument.Parse("{\"privatePayload\":\"not retained in captures\"}");
        var modules = new List<ManagementModuleInventory> { new("future.module", "one", details.RootElement) };
        var selected = new ManagementInventoryContent(modules, [], []);
        var script = new ScriptedManagementInventorySource();
        IManagementInventorySource source = script;
        script.EnqueueValue(selected);
        modules.Add(new("another.module", "two"));

        ManagementInventoryContent actual = source.CaptureInventory();
        Assert.AreSame(selected, actual);
        Assert.AreSame(modules, actual.Modules);
        Assert.HasCount(2, actual.Modules, "The script must return the selected whole content, not a repaired or partial catalog.");
        Assert.AreEqual(1L, script.TotalCaptures);
        Assert.AreEqual("inventory", script.Captures.Single().Kind);
        CollectionAssert.AreEqual(new[] { "Kind" }, typeof(ScriptedManagementCapture).GetProperties().Select(property => property.Name).ToArray());
        string safe = JsonSerializer.Serialize(script.Captures);
        Assert.IsFalse(safe.Contains("privatePayload", StringComparison.Ordinal));
        Assert.IsFalse(safe.Contains("future.module", StringComparison.Ordinal));
        Assert.ThrowsExactly<InvalidOperationException>(() => source.CaptureInventory());
        Assert.AreEqual(1L, script.TotalCaptures);
    }

    [TestMethod]
    public void QueueAndRecordBoundsAreIndependentAndClearDropsPendingContentAndCounters()
    {
        var script = new ScriptedManagementObservationSource(capacity: 1);
        var first = new ManagementObservationContent("first", "healthy");
        var second = new ManagementObservationContent("second", "unhealthy");
        script.EnqueueValue(first);
        Assert.ThrowsExactly<InvalidOperationException>(() => script.EnqueueValue(second));
        Assert.AreSame(first, script.CaptureObservation());
        IReadOnlyList<ScriptedManagementCapture> previous = script.Captures;
        Assert.IsTrue(((IList<ScriptedManagementCapture>)previous).IsReadOnly);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<ScriptedManagementCapture>)previous).Add(new("untrusted")));

        script.EnqueueValue(second);
        Assert.AreSame(second, script.CaptureObservation());
        Assert.HasCount(1, script.Captures);
        Assert.AreEqual(2L, script.TotalCaptures);
        Assert.AreEqual(1L, script.DroppedCaptures);
        script.EnqueueValue(first);
        script.Clear();
        Assert.HasCount(0, script.Captures);
        Assert.AreEqual(0L, script.TotalCaptures);
        Assert.AreEqual(0L, script.DroppedCaptures);
        Assert.HasCount(1, previous, "Previously returned safe snapshots must remain detached from Clear.");
        Assert.ThrowsExactly<InvalidOperationException>(() => script.CaptureObservation());
        script.EnqueueValue(second);
        Assert.AreSame(second, script.CaptureObservation());
        Assert.AreEqual(1L, script.TotalCaptures);
    }

    [TestMethod]
    public void PrecancellationConsumesNeitherSourceStepAndPreservesTheCallerToken()
    {
        var observation = new ScriptedManagementObservationSource();
        var inventory = new ScriptedManagementInventorySource();
        var observationValue = new ManagementObservationContent("ready", "healthy");
        var inventoryValue = new ManagementInventoryContent([], [], []);
        observation.EnqueueValue(observationValue);
        inventory.EnqueueValue(inventoryValue);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        OperationCanceledException first = Assert.ThrowsExactly<OperationCanceledException>(() => observation.CaptureObservation(canceled.Token));
        OperationCanceledException second = Assert.ThrowsExactly<OperationCanceledException>(() => inventory.CaptureInventory(canceled.Token));
        Assert.AreEqual(canceled.Token, first.CancellationToken);
        Assert.AreEqual(canceled.Token, second.CancellationToken);
        Assert.AreEqual(0L, observation.TotalCaptures);
        Assert.AreEqual(0L, inventory.TotalCaptures);
        Assert.HasCount(0, observation.Captures);
        Assert.HasCount(0, inventory.Captures);
        Assert.AreSame(observationValue, observation.CaptureObservation());
        Assert.AreSame(inventoryValue, inventory.CaptureInventory());
    }

    [TestMethod]
    public void ProgrammingCancellationAndWholeInventoryRefusalRemainTheOriginalExceptions()
    {
        var observation = new ScriptedManagementObservationSource();
        var inventory = new ScriptedManagementInventorySource();
        var programming = new InvalidOperationException("Original programming fault.");
        using var unrelated = new CancellationTokenSource();
        unrelated.Cancel();
        var cancellation = new OperationCanceledException("Original selected fault.", unrelated.Token);
        var refusal = new ManagementInventoryRejectedException("management.inventory.local_limit", programming);
        observation.EnqueueException(programming);
        observation.EnqueueException(cancellation);
        inventory.EnqueueException(refusal);

        Assert.AreSame(programming, Assert.ThrowsExactly<InvalidOperationException>(() => observation.CaptureObservation()));
        OperationCanceledException caught = Assert.ThrowsExactly<OperationCanceledException>(() => observation.CaptureObservation());
        Assert.AreSame(cancellation, caught);
        Assert.AreEqual(unrelated.Token, caught.CancellationToken);
        Assert.AreSame(refusal, Assert.ThrowsExactly<ManagementInventoryRejectedException>(() => inventory.CaptureInventory()));
        Assert.AreSame(programming, refusal.InnerException);
        Assert.AreEqual(2L, observation.TotalCaptures);
        Assert.AreEqual(1L, inventory.TotalCaptures);
        Assert.IsTrue(observation.Captures.All(capture => capture.Kind == "observation"));
        Assert.IsTrue(inventory.Captures.All(capture => capture.Kind == "inventory"));
        inventory.Clear();
        Assert.AreEqual(0L, inventory.TotalCaptures);
        Assert.ThrowsExactly<InvalidOperationException>(() => inventory.CaptureInventory());
    }

    [TestMethod]
    public void DefaultCapacityIsSixteenAndInputsCannotBypassFiniteScriptAdmission()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ScriptedManagementObservationSource(0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ScriptedManagementInventorySource(65_537));
        _ = new ScriptedManagementObservationSource(65_536);
        _ = new ScriptedManagementInventorySource(1);
        var observation = new ScriptedManagementObservationSource();
        var inventory = new ScriptedManagementInventorySource();
        Assert.ThrowsExactly<ArgumentNullException>(() => observation.EnqueueValue(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => observation.EnqueueException(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => inventory.EnqueueValue(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => inventory.EnqueueException(null!));
        Assert.AreEqual(0L, observation.TotalCaptures);
        Assert.AreEqual(0L, inventory.TotalCaptures);
        for (int index = 0; index < 16; index++)
        {
            observation.EnqueueValue(new("ready", "healthy"));
            inventory.EnqueueValue(new([], [], []));
        }
        Assert.ThrowsExactly<InvalidOperationException>(() => observation.EnqueueValue(new("ready", "healthy")));
        Assert.ThrowsExactly<InvalidOperationException>(() => inventory.EnqueueValue(new([], [], [])));
        inventory.Clear();
        Assert.ThrowsExactly<InvalidOperationException>(() => inventory.CaptureInventory());
    }
}
