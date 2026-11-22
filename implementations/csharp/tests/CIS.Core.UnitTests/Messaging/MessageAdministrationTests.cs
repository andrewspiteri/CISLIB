using CIS.Messaging;
using CIS.Security;
using CIS.Transactions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Messaging;

[TestClass]
[DoNotParallelize]
public sealed class MessageAdministrationTests
{
    [TestMethod]
    public async Task DisabledAndDeniedAdministrationDiscloseNoStoreMetadataAndBeginNoTransaction()
    {
        await using var disabled = new DispositionUnitFixture();
        var authority = new AdministrationUnitAuthority();
        MessageAdministration admin = Create(disabled, authority);
        Assert.AreEqual(MessageAdministrationDisposition.Disabled, (await admin.InspectAsync(new())).Disposition);
        Assert.AreEqual(MessageAdministrationDisposition.Disabled, (await admin.MaintainAsync()).Disposition);
        Assert.AreEqual(0, authority.Calls);
        Assert.AreEqual(0, disabled.Store.Calls);
        Assert.AreEqual(0, disabled.Transactions.CreatedTransactions.Count);
        await disabled.DisposeAsync();

        await using var denied = new DispositionUnitFixture(Options());
        authority.Allowed = false;
        admin = Create(denied, authority);
        MessageInspectResult inspection = await admin.InspectAsync(new());
        MessageAdminRedriveResult redrive = await admin.RedriveAsync(new("Countries", Guid.NewGuid(), 1, "operator.reviewed"));
        Assert.AreEqual(MessageAdministrationDisposition.Denied, inspection.Disposition);
        Assert.IsNull(inspection.Records);
        Assert.AreEqual(MessageAdministrationDisposition.Denied, redrive.Disposition);
        Assert.AreEqual(0, denied.Store.Calls);
        Assert.AreEqual(0, denied.Transactions.CreatedTransactions.Count);
    }

    [TestMethod]
    public async Task ExactInspectionUsesCapturedOwnerAndExposesOnlySafeVersionedMetadata()
    {
        await using var fixture = new DispositionUnitFixture(Options());
        fixture.Script(TransactionOutcome.Committed);
        MessageProcessingAttempt attempt = fixture.Failure().Attempt!;
        fixture.Store.Processing = new(attempt.Key, attempt.Message.Digest, attempt.Provenance, 3,
            MessageProcessingDisposition.Terminal, 9, 2, DateTimeOffset.UnixEpoch);
        var authority = new AdministrationUnitAuthority();
        MessageInspectResult result = await Create(fixture, authority).InspectAsync(new(8, "Countries", attempt.Key.MessageId));
        Assert.AreEqual(MessageAdministrationDisposition.Executed, result.Disposition);
        Assert.AreEqual(9L, result.Processing!.Version);
        Assert.AreEqual(2, result.Processing.Generation);
        Assert.AreEqual(fixture.Identity.Application, fixture.Store.Inspection!.Application);
        Assert.AreEqual(fixture.Identity.Environment, fixture.Store.Inspection.Environment);
        Assert.AreEqual(fixture.Identity.Application, authority.Target!.Application);
        Assert.AreEqual("messaging.store", authority.Target.ResourceType);
        Assert.AreEqual("Messaging", authority.Target.ResourceIdentifier);
        Assert.AreSame(fixture.Transactions.CreatedTransactions.Single().Context, fixture.Store.Context);
    }

    [TestMethod]
    [DataRow(TransactionOutcome.Committed)]
    [DataRow(TransactionOutcome.NotCommitted)]
    [DataRow(TransactionOutcome.Unknown)]
    public async Task RedriveKeepsExpectedVersionCurrentHorizonActualCallerAndActualPersistenceSeparate(TransactionOutcome outcome)
    {
        var retention = new MessageRetentionPolicy(supportedRedriveHorizon: TimeSpan.FromDays(10));
        MessagingOptions options = Options();
        options.Retention = retention;
        await using var fixture = new DispositionUnitFixture(options);
        fixture.Script(outcome);
        var authority = new AdministrationUnitAuthority();
        Guid messageId = Guid.NewGuid();
        MessageAdminRedriveResult result = await Create(fixture, authority).RedriveAsync(new("Countries", messageId, 7, "operator.reviewed"));
        Assert.AreEqual(outcome, result.Completion!.Outcome);
        Assert.AreEqual(MessageRedriveDisposition.Prepared, result.Transition);
        Assert.AreEqual(outcome == TransactionOutcome.Committed ? 1 : 0, fixture.Wakes);
        Assert.AreEqual(7L, fixture.Store.Redrive!.ExpectedVersion);
        Assert.AreEqual(retention.SupportedRedriveHorizon, fixture.Store.Redrive.SupportedHorizon);
        Assert.AreEqual("events", fixture.Store.Redrive.Destination.RouteAlias);
        Assert.AreEqual("messaging.subscription", authority.Target!.ResourceType);
        Assert.AreEqual("Countries", authority.Target.ResourceIdentifier);
        Assert.AreSame(authority.Current, authority.LastAudit!.Caller);
        Assert.AreEqual(messageId.ToString("D"), authority.LastAudit.CorrelationId);
        Assert.AreEqual(outcome == TransactionOutcome.Committed ? SecurityOperationOutcome.Completed : SecurityOperationOutcome.Failed,
            authority.LastAudit.Outcome);
    }

