using System.Text;
using CIS.Operations;
using CIS.Operations.Hosting;
using CIS.Security;
using CIS.Transactions;
using CIS.Transactions.AdoNet;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Operations;

[TestClass]
public sealed class OperationExecutionTests
{
    [TestMethod]
    public async Task PreparedUsesOriginalContextOwnedInputAndDecodedStoredBytesWithoutCompletingOwner()
    {
        using var fixture = new OperationRuntimeFixture();
        await using ILocalTransaction owner = await fixture.Begin();
        OperationRequest<RuntimeInput> request = OperationRuntimeFixture.Request();
        fixture.Journal.Claim = async (context, _, _, token) =>
        {
            await context.UseAdoNetAsync((_, _, _) => Task.CompletedTask, token);
            request.Input.Amount = 999;
            return new(OperationClaimDisposition.Acquired);
        };
        fixture.Codec.Encoder = value => Encoding.UTF8.GetBytes(value!.ToUpperInvariant());
        fixture.Codec.Decoder = (_, bytes) => "decoded:" + Encoding.UTF8.GetString(bytes.Span);
        OperationExecution<string> execution = await fixture.Service.ExecuteAsync(owner.Context, request, async (context, input, token) =>
        {
            Assert.AreSame(owner.Context, context);
            Assert.AreNotSame(request.Input, input);
            Assert.AreEqual(10, input.Amount);
            await context.UseAdoNetAsync((_, _, _) => Task.CompletedTask, token);
            return OperationDecision<string>.Succeed("generated-receipt");
        });
        Assert.AreEqual(OperationExecutionDisposition.Prepared, execution.Disposition);
        Assert.AreEqual("decoded:GENERATED-RECEIPT", execution.Decision!.Value);
        Assert.AreEqual("GENERATED-RECEIPT", Encoding.UTF8.GetString(fixture.Journal.Stored!.Value!.Value.Span));
        Assert.AreSame(owner.Context, fixture.Journal.ClaimedContext);
        Assert.AreSame(owner.Context, fixture.Journal.WrittenContext);
        Assert.AreEqual("trusted-account", fixture.Journal.Identity!.Scope);
        Assert.AreEqual("actual-account", fixture.Authorizer.Target!.ResourceIdentifier);
        Assert.AreEqual("account", fixture.Authorizer.Target.ResourceType);
        LocalApplicationIdentity identity = fixture.Provider.GetRequiredService<LocalApplicationIdentity>();
        Assert.AreEqual(identity.Application, fixture.Journal.Identity.Application);
        Assert.AreEqual(identity.Environment, fixture.Journal.Identity.Environment);
        CollectionAssert.AreEqual(new[] { "ledger.submit", "ledger.read" }, fixture.Authorizer.Capabilities!.ToArray());
        Assert.IsNull(owner.Completion);
        Assert.AreEqual(0, fixture.Source.Connections.Single().Transactions.Single().CommitCalls);
        Assert.AreEqual(TransactionOutcome.Committed, (await owner.CommitAsync()).Outcome);
    }

    [TestMethod]
    public async Task FinalBusinessRejectionCanBePreparedAndCommittedWithoutSuccessEncoding()
    {
        using var fixture = new OperationRuntimeFixture();
        await using ILocalTransaction owner = await fixture.Begin();
        OperationExecution<string> execution = await fixture.Service.ExecuteAsync(owner.Context, OperationRuntimeFixture.Request(),
            (_, _, _) => Task.FromResult(OperationDecision<string>.Reject("balance.low", "Original business decision")));
        Assert.AreEqual(OperationExecutionDisposition.Prepared, execution.Disposition);
        Assert.AreEqual(OperationBusinessDecision.Rejected, execution.Decision!.Decision);
        Assert.AreEqual("balance.low", execution.Decision.Error!.Code);
        Assert.AreEqual("Original business decision", fixture.Journal.Stored!.Error!.Message);
        Assert.AreEqual(0, fixture.Codec.Encodes);
        Assert.AreEqual(0, fixture.Codec.Decodes);
        Assert.IsFalse(owner.Context.IsRollbackOnly);
        Assert.AreEqual(TransactionOutcome.Committed, (await owner.CommitAsync()).Outcome);
    }

