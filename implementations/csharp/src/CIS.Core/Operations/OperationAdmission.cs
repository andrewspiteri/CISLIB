using System.Runtime.CompilerServices;
using CIS.Transactions;

namespace CIS.Operations;

// One module-wide marker across service instances, definitions and all generic request/result types.
// The value holds no context reference and is never reset or removed after admission.
internal static class OperationAdmission
{
    private static readonly ConditionalWeakTable<ILocalTransactionContext, Marker> Used = new();

    internal static void Enter(ILocalTransactionContext context)
    {
        Marker marker = Used.GetValue(context, static _ => new Marker());
        if (Interlocked.Exchange(ref marker.Used, 1) != 0)
        {
            throw new InvalidOperationException("A transaction context can be admitted to only one durable operation. Use a fresh owner for another attempt.");
        }
    }

    private sealed class Marker
    {
        internal int Used;
    }
}