    [TestMethod]
    public async Task MaintenanceNeverConfirmsUnknownCountsAndRejectsAnInvalidStoreBatchBeforeCommit()
    {
        await using var fixture = new DispositionUnitFixture(Options());
        fixture.Script(TransactionOutcome.Unknown, TransactionOutcome.Committed);
        var authority = new AdministrationUnitAuthority();
        MessageAdministration admin = Create(fixture, authority);
        MessageAdminMaintenanceResult result = await admin.MaintainAsync(4);
        Assert.AreEqual(TransactionOutcome.Unknown, result.Completion!.Outcome);
        Assert.IsNotNull(result.CandidateCounts);
        Assert.IsNull(result.ConfirmedCounts);
        Assert.AreSame(fixture.Snapshot.Settings.Retention, fixture.Store.Maintenance!.Policy);
        fixture.Store.Counts = new(5, 0, 0);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => admin.MaintainAsync(4));
        Assert.AreEqual(TransactionOutcome.NotCommitted, fixture.Transactions.CreatedTransactions[1].Completion!.Outcome);
    }

    [TestMethod]
    public async Task SharedCapacityRefusalBeginsNoRedriveAndRuntimeDrainJoinsAcceptedAdministrativeWork()
    {
        MessagingOptions options = Options();
        options.MaximumConcurrentDeliveries = 1;
        await using var fixture = new DispositionUnitFixture(options);
        fixture.Script(TransactionOutcome.Committed);
        var authority = new AdministrationUnitAuthority();
        MessageAdministration admin = Create(fixture, authority);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Store.Hold = async () =>
        {
            entered.TrySetResult();
            await release.Task;
        };
        Task<MessageInspectResult> accepted = admin.InspectAsync(new());
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(MessageAdministrationDisposition.Unavailable,
            (await admin.RedriveAsync(new("Countries", Guid.NewGuid(), 1, "operator.reviewed"))).Disposition);
        Task draining = fixture.Resources.DisposeAsync().AsTask();
        try
        {
            Assert.IsFalse(draining.IsCompleted);
            Assert.AreEqual(1, fixture.Transactions.CreatedTransactions.Count);
        }
        finally
        {
            release.TrySetResult();
        }
        Assert.AreEqual(MessageAdministrationDisposition.Executed, (await accepted).Disposition);
        await draining.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(MessageAdministrationDisposition.Unavailable, (await admin.InspectAsync(new())).Disposition);
    }

    [TestMethod]
    public async Task HostCutoffRefusesEveryNewAdminEntryWhileAnAcceptedOwnerStillSettles()
    {
        await using var fixture = new DispositionUnitFixture(Options());
        fixture.Script(TransactionOutcome.Committed);
        var authority = new AdministrationUnitAuthority();
        bool accepting = true;
        var admin = new MessageAdministration(fixture.Snapshot, fixture.Store, fixture.Transactions, fixture.Identity,
            authority, authority, authority, fixture.Resources, accepting: () => accepting);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Store.Hold = async () =>
        {
            entered.TrySetResult();
            await release.Task;
        };
        Task<MessageInspectResult> accepted = admin.InspectAsync(new());
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        accepting = false;
        try
        {
            Assert.AreEqual(MessageAdministrationDisposition.Unavailable, (await admin.InspectAsync(new())).Disposition);
            Assert.AreEqual(MessageAdministrationDisposition.Unavailable, (await admin.MaintainAsync()).Disposition);
            Assert.AreEqual(MessageAdministrationDisposition.Unavailable,
                (await admin.RedriveAsync(new("Countries", Guid.NewGuid(), 1, "operator.reviewed"))).Disposition);
            Assert.AreEqual(1, authority.Calls);
            Assert.AreEqual(1, fixture.Store.Calls);
            Assert.IsFalse(accepted.IsCompleted);
        }
        finally
        {
            release.TrySetResult();
        }
        Assert.AreEqual(TransactionOutcome.Committed, (await accepted).Completion!.Outcome);
        Assert.IsTrue(fixture.Transactions.CreatedTransactions.Single().DisposeRequested);
    }

    [TestMethod]
    public async Task EncodedBytePressureRefusesRedriveBeforeOpeningItsDatabaseOwner()
    {
        await using var fixture = new DispositionUnitFixture(Options());
        var authority = new AdministrationUnitAuthority();
        using IDisposable occupied = await fixture.Resources.AcquireAsync(checked((int)fixture.Snapshot.Settings.MaximumEncodedBufferBytes));
        MessageAdminRedriveResult result = await Create(fixture, authority).RedriveAsync(new("Countries", Guid.NewGuid(), 1, "operator.reviewed"));
        Assert.AreEqual(MessageAdministrationDisposition.Unavailable, result.Disposition);
        Assert.AreEqual(0, fixture.Store.Calls);
        Assert.AreEqual(0, fixture.Transactions.CreatedTransactions.Count);
    }

    [TestMethod]
    public async Task AwaitedAuditFaultRemainsVisibleAfterActualCommittedRedriveWithoutInventingRollback()
    {
        await using var fixture = new DispositionUnitFixture(Options());
        fixture.Script(TransactionOutcome.Committed);
        var original = new InvalidOperationException("audit-hook-fault");
        var authority = new AdministrationUnitAuthority { AuditFault = original };
        InvalidOperationException observed = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => Create(fixture, authority).RedriveAsync(new("Countries", Guid.NewGuid(), 1, "operator.reviewed")));
        Assert.AreSame(original, observed);
        Assert.AreEqual(TransactionOutcome.Committed, fixture.Transactions.CreatedTransactions.Single().Completion!.Outcome);
        Assert.IsTrue(fixture.Transactions.CreatedTransactions.Single().DisposeRequested);
        Assert.AreEqual(1, fixture.Wakes);
        Assert.AreEqual(1, fixture.Store.Calls);
    }

    [TestMethod]
    public async Task PostCommitOwnerDisposalFaultIsPreservedWithoutAnotherStoreCallOrInventedRollback()
    {
        await using var fixture = new DispositionUnitFixture(Options());
        fixture.Script(TransactionOutcome.Committed);
        var original = new IOException("owned-disposal-fault");
        var authority = new AdministrationUnitAuthority();
        var factory = new AdministrationDisposeFaultFactory(fixture.Transactions, original);
        var admin = new MessageAdministration(fixture.Snapshot, fixture.Store, factory, fixture.Identity,
            authority, authority, authority, fixture.Resources);
        IOException observed = await Assert.ThrowsExactlyAsync<IOException>(() => admin.MaintainAsync());
        Assert.AreSame(original, observed);
        Assert.AreEqual(TransactionOutcome.Committed, fixture.Transactions.CreatedTransactions.Single().Completion!.Outcome);
        Assert.IsTrue(fixture.Transactions.CreatedTransactions.Single().DisposeRequested);
        Assert.AreEqual(1, fixture.Store.Calls);
        Assert.AreSame(authority.Current, authority.LastAudit!.Caller);
        Assert.AreEqual(SecurityOperationOutcome.Completed, authority.LastAudit.Outcome);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RedriveAuditsActualCommittedOutcomeBeforePreservingAnOriginalCleanupFault(bool auditAlsoFails)
    {
        await using var fixture = new DispositionUnitFixture(Options());
        fixture.Script(TransactionOutcome.Committed);
        var original = new IOException("owned-disposal-fault");
        var authority = new AdministrationUnitAuthority { AuditFault = auditAlsoFails ? new InvalidOperationException("later-audit-fault") : null };
        var factory = new AdministrationDisposeFaultFactory(fixture.Transactions, original);
        var admin = new MessageAdministration(fixture.Snapshot, fixture.Store, factory, fixture.Identity,
            authority, authority, authority, fixture.Resources);
        Guid messageId = Guid.NewGuid();
        IOException observed = await Assert.ThrowsExactlyAsync<IOException>(
            () => admin.RedriveAsync(new("Countries", messageId, 1, "operator.reviewed")));
        Assert.AreSame(original, observed);
        Assert.AreEqual(TransactionOutcome.Committed, fixture.Transactions.CreatedTransactions.Single().Completion!.Outcome);
        Assert.AreSame(authority.Current, authority.LastAudit!.Caller);
        Assert.AreEqual(SecurityOperationOutcome.Completed, authority.LastAudit.Outcome);
        Assert.AreEqual(messageId.ToString("D"), authority.LastAudit.CorrelationId);
        Assert.AreEqual(1, fixture.Store.Calls);
    }

    [TestMethod]
    public void AdministrativeInputsAndCapabilitySnapshotsAreBoundedAndDetached()
    {
        string[] selected = ["messaging.inspect"];
        var options = new MessagingOptions { InspectionCapabilities = selected };
        MessagingSettings captured = options.Capture();
        selected[0] = "different.capability";
        Assert.AreEqual("messaging.inspect", captured.InspectionCapabilities.Single());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new MessageInspectQuery(257));
        Assert.ThrowsExactly<ArgumentException>(() => new MessageInspectQuery(subscription: "Countries"));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new MessageRedriveCommand("Countries", Guid.NewGuid(), 0, "operator.reviewed"));
        Assert.ThrowsExactly<ArgumentException>(() => new MessageRedriveCommand("Countries", Guid.NewGuid(), 1, "unsafe\ncode"));
        Assert.ThrowsExactly<ArgumentException>(() => new MessagingOptions { MaintenanceCapabilities = ["messaging.maintain", "messaging.maintain"] }.Capture());
    }

    private static MessagingOptions Options() => new() { InspectionCapabilities = ["messaging.inspect"], MaintenanceCapabilities = ["messaging.maintain"] };

    private static MessageAdministration Create(DispositionUnitFixture fixture, AdministrationUnitAuthority authority)
        => new(fixture.Snapshot, fixture.Store, fixture.Transactions, fixture.Identity, authority, authority, authority,
            fixture.Resources, () => fixture.Wakes++);
}

