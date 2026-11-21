using CIS.CommonTesting;
using CIS.Operations;
using CIS.Transactions;
using CIS.Transactions.AdoNet;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.CommonTesting;

[TestClass]
public sealed class ScriptedDurableOperationServiceTests
{
    [TestMethod]
    public async Task ExactLogicalOutcomeAndContextArePublicWithoutInvokingHandlerOrCompletingOwner()
    {
        var transactions = new ScriptedLocalTransactionFactory();
        transactions.Enqueue();
        await using ILocalTransaction owner = await transactions.BeginAsync("Ledger");
        var scripts = new ScriptedDurableOperationService();
        IDurableOperationService substituted = scripts;
        var original = new OperationExecution<string>(OperationExecutionDisposition.Prepared, OperationDecision<string>.Succeed("original-id"));
        scripts.EnqueueExecution<MutableInput, string>(original);
        var input = new MutableInput { Amount = 5 };
        IReadOnlyList<ScriptedOperationCall> before = scripts.Calls;
        int invoked = 0;
        OperationExecution<string> observed = await substituted.ExecuteAsync(owner.Context,
            new OperationRequest<MutableInput>("Debit", "requested-scope", "opaque-key", input), (_, _, _) =>
            {
                invoked++;
                return Task.FromResult(OperationDecision<string>.Succeed("unexpected"));
            });
        input.Amount = 99;
        Assert.AreSame(original, observed);
        Assert.AreEqual(0, invoked);
        Assert.IsNull(owner.Completion);
        Assert.IsFalse(owner.Context.IsRollbackOnly);
        Assert.IsEmpty(before);
        ScriptedOperationCall call = scripts.Calls.Single();
        Assert.AreSame(owner.Context, call.Context);
        Assert.AreEqual(typeof(MutableInput), call.RequestType);
        Assert.AreEqual(typeof(string), call.ResultType);
        Assert.AreEqual("Debit", call.OperationName);
        Assert.AreEqual("requested-scope", call.RequestedScope);
        Assert.AreEqual("opaque-key", call.Key);
        Assert.IsFalse(call.IsLookup);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<ScriptedOperationCall>)scripts.Calls).Clear());
        Assert.AreEqual(0, scripts.PendingScripts);
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => owner.Context.UseAdoNetAsync((_, _, _) => Task.CompletedTask));
        Assert.AreEqual(TransactionOutcome.Committed, (await owner.CommitAsync()).Outcome);
    }

    [TestMethod]
    [DataRow(OperationExecutionDisposition.Prepared, OperationBusinessDecision.Succeeded)]
    [DataRow(OperationExecutionDisposition.Prepared, OperationBusinessDecision.Rejected)]
    [DataRow(OperationExecutionDisposition.Replayed, OperationBusinessDecision.Succeeded)]
    [DataRow(OperationExecutionDisposition.Replayed, OperationBusinessDecision.Rejected)]
    public async Task OriginalSucceededAndRejectedDecisionsRemainSeparateFromExecutionAndCommitFacts(
        OperationExecutionDisposition disposition, OperationBusinessDecision business)
    {
        var transactions = new ScriptedLocalTransactionFactory();
        transactions.Enqueue(new(TransactionOutcome.Unknown, TransactionEndReason.CommitRequested, true, false, false));
        await using ILocalTransaction owner = await transactions.BeginAsync("Ledger");
        var scripts = new ScriptedDurableOperationService();
        OperationDecision<string> decision = business == OperationBusinessDecision.Succeeded
            ? OperationDecision<string>.Succeed("original")
            : OperationDecision<string>.Reject("app.declined", "Original message");
        var scripted = new OperationExecution<string>(disposition, decision);
        scripts.EnqueueExecution<int, string>(scripted);
        Assert.AreSame(scripted, await scripts.ExecuteAsync(owner.Context, Request(), Never));
        Assert.AreEqual(TransactionOutcome.Unknown, (await owner.CommitAsync()).Outcome);
        Assert.AreEqual(business, scripted.Decision!.Decision);
    }

    [TestMethod]
    [DataRow(OperationExecutionDisposition.KeyConflict)]
    [DataRow(OperationExecutionDisposition.ResultExpired)]
    [DataRow(OperationExecutionDisposition.Incompatible)]
    [DataRow(OperationExecutionDisposition.Contended)]
    [DataRow(OperationExecutionDisposition.RetryRequired)]
    [DataRow(OperationExecutionDisposition.Denied)]
    [DataRow(OperationExecutionDisposition.RollbackOnly)]
    public async Task ExplicitNonbusinessExecutionFactsAreReturnedWithoutRuntimeEmulation(OperationExecutionDisposition disposition)
    {
        var transactions = new ScriptedLocalTransactionFactory();
        transactions.Enqueue();
        await using ILocalTransaction owner = await transactions.BeginAsync("Ledger");
        var scripts = new ScriptedDurableOperationService();
        scripts.EnqueueExecution<int, string>(new(disposition));
        OperationExecution<string> observed = await scripts.ExecuteAsync(owner.Context, Request(), Never);
        Assert.AreEqual(disposition, observed.Disposition);
        Assert.IsNull(observed.Decision);
        Assert.IsFalse(owner.Context.IsRollbackOnly, "A logical result does not impersonate native rollback-only marking.");
    }

    [TestMethod]
    public async Task LookupReturnsOriginalDecisionAndReadCompletionWithoutCreatingAnOwner()
    {
        var scripts = new ScriptedDurableOperationService();
        var completion = new TransactionCompletion(TransactionOutcome.NotCommitted, TransactionEndReason.RollbackRequested, false, true, true);
        var original = new OperationLookup<string>(OperationLookupDisposition.Found,
            OperationDecision<string>.Reject("app.declined", "Original rejection"), completion);
        scripts.EnqueueLookup(original);
        Assert.AreSame(original, await scripts.GetResultAsync<string>(new("Debit", "scope", "key")));
        ScriptedOperationCall call = scripts.Calls.Single();
        Assert.IsTrue(call.IsLookup);
        Assert.IsNull(call.Context);
        Assert.IsNull(call.RequestType);
        Assert.AreEqual(typeof(string), call.ResultType);
        Assert.AreSame(completion, original.ReadScopeCompletion);
    }

    [TestMethod]
    [DataRow(OperationLookupDisposition.NotFound)]
    [DataRow(OperationLookupDisposition.ResultExpired)]
    [DataRow(OperationLookupDisposition.Incompatible)]
    [DataRow(OperationLookupDisposition.Unavailable)]
    [DataRow(OperationLookupDisposition.Denied)]
    public async Task ExplicitLookupStatesNeverInventBusinessResults(OperationLookupDisposition disposition)
    {
        var scripts = new ScriptedDurableOperationService();
        scripts.EnqueueLookup<string>(new(disposition));
        OperationLookup<string> observed = await scripts.GetResultAsync<string>(new("Debit", "scope", "key"));
        Assert.AreEqual(disposition, observed.Disposition);
        Assert.IsNull(observed.Decision);
        Assert.IsNull(observed.ReadScopeCompletion);
    }

    [TestMethod]
    public async Task MismatchedKindAndTypesDoNotConsumeTheNextScriptOrRecordAnInvocation()
    {
        var transactions = new ScriptedLocalTransactionFactory();
        transactions.Enqueue();
        await using ILocalTransaction owner = await transactions.BeginAsync("Ledger");
        var scripts = new ScriptedDurableOperationService();
        scripts.EnqueueExecution<int, string>(new(OperationExecutionDisposition.KeyConflict));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => scripts.GetResultAsync<string>(new("Debit", "scope", "key")));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => scripts.ExecuteAsync(owner.Context,
            new OperationRequest<string>("Debit", "scope", "key", "wrong-input-type"), (_, _, _) => Task.FromResult(OperationDecision<string>.Succeed("wrong"))));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => scripts.ExecuteAsync(owner.Context, Request(),
            (_, _, _) => Task.FromResult(OperationDecision<int>.Succeed(1))));
        Assert.AreEqual(1, scripts.PendingScripts);
        Assert.IsEmpty(scripts.Calls);
        Assert.AreEqual(OperationExecutionDisposition.KeyConflict, (await scripts.ExecuteAsync(owner.Context, Request(), Never)).Disposition);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => scripts.ExecuteAsync(owner.Context, Request(), Never));
        Assert.HasCount(1, scripts.Calls);
    }

    [TestMethod]
    public async Task OriginalFaultsIncludingUnrelatedCancellationRemainFaultedAndPreCancellationConsumesNothing()
    {
        var transactions = new ScriptedLocalTransactionFactory();
        transactions.Enqueue();
        await using ILocalTransaction owner = await transactions.BeginAsync("Ledger");
        var scripts = new ScriptedDurableOperationService();
        var original = new OperationCanceledException("Scripted unrelated cancellation fault");
        scripts.EnqueueExecutionFailure<int, string>(original);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Task<OperationExecution<string>> canceled = scripts.ExecuteAsync(owner.Context, Request(), Never, cancellation.Token);
        OperationCanceledException observedCancellation = await Assert.ThrowsAsync<OperationCanceledException>(() => canceled);
        Assert.AreEqual(cancellation.Token, observedCancellation.CancellationToken);
        Assert.IsTrue(canceled.IsCanceled);
        Assert.AreEqual(1, scripts.PendingScripts);
        Assert.IsEmpty(scripts.Calls);
        Task<OperationExecution<string>> faulted = scripts.ExecuteAsync(owner.Context, Request(), Never);
        Assert.AreSame(original, await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => faulted));
        Assert.IsTrue(faulted.IsFaulted);
        var lookupOriginal = new InvalidOperationException("Original lookup fault");
        scripts.EnqueueLookupFailure<string>(lookupOriginal);
        Assert.AreSame(lookupOriginal, await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => scripts.GetResultAsync<string>(new("Debit", "scope", "key"))));
        Assert.HasCount(2, scripts.Calls);
    }

    [TestMethod]
    public async Task DefaultScriptAndRecordingBoundsAreIndependentAndNeverEvictOrConsumeOnExhaustion()
    {
        var scripts = new ScriptedDurableOperationService();
        for (int index = 0; index < 16; index++)
        {
            scripts.EnqueueLookup<string>(new(OperationLookupDisposition.NotFound));
        }
        Assert.ThrowsExactly<InvalidOperationException>(() => scripts.EnqueueLookup<string>(new(OperationLookupDisposition.NotFound)));
        Assert.AreEqual(16, scripts.PendingScripts);
        for (int index = 0; index < 16; index++)
        {
            await scripts.GetResultAsync<string>(new("Debit", "scope", "key-" + index));
        }
        scripts.EnqueueLookup<string>(new(OperationLookupDisposition.NotFound));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => scripts.GetResultAsync<string>(new("Debit", "scope", "seventeenth")));
        Assert.AreEqual(1, scripts.PendingScripts);
        Assert.HasCount(16, scripts.Calls);
        Assert.AreEqual("key-0", scripts.Calls[0].Key);
    }

    [TestMethod]
    [DataRow(0, 1)]
    [DataRow(65_537, 1)]
    [DataRow(1, 0)]
    [DataRow(1, 65_537)]
    public void ConfiguredCapacitiesMustRemainFiniteAndPositive(int scripts, int records)
        => Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ScriptedDurableOperationService(scripts, records));

    private static OperationRequest<int> Request() => new("Debit", "scope", "key", 1);
    private static Task<OperationDecision<string>> Never(ILocalTransactionContext _, int input, CancellationToken token)
        => throw new AssertFailedException("A scripted service must not execute an application handler.");

    private sealed class MutableInput
    {
        internal int Amount;
    }
}
