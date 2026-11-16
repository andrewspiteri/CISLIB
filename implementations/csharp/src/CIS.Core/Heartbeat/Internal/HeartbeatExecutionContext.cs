namespace CIS.Heartbeat;

// Scalar identities avoid keeping subscribers/scopes alive through a captured logical context.
internal static class HeartbeatExecutionContext
{
    private static readonly AsyncLocal<Scope?> Current = new();

    internal static IDisposable Enter(HeartbeatRuntime runtime, Guid registrationId, Guid ownerId)
    {
        var scope = new Scope(runtime, registrationId, ownerId, Current.Value);
        Current.Value = scope;
        return scope;
    }

    internal static void ThrowIfWaitingOn(HeartbeatRuntime runtime, Guid? registrationId = null, Guid? ownerId = null)
    {
        for (Scope? scope = Current.Value; scope is not null; scope = scope.Previous)
        {
            if (Volatile.Read(ref scope.Active) != 0 && ReferenceEquals(scope.Runtime, runtime)
                && (!registrationId.HasValue || registrationId == scope.RegistrationId)
                && (!ownerId.HasValue || ownerId == scope.OwnerId))
            {
                throw new InvalidOperationException("Heartbeat work cannot wait for a barrier containing itself. Notify an external owner and return.");
            }
        }
    }

    private sealed class Scope(HeartbeatRuntime runtime, Guid registrationId, Guid ownerId, Scope? previous) : IDisposable
    {
        internal readonly HeartbeatRuntime Runtime = runtime;
        internal readonly Guid RegistrationId = registrationId;
        internal readonly Guid OwnerId = ownerId;
        internal readonly Scope? Previous = previous;
        internal int Active = 1;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref Active, 0) == 0) { return; }
            if (ReferenceEquals(Current.Value, this)) { Current.Value = Previous; }
        }
    }
}
