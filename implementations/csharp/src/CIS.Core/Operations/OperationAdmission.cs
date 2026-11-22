using System.Runtime.CompilerServices;
using CIS.Security;
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

    internal static IDisposable ExpectBinding(ILocalTransactionContext context, OperationIdentity identity, CapabilityTarget target)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(target);
        Marker marker = Used.GetValue(context, static _ => new Marker());
        lock (marker)
        {
            if (marker.Used != 0 || marker.Expected is not null)
            {
                throw new InvalidOperationException("Bind one expected durable identity/resource before the original context is admitted.");
            }
            var expected = new ExpectedBinding(identity, target);
            marker.Expected = expected;
            return new BindingScope(marker, expected);
        }
    }

    internal static void CheckBinding(ILocalTransactionContext context, OperationIdentity identity, CapabilityTarget target)
    {
        if (!Used.TryGetValue(context, out Marker? marker))
        {
            return;
        }
        ExpectedBinding? expected;
        lock (marker)
        {
            expected = marker.Expected;
        }
        if (expected is not null && (expected.Identity != identity || expected.Target != target))
        {
            try
            {
                context.MarkRollbackOnly("operations.scope_binding_changed");
            }
            catch
            {
                // A disposed/misused owner must not replace the binding fault.
            }
            throw new InvalidOperationException("The actual durable scope/resource differs from its original-context expected binding.");
        }
    }

    private sealed class Marker
    {
        internal int Used;
        internal ExpectedBinding? Expected;
    }

    private sealed record ExpectedBinding(OperationIdentity Identity, CapabilityTarget Target);

    private sealed class BindingScope(Marker marker, ExpectedBinding expected) : IDisposable
    {
        private ExpectedBinding? expected = expected;

        public void Dispose()
        {
            ExpectedBinding? held = Interlocked.Exchange(ref expected, null);
            lock (marker)
            {
                if (held is not null && ReferenceEquals(marker.Expected, held))
                {
                    marker.Expected = null;
                }
            }
        }
    }
}
