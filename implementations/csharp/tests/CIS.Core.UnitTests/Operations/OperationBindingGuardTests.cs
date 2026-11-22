using CIS.Operations;
using CIS.Security;
using CIS.Transactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Operations;

[TestClass]
public sealed class OperationBindingGuardTests
{
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task ActualResolverDriftPoisonsTheOriginalOwnerBeforeAuthorizationJournalOrEffects(bool scopeDrifts)
    {
        using var fixture = new OperationRuntimeFixture();
        await using ILocalTransaction owner = await fixture.Begin();
        LocalApplicationIdentity local = fixture.Provider.GetRequiredService<LocalApplicationIdentity>();
        OperationScope expected = fixture.Resolver.Scope;
        var identity = new OperationIdentity(local.Application, local.Environment, expected.Scope, "Debit", "key");
        var resource = new CapabilityTarget(local.Application, local.Environment, expected.ResourceType, expected.ResourceIdentifier);
        using IDisposable guard = OperationAdmission.ExpectBinding(owner.Context, identity, resource);
        fixture.Resolver.Scope = scopeDrifts ? new("another-scope", expected.ResourceType, expected.ResourceIdentifier)
            : new(expected.Scope, expected.ResourceType, "another-resource");
        int effects = 0;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Service.ExecuteAsync(owner.Context,
            OperationRuntimeFixture.Request(), (context, input, token) =>
            {
                effects++;
                return OperationRuntimeFixture.Success(context, input, token);
            }));
        Assert.AreEqual(0, effects);
        Assert.AreEqual(0, fixture.Authorizer.Calls);
        Assert.AreEqual(0, fixture.Normalizer.Calls);
        Assert.AreEqual(0, fixture.Journal.Claims);
        Assert.AreEqual(0, fixture.Journal.Writes);
        Assert.IsTrue(owner.Context.IsRollbackOnly);
        Assert.AreEqual("operations.scope_binding_changed", owner.Context.RollbackReasonCode);
        Assert.AreEqual(TransactionOutcome.NotCommitted, (await owner.CommitAsync()).Outcome);
    }

    [TestMethod]
    public async Task MatchingGuardPreservesPreparedOriginalContextAndDoesNotResetOneShotAdmission()
    {
        using var fixture = new OperationRuntimeFixture();
        await using ILocalTransaction owner = await fixture.Begin();
        LocalApplicationIdentity local = fixture.Provider.GetRequiredService<LocalApplicationIdentity>();
        OperationScope expected = fixture.Resolver.Scope;
        var identity = new OperationIdentity(local.Application, local.Environment, expected.Scope, "Debit", "key");
        var resource = new CapabilityTarget(local.Application, local.Environment, expected.ResourceType, expected.ResourceIdentifier);
        using (OperationAdmission.ExpectBinding(owner.Context, identity, resource))
        {
            OperationExecution<string> execution = await fixture.Service.ExecuteAsync(owner.Context,
                OperationRuntimeFixture.Request(), OperationRuntimeFixture.Success);
            Assert.AreEqual(OperationExecutionDisposition.Prepared, execution.Disposition);
        }
        Assert.AreSame(owner.Context, fixture.Journal.ClaimedContext);
        Assert.AreSame(owner.Context, fixture.Journal.WrittenContext);
        Assert.ThrowsExactly<InvalidOperationException>(() => OperationAdmission.ExpectBinding(owner.Context, identity, resource));
        Assert.IsNull(owner.Completion);
        Assert.AreEqual(TransactionOutcome.Committed, (await owner.CommitAsync()).Outcome);
    }
}
