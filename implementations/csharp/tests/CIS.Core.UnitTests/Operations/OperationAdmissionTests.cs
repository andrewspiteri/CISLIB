using CIS.Operations;
using CIS.Operations.Hosting;
using CIS.Transactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Operations;

[TestClass]
public sealed class OperationAdmissionTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [TestMethod]
    public async Task OneMarkerIsSharedAcrossInstancesDefinitionsAndGenericTypesAndNeverReset()
    {
        using var fixture = new OperationRuntimeFixture(services: services => services.AddCisOperation(
            new OperationDefinition<int, int>("Other", "Ledger", new("other-request", 1), new IntNormalizer(), new IntCodec(),
                _ => new RuntimeResolver(), new OperationDefinitionOptions
                {
                    ExecuteCapabilities = ["other.submit"],
                    LookupCapabilities = ["other.read"]
                })));
        await using ILocalTransaction owner = await fixture.Begin();
        Assert.AreEqual(OperationExecutionDisposition.Prepared,
            (await fixture.Service.ExecuteAsync(owner.Context, OperationRuntimeFixture.Request(), OperationRuntimeFixture.Success)).Disposition);
        using IServiceScope otherScope = fixture.Provider.CreateScope();
        IDurableOperationService other = otherScope.ServiceProvider.GetRequiredService<IDurableOperationService>();
        Assert.AreNotSame(fixture.Service, other);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => other.ExecuteAsync(owner.Context,
            new OperationRequest<int>("Other", "scope", "other-key", 1), (_, value, _) => Task.FromResult(OperationDecision<int>.Succeed(value))));
        Assert.AreEqual(1, fixture.Journal.Claims);
        Assert.AreEqual(TransactionOutcome.Committed, (await owner.CommitAsync()).Outcome);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Service.ExecuteAsync(owner.Context,
            OperationRuntimeFixture.Request(key: "another-key"), OperationRuntimeFixture.Success));
    }

    [TestMethod]
    public async Task ConcurrentServiceAndNestedCallsRejectWithoutInvokingAnotherHandler()
    {
        using var fixture = new OperationRuntimeFixture();
        await using ILocalTransaction owner = await fixture.Begin();
        using IServiceScope otherScope = fixture.Provider.CreateScope();
        IDurableOperationService other = otherScope.ServiceProvider.GetRequiredService<IDurableOperationService>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int unexpectedHandlers = 0;
        Task<OperationExecution<string>> admitted = fixture.Service.ExecuteAsync(owner.Context, OperationRuntimeFixture.Request(), async (context, _, _) =>
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => other.ExecuteAsync(context,
                OperationRuntimeFixture.Request(key: "nested"), (_, _, _) =>
                {
                    unexpectedHandlers++;
                    return Task.FromResult(OperationDecision<string>.Succeed("wrong"));
                }));
            entered.SetResult();
            await release.Task;
            return OperationDecision<string>.Succeed("original");
        });
        try
        {
            await entered.Task.WaitAsync(Deadline);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => other.ExecuteAsync(owner.Context,
                OperationRuntimeFixture.Request(key: "overlap"), (_, _, _) =>
                {
                    unexpectedHandlers++;
                    return Task.FromResult(OperationDecision<string>.Succeed("wrong"));
                }));
            Assert.AreEqual(1, fixture.Journal.Claims);
            Assert.AreEqual(0, fixture.Journal.Writes);
        }
        finally
        {
            release.TrySetResult();
        }
        Assert.AreEqual(OperationExecutionDisposition.Prepared, (await admitted.WaitAsync(Deadline)).Disposition);
        Assert.AreEqual(0, unexpectedHandlers);
        Assert.IsFalse(owner.Context.IsRollbackOnly, "Caught rejected admission does not poison the admitted attempt.");
    }

    [TestMethod]
    public async Task UncaughtNestedAdmissionFaultPoisonsTheOuterAttempt()
    {
        using var fixture = new OperationRuntimeFixture();
        await using ILocalTransaction owner = await fixture.Begin();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Service.ExecuteAsync(owner.Context,
            OperationRuntimeFixture.Request(), async (context, _, _) =>
            {
                await fixture.Service.ExecuteAsync(context, OperationRuntimeFixture.Request(key: "nested"), OperationRuntimeFixture.Success);
                return OperationDecision<string>.Succeed("must-not-prepare");
            }));
        Assert.IsTrue(owner.Context.IsRollbackOnly);
        Assert.AreEqual(1, fixture.Journal.Claims);
        Assert.AreEqual(0, fixture.Journal.Writes);
    }

    [TestMethod]
    [DataRow(OperationClaimDisposition.MissingAfterConflict, OperationExecutionDisposition.Contended)]
    [DataRow(OperationClaimDisposition.Contended, OperationExecutionDisposition.Contended)]
    [DataRow(OperationClaimDisposition.RetryRequired, OperationExecutionDisposition.RetryRequired)]
    public async Task NonOwningArbitrationNeverRetriesAndStillSpendsAdmission(OperationClaimDisposition claim,
        OperationExecutionDisposition expected)
    {
        using var fixture = new OperationRuntimeFixture();
        await using ILocalTransaction owner = await fixture.Begin();
        fixture.Journal.Claim = (_, _, _, _) => Task.FromResult(new OperationClaim(claim));
        OperationExecution<string> observed = await fixture.Service.ExecuteAsync<RuntimeInput, string>(owner.Context, OperationRuntimeFixture.Request(), (_, _, _) =>
            throw new AssertFailedException("No claim means no business handler."));
        Assert.AreEqual(expected, observed.Disposition);
        Assert.AreEqual(claim != OperationClaimDisposition.MissingAfterConflict, owner.Context.IsRollbackOnly);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Service.ExecuteAsync(owner.Context,
            OperationRuntimeFixture.Request(), OperationRuntimeFixture.Success));
        Assert.AreEqual(1, fixture.Journal.Claims);
        Assert.AreEqual(0, fixture.Journal.Writes);
    }

    private sealed class IntNormalizer : IOperationRequestNormalizer<int>
    {
        public NormalizedOperationInput<int> Normalize(int input, OperationScope scope)
            => new(input, [new(new("other-request", 1), OperationCanonicalValue.Integer(input))]);
    }

    private sealed class IntCodec : IOperationResultCodec<int>
    {
        public OperationContract CurrentContract => new("other-result", 1);
        public IReadOnlyList<OperationContract> ReadableContracts => [CurrentContract];
        public byte[] Encode(int value) => BitConverter.GetBytes(value);
        public bool TryDecode(OperationContract contract, ReadOnlyMemory<byte> value, out int decoded)
        {
            decoded = BitConverter.ToInt32(value.Span);
            return true;
        }
    }
}
