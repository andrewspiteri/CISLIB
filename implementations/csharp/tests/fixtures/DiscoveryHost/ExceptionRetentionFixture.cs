using System.Runtime.CompilerServices;
using CIS.Logging.Exceptions;

namespace CIS.Tests.DiscoveryFixture;

internal sealed record ExceptionRetention(CapturedException Captured, WeakReference[] CallerReferences);

internal static class ExceptionRetentionFixture
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static ExceptionRetention Create(string kind)
    {
        Exception root = ExceptionContentSources.CreateBcl(kind);
        var references = new List<WeakReference>();
        foreach (Exception node in ExceptionContentSources.BclNodes(root))
        {
            references.Add(new WeakReference(node));
            references.Add(new WeakReference(node.Data));
            references.Add(new WeakReference(node.Data["attachment"]!));
        }
        // Include an unrelated private header dictionary in the collection proof.
        references.Add(new WeakReference(root.Data["headers"]!));
        CapturedException captured = ExceptionProjection.Capture(root)!;
        return new(captured, references.ToArray());
    }

    internal static bool Collect(ExceptionRetention state)
    {
        for (int attempt = 0; attempt < 5 && state.CallerReferences.Any(reference => reference.IsAlive); attempt++)
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        }
        bool collected = state.CallerReferences.All(reference => !reference.IsAlive);
        GC.KeepAlive(state.Captured);
        return collected;
    }
}
