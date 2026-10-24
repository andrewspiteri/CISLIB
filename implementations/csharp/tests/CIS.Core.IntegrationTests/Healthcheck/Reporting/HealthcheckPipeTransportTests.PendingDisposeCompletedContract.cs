using System.Runtime.CompilerServices;

namespace CIS.Tests.IntegrationTests.Healthcheck.Reporting;

public sealed partial class HealthcheckPipeTransportTests
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RequirePendingDisposeCompletedContractSingleton(Task actualContract, Task actualFirstDispose,
        PendingDisposeFacts facts, PendingDisposeStatus status)
    {
        if (!ReferenceEquals(actualContract, Task.CompletedTask) || !actualContract.IsCompletedSuccessfully || !actualFirstDispose.IsCompletedSuccessfully)
        { throw new InvalidOperationException("Only the exact actual cached completed contract after successful original first Dispose can leave the owner graph."); }
        RequirePendingDisposeCompletedContractFacts(facts, status);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RequirePendingDisposeCompletedContractSingletonAfterGraph(WeakReference<Task> reference,
        PendingDisposeFacts facts, PendingDisposeStatus status)
    {
        if (!reference.TryGetTarget(out Task? actualContract) || !ReferenceEquals(actualContract, Task.CompletedTask) || !actualContract.IsCompletedSuccessfully)
        { throw new InvalidOperationException("The exact original completed contract singleton must remain observed after every actual resource owner dies."); }
        RequirePendingDisposeCompletedContractFacts(facts, status);
    }

    private static void RequirePendingDisposeCompletedContractFacts(PendingDisposeFacts facts, PendingDisposeStatus status)
    {
        RequirePendingDisposePhase(facts);
        Exception[] unknown = status.Errors();
        if (unknown.Length > 0) { throw new AggregateException("Unknown actual terminal errors cannot be bypassed by the completed contract singleton.", unknown); }
        if (facts.EarlyAssertion is not null || facts.ReplyAssertion is not null
            || Volatile.Read(ref facts.firstDisposeInvoked) != 1 || Volatile.Read(ref facts.firstDisposeSucceeded) != 1
            || Volatile.Read(ref facts.laterStopSucceeded) != 1 || Volatile.Read(ref facts.originalReplyFailureAssertionReturned) != 1
            || Volatile.Read(ref facts.originalPendingAssertionReturned) != 1 || Volatile.Read(ref facts.originalCallerRelease) != 1
            || !status.Stop || !status.Dispose || !status.FirstDispose || !status.Contract)
        { throw new InvalidOperationException("The exact completed contract requires every actual original terminal assertion and independent cleanup success before and after owner review."); }
    }
}