// One focused cleanup-fault seam around the shared portable owner; it fabricates no persistence facts.
internal sealed class AdministrationDisposeFaultFactory(ILocalTransactionFactory inner, Exception fault) : ILocalTransactionFactory
{
    public async Task<ILocalTransaction> BeginAsync(string databaseName, CancellationToken cancellationToken = default)
        => new FaultOwner(await inner.BeginAsync(databaseName, cancellationToken), fault);

    public async Task<ILocalTransaction> BeginAsync(string databaseName, LocalTransactionOptions options, CancellationToken cancellationToken = default)
        => new FaultOwner(await inner.BeginAsync(databaseName, options, cancellationToken), fault);

    private sealed class FaultOwner(ILocalTransaction innerOwner, Exception original) : ILocalTransaction
    {
        public ILocalTransactionContext Context => innerOwner.Context;
        public LocalTransactionStatus Status => innerOwner.Status;
        public TransactionCompletion? Completion => innerOwner.Completion;
        public Task<TransactionCompletion> CommitAsync(CancellationToken cancellationToken = default) => innerOwner.CommitAsync(cancellationToken);
        public Task<TransactionCompletion> RollbackAsync() => innerOwner.RollbackAsync();
        public async ValueTask DisposeAsync()
        {
            await innerOwner.DisposeAsync();
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(original).Throw();
        }
    }
}

