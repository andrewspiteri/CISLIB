using System.Runtime.CompilerServices;
using CIS.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

// Explicit qualification keeps the original method, rows and assertions.
public sealed partial class InterpolationTests
{
    [TestMethod]
    [TestCategory("ExtendedStress")]
    public async Task UserObjectCanBeCollectedWhileItsOccurrenceIsStillWaiting()
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writer = new Writer(); await using var runtime = Create(writer, start: start);
        WeakReference reference = PublishCollectible(runtime.CreateLogger("Collectible"));
        try
        {
            for (int count = 0; count < 3 && reference.IsAlive; count++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
            Assert.IsFalse(reference.IsAlive, "The queued occurrence must not retain the caller's formatting object.");
        }
        finally { start.TrySetResult(); }
        await Drain(runtime); Assert.AreEqual("original/", Message(writer, 0));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference PublishCollectible(CoreLogger logger)
    {
        var value = new MutableValue { Text = "original" }; logger.Info($"{value}"); return new(value);
    }
}
