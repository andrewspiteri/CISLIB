using System.Diagnostics;
using System.Runtime.CompilerServices;
using CIS.Logging.Events;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

// Explicit qualification keeps the original method, rows and assertions.
public sealed partial class EventCorrelationTests
{
    [TestMethod]
    [TestCategory("ExtendedStress")]
    public void OwnedFactsDoNotKeepActivityOrClockAlive()
    {
        (CapturedEventFacts facts, WeakReference activity, WeakReference clock) = CaptureCollectible();
        for (int attempt = 0; attempt < 10 && (activity.IsAlive || clock.IsAlive); attempt++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
        Assert.IsFalse(activity.IsAlive); Assert.IsFalse(clock.IsAlive); Assert.IsNotNull(facts.TraceId); Assert.IsNotNull(facts.SpanId);
        GC.KeepAlive(facts);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (CapturedEventFacts, WeakReference, WeakReference) CaptureCollectible()
    {
        Activity? previous = Activity.Current; Activity.Current = null;
        using var activity = new Activity("collectible").SetIdFormat(ActivityIdFormat.W3C).Start();
        var clock = new EventCorrelationFixture.MutableClock(DateTimeOffset.MinValue);
        try { return (Capture(clock: clock), new WeakReference(activity), new WeakReference(clock)); }
        finally { activity.Stop(); Activity.Current = previous; }
    }
}