    [TestMethod]
    public async Task HandlerRollbackOnlyPreservesFirstReasonAndNeverFinalizesOrPrepares()
    {
        using var fixture = new OperationRuntimeFixture();
        await using ILocalTransaction owner = await fixture.Begin();
        OperationExecution<string> execution = await fixture.Service.ExecuteAsync(owner.Context, OperationRuntimeFixture.Request(), (context, _, _) =>
        {
            context.MarkRollbackOnly("business.declined_with_partial_effects");
            context.MarkRollbackOnly("later.reason");
            return Task.FromResult(OperationDecision<string>.Succeed("must-not-prepare"));
        });
        Assert.AreEqual(OperationExecutionDisposition.RollbackOnly, execution.Disposition);
        Assert.AreEqual("business.declined_with_partial_effects", owner.Context.RollbackReasonCode);
        Assert.AreEqual(0, fixture.Journal.Writes);
        Assert.AreEqual(TransactionOutcome.NotCommitted, (await owner.CommitAsync()).Outcome);
    }

    [TestMethod]
    public async Task DenialAndAuthorizationFaultsDoNotNormalizeTouchJournalOrSpendAdmission()
    {
        using var fixture = new OperationRuntimeFixture();
        await using ILocalTransaction owner = await fixture.Begin();
        fixture.Caller.Current = new("copied", "serialized", new("untrusted-issuer", "copied-subject"),
            SecurityActorKind.Application, "caller-application", "caller-environment",
            new(DateTimeOffset.UtcNow), "copied-policy");
        fixture.Authorizer.Handler = _ => ValueTask.FromResult(AuthorizationDecision.Deny(new(SecurityFailureKind.CapabilityDenied, "Denied")));
        OperationExecution<string> denied = await fixture.Service.ExecuteAsync(owner.Context, OperationRuntimeFixture.Request(), OperationRuntimeFixture.Success);
        Assert.AreEqual(OperationExecutionDisposition.Denied, denied.Disposition);
        Assert.AreEqual(0, fixture.Normalizer.Calls);
        Assert.AreEqual(0, fixture.Journal.Claims);
        Assert.AreSame(fixture.Caller.Current, fixture.Resolver.ObservedCaller);
        var original = new InvalidOperationException("Awaited authorization/audit fault");
        fixture.Authorizer.Handler = _ => ValueTask.FromException<AuthorizationDecision>(original);
        Exception observed = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Service.ExecuteAsync(owner.Context,
            OperationRuntimeFixture.Request(), OperationRuntimeFixture.Success));
        Assert.AreSame(original, observed);
        Assert.IsFalse(owner.Context.IsRollbackOnly);
        fixture.Authorizer.Handler = null;
        Assert.AreEqual(OperationExecutionDisposition.Prepared,
            (await fixture.Service.ExecuteAsync(owner.Context, OperationRuntimeFixture.Request(), OperationRuntimeFixture.Success)).Disposition);
    }

    [TestMethod]
    [DataRow("handler")]
    [DataRow("encoder")]
    [DataRow("decoder")]
    [DataRow("write")]
    [DataRow("claim")]
    public async Task AdmittedFaultsPoisonAndPreserveTheOriginalException(string phase)
    {
        using var fixture = new OperationRuntimeFixture();
        await using ILocalTransaction owner = await fixture.Begin();
        var original = new InvalidOperationException("Controlled " + phase + " fault");
        Func<ILocalTransactionContext, RuntimeInput, CancellationToken, Task<OperationDecision<string>>> handler = OperationRuntimeFixture.Success;
        if (phase == "handler")
        {
            handler = (_, _, _) => Task.FromException<OperationDecision<string>>(original);
        }
        if (phase == "encoder")
        {
            fixture.Codec.Encoder = _ => throw original;
        }
        if (phase == "decoder")
        {
            fixture.Codec.Decoder = (_, _) => throw original;
        }
        if (phase == "write")
        {
            fixture.Journal.Write = (_, _) => Task.FromException(original);
        }
        if (phase == "claim")
        {
            fixture.Journal.Claim = (_, _, _, _) => Task.FromException<OperationClaim>(original);
        }
        Exception observed = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Service.ExecuteAsync(owner.Context,
            OperationRuntimeFixture.Request(), handler));
        Assert.AreSame(original, observed);
        Assert.IsTrue(owner.Context.IsRollbackOnly);
        Assert.IsTrue(owner.Context.RollbackReasonCode!.StartsWith("operations.", StringComparison.Ordinal));
        Assert.AreEqual(TransactionOutcome.NotCommitted, (await owner.CommitAsync()).Outcome);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Service.ExecuteAsync(owner.Context,
            OperationRuntimeFixture.Request(), OperationRuntimeFixture.Success));
    }

    [TestMethod]
    [DataRow("handler")]
    [DataRow("encode")]
    [DataRow("write")]
    public async Task CancellationBetweenClaimAndReturnPoisonsWithoutPrepared(string phase)
    {
        using var fixture = new OperationRuntimeFixture();
        await using ILocalTransaction owner = await fixture.Begin();
        using var cancellation = new CancellationTokenSource();
        if (phase == "encode")
        {
            fixture.Codec.Encoder = value =>
            {
                cancellation.Cancel();
                return Encoding.UTF8.GetBytes(value!);
            };
        }
        if (phase == "write")
        {
            fixture.Journal.Write = (_, _) =>
            {
                cancellation.Cancel();
                return Task.CompletedTask;
            };
        }
        OperationCanceledException observed = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => fixture.Service.ExecuteAsync(
            owner.Context, OperationRuntimeFixture.Request(), (_, _, _) =>
            {
                if (phase == "handler")
                {
                    cancellation.Cancel();
                }
                return Task.FromResult(OperationDecision<string>.Succeed("receipt"));
            }, cancellation.Token));
        Assert.AreEqual(cancellation.Token, observed.CancellationToken);
        Assert.IsTrue(owner.Context.IsRollbackOnly);
        Assert.AreEqual(phase == "write" ? 1 : 0, fixture.Journal.Writes);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task OversizedOrUndecodableCurrentOutputNeverFinalizes(bool oversized)
    {
        using var fixture = new OperationRuntimeFixture(options => options.MaximumResultBytes = 16);
        await using ILocalTransaction owner = await fixture.Begin();
        if (oversized)
        {
            fixture.Codec.Encoder = _ => new byte[17];
        }
        else
        {
            fixture.Codec.DecodeSupported = false;
        }
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Service.ExecuteAsync(owner.Context,
            OperationRuntimeFixture.Request(), OperationRuntimeFixture.Success));
        Assert.IsTrue(owner.Context.IsRollbackOnly);
        Assert.AreEqual(0, fixture.Journal.Writes);
    }

    [TestMethod]
    public async Task InvalidPreflightNormalizationAndCancellationLeaveAdmissionAvailable()
    {
        using var fixture = new OperationRuntimeFixture();
        await using ILocalTransaction owner = await fixture.Begin();
        IDurableOperationService service = fixture.Service;
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.ExecuteAsync(owner.Context,
            new OperationRequest<RuntimeInput>("Unknown", "scope", "key", new(1)), OperationRuntimeFixture.Success));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.ExecuteAsync(owner.Context,
            new OperationRequest<int>("Debit", "scope", "key", 1), (_, _, _) => Task.FromResult(OperationDecision<string>.Succeed("wrong-type"))));
        var original = new InvalidOperationException("normalizer fault");
        fixture.Normalizer.Handler = (_, _) => throw original;
        Assert.AreSame(original, await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.ExecuteAsync(owner.Context,
            OperationRuntimeFixture.Request(), OperationRuntimeFixture.Success)));
        fixture.Normalizer.Handler = null;
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => service.ExecuteAsync(owner.Context,
            OperationRuntimeFixture.Request(), OperationRuntimeFixture.Success, canceled.Token));
        Assert.AreEqual(0, fixture.Journal.Claims);
        Assert.IsFalse(owner.Context.IsRollbackOnly);
        Assert.AreEqual(OperationExecutionDisposition.Prepared,
            (await service.ExecuteAsync(owner.Context, OperationRuntimeFixture.Request(), OperationRuntimeFixture.Success)).Disposition);
    }

    [TestMethod]
    [DataRow(TransactionIsolation.RepeatableRead)]
    [DataRow(TransactionIsolation.Serializable)]
    public async Task UnsupportedIsolationAndWrongDatabaseRejectBeforeNativeWork(TransactionIsolation isolation)
    {
        using var fixture = new OperationRuntimeFixture();
        await using ILocalTransaction owner = await fixture.Transactions.BeginAsync("Ledger", new LocalTransactionOptions { Isolation = isolation });
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => fixture.Service.ExecuteAsync(owner.Context,
            OperationRuntimeFixture.Request(), OperationRuntimeFixture.Success));
        var wrongDatabase = new RuntimeContext { DatabaseName = "Other" };
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => fixture.Service.ExecuteAsync(wrongDatabase,
            OperationRuntimeFixture.Request(), OperationRuntimeFixture.Success));
        Assert.AreEqual(0, fixture.Journal.Claims);
        Assert.AreEqual(0, fixture.Authorizer.Calls);
        Assert.IsFalse(owner.Context.IsRollbackOnly);
        Assert.AreEqual(isolation, owner.Context.Isolation);
    }

    [TestMethod]
    public async Task RegistrationSnapshotsCodecMetadataAndRejectsLateOpaqueDescriptorTampering()
    {
        using var fixture = new OperationRuntimeFixture();
        fixture.Codec.CurrentContract = new("changed", 999);
        fixture.Codec.ReadableContracts = [new("changed", 999)];
        await using ILocalTransaction owner = await fixture.Begin();
        Assert.AreEqual(OperationExecutionDisposition.Prepared,
            (await fixture.Service.ExecuteAsync(owner.Context, OperationRuntimeFixture.Request(), OperationRuntimeFixture.Success)).Disposition);
        Assert.AreEqual(new OperationContract("debit-result", 1), fixture.Journal.Stored!.Contract);

        bool opaqueResolved = false;
        using var tampered = new OperationRuntimeFixture(services: services => services.AddSingleton<OperationDefinition>(_ =>
        {
            opaqueResolved = true;
            throw new AssertFailedException("Must reject passive metadata before resolving this opaque factory.");
        }));
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = tampered.Service);
        Assert.IsFalse(opaqueResolved);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CollectionChangesAfterProviderBuildCannotDivergeRuntimeAndCapturedHostMetadata(bool replacement)
    {
        using var fixture = new OperationRuntimeFixture();
        string name = replacement ? "Debit" : "Another";
        var added = new OperationDefinition<RuntimeInput, string>(name, "Ledger", new("debit-request", 1),
            new RuntimeNormalizer(), new RuntimeCodec(), _ => new RuntimeResolver(), new OperationDefinitionOptions
            {
                ExecuteCapabilities = ["ledger.submit"],
                LookupCapabilities = ["ledger.read"]
            });
        if (replacement)
        {
            ServiceDescriptor previous = fixture.Services.Single(item => item.ServiceType == typeof(OperationDefinition));
            fixture.Services.Remove(previous);
            fixture.Services.AddSingleton<OperationDefinition>(added);
        }
        else
        {
            fixture.Services.AddCisOperation(added);
        }
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = fixture.Service);
        CollectionAssert.AreEqual(new[] { "Debit" }, fixture.Provider.GetRequiredService<OperationsComposition>().OperationNames.ToArray());
    }

    [TestMethod]
    public async Task CanonicalOverflowBeforeClaimDoesNotPoisonOrSpendAdmission()
    {
        using var fixture = new OperationRuntimeFixture(options => options.MaximumCanonicalBytes = 128);
        await using ILocalTransaction owner = await fixture.Begin();
        fixture.Normalizer.Handler = (input, _) => new(new(input.Amount),
            [new(new("debit-request", 1), OperationCanonicalValue.Text(new string('x', 200)))]);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => fixture.Service.ExecuteAsync(owner.Context,
            OperationRuntimeFixture.Request(), OperationRuntimeFixture.Success));
        Assert.IsFalse(owner.Context.IsRollbackOnly);
        Assert.AreEqual(0, fixture.Journal.Claims);
        fixture.Normalizer.Handler = null;
        Assert.AreEqual(OperationExecutionDisposition.Prepared,
            (await fixture.Service.ExecuteAsync(owner.Context, OperationRuntimeFixture.Request(), OperationRuntimeFixture.Success)).Disposition);
    }
}
