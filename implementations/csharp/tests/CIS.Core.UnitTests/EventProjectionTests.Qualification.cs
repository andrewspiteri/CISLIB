using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Events;
using CIS.Logging.Scopes;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

// Explicit qualification keeps the original method, rows and assertions.
public sealed partial class EventProjectionTests
{
    [TestMethod]
    [TestCategory("ExtendedStress")]
    public void EventFactsDoNotRetainStartupInputsConfigurationCallerExceptionOrScopeStore()
    {
        (CapturedEventFacts facts, WeakReference[] sources) = CreateCollectibleInputs();
        for (int pass = 0; pass < 8 && sources.Any(source => source.IsAlive); pass++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        }
        Assert.IsTrue(sources.All(source => !source.IsAlive));
        Assert.AreEqual("message", facts.Message);
        Assert.AreEqual("boom", facts.Exception!.Message);
        GC.KeepAlive(facts);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (CapturedEventFacts, WeakReference[]) CreateCollectibleInputs()
    {
        EventMetadata snapshot = EventProjectionFixtureSources.Create(Metadata(), out JsonObject source);
        var attributes = new Dictionary<string, LogValue> { ["value"] = LogValue.Null() };
        var exception = new Exception("boom");
        var attachment = new object(); exception.Data["attachment"] = attachment;
        var data = new LogEventData { EventName = "event", Message = "message", Attributes = attributes, Exception = exception };
        var store = new OperationScopeStore();
        CapturedEventFacts facts;
        using (store.BeginScope(new OperationContextData { OperationId = "scope" }))
        {
            facts = EventProjection.Capture(snapshot, store.Capture(), "Component", CoreLogLevel.Information, data)!;
        }
        WeakReference[] references = new object[] { snapshot, EnvironmentStoreTestData.Values.Logging, source, data, attributes, exception, attachment, store }
            .Select(value => new WeakReference(value)).ToArray();
        // Caller replaces its store reference; the captured event must not retain the old settings.
        EnvironmentStoreTestData.Values.Logging = new LoggingOptions();
        return (facts, references);
    }
}
