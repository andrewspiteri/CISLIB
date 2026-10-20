namespace CIS.Healthcheck.Internal;

/// <summary>Detects waits on a collection's own barriers in explicitly marked execution flows.</summary>
/// <remarks>It does not establish ownership in detached or execution-context-suppressed user work.</remarks>
internal static class HealthCollectionExecution
{
    private static readonly AsyncLocal<ExecutionScope?> Current = new();

    internal static IDisposable Enter(IHealthCollectionState state, Guid monitorId, Guid collectionRegistrationId, Guid runnerId)
    {
        ArgumentNullException.ThrowIfNull(state);
        var scope = new ExecutionScope(state, monitorId, collectionRegistrationId, runnerId, Current.Value);
        Current.Value = scope;
        return scope;
    }

    internal static void ThrowIfSelfWait(IHealthCollectionState state, Guid? monitorId = null,
        Guid? collectionRegistrationId = null, Guid? runnerId = null)
    {
        for (ExecutionScope? scope = Current.Value; scope is not null; scope = scope.Previous)
        {
            if (Volatile.Read(ref scope.Active) != 0 && ReferenceEquals(scope.State, state) &&
                (!monitorId.HasValue || scope.MonitorId == monitorId.Value) &&
                (!collectionRegistrationId.HasValue || scope.CollectionRegistrationId == collectionRegistrationId.Value) &&
                (!runnerId.HasValue || scope.RunnerId == runnerId.Value))
            {
                throw new InvalidOperationException("A health collector cannot wait for a completion barrier containing its own operation. Request withdrawal and let its outer owner await settlement.");
            }
        }
    }

    private sealed class ExecutionScope(IHealthCollectionState state, Guid monitorId,
        Guid collectionRegistrationId, Guid runnerId, ExecutionScope? previous) : IDisposable
    {
        internal readonly IHealthCollectionState State = state;
        internal readonly Guid MonitorId = monitorId;
        internal readonly Guid CollectionRegistrationId = collectionRegistrationId;
        internal readonly Guid RunnerId = runnerId;
        internal readonly ExecutionScope? Previous = previous;
        internal int Active = 1;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref Active, 0) == 0) { return; }
            if (ReferenceEquals(Current.Value, this)) { Current.Value = Previous; }
        }
    }
}