internal sealed class AdministrationUnitAuthority : ICapabilityAuthorizer, ICallerContextAccessor, ISecurityAuditSink
{
    internal bool Allowed = true;
    internal int Calls;
    internal CapabilityTarget? Target;
    internal SecurityOperationAudit? LastAudit;
    internal Exception? AuditFault;

    public CallerContext Current { get; } = new("Native", "Native", new("fixture", "operator"), SecurityActorKind.User,
        null, null, new(DateTimeOffset.UnixEpoch), "fixture-v1");

    public ValueTask<AuthorizationDecision> AuthorizeAsync(IReadOnlyList<string> capabilities, CapabilityTarget target, CancellationToken cancellationToken = default)
    {
        Calls++;
        Target = target;
        return ValueTask.FromResult(Allowed ? AuthorizationDecision.Allow
            : AuthorizationDecision.Deny(new(SecurityFailureKind.CapabilityDenied, "Denied by the unit policy.")));
    }

    public ValueTask AuthorizationCompletedAsync(SecurityAuthorizationAudit audit, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public ValueTask OperationCompletedAsync(SecurityOperationAudit audit, CancellationToken cancellationToken = default)
    {
        LastAudit = audit;
        if (AuditFault is not null)
        {
            return ValueTask.FromException(AuditFault);
        }
        return ValueTask.CompletedTask;
    }
}
