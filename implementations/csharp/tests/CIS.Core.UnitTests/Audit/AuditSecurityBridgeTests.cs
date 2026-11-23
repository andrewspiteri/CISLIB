using System.Text.Json;
using CIS.Audit;
using CIS.Audit.Hosting;
using CIS.CommonTesting;
using CIS.Messaging;
using CIS.Security;
using CIS.Security.Hosting;
using CIS.Transactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Audit;

[TestClass]
[DoNotParallelize]
public sealed class AuditSecurityBridgeTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-23T12:34:56Z");
    private static readonly CapabilityTarget BusinessTarget = new("resource-owner", "test", "country", "MT");

    [TestMethod]
    public async Task AuthorizationUsesDetachedTrustedFactsAndPreservesEveryOrderedCapability()
    {
        await using var fixture = new Fixture();
        CapturedAuditRecord? observed = null;
        fixture.Writer.Submit = (record, _) =>
        {
            observed = record;
            return Task.FromResult(Committed(record));
        };
        string[] capabilities = Enumerable.Range(0, 256).Select(index => index % 2 == 0 ? "country.read" : "country.select").ToArray();
        var failure = new SecurityFailure(SecurityFailureKind.ValidationUnavailable, "Selected safe reason.",
            SecurityFailureSource.Remote, 503, "provider-ä", "transport-λ");
        var audit = new SecurityAuthorizationAudit(Caller(), capabilities, BusinessTarget,
            AuthorizationDecision.Deny(failure), new string('ä', 128));

        await fixture.Bridge.AuthorizationCompletedAsync(audit);

        Assert.IsNotNull(observed);
        AuditRecord record = observed.Record;
        Assert.AreEqual("cis.security.authorization.decision", record.EventType);
        Assert.AreEqual(AuditCategory.Security, record.Category);
        Assert.AreEqual(AuditOutcome.Denied, record.Outcome);
        Assert.AreEqual("resource-owner", record.Target!.Application);
        Assert.AreEqual("caller-app", record.Actor.Application);
        Assert.AreEqual("subject", record.Actor.Subject!.Identifier);
        Assert.AreEqual("human", record.Actor.HumanSubject!.Identifier);
        Assert.AreEqual("verified-delegation", record.Actor.VerifiedDelegation);
        Assert.AreEqual(AuditInstanceKind.Reported, record.Producer.Instance!.Kind);
        Assert.AreEqual(Now, record.RecordedAtUtc);
        Assert.AreEqual(record.RecordedAtUtc, record.OccurredAtUtc);
        Assert.AreNotEqual(Caller().Authentication.ValidatedAtUtc, record.OccurredAtUtc);
        Assert.AreEqual(audit.CorrelationId, record.CorrelationId);
        Assert.IsNull(record.OperationReference);
        Assert.IsNull(record.CausationAuditId);
        Assert.AreEqual(failure.Code, record.Reason!.Code);
        Assert.AreEqual(failure.Message, record.Reason.Text);
        using JsonDocument details = JsonDocument.Parse(record.DetailsJson);
        CollectionAssert.AreEqual(capabilities, details.RootElement.GetProperty("capabilities").EnumerateArray()
            .Select(item => item.GetString()!).ToArray());
        Assert.AreEqual("remote", details.RootElement.GetProperty("failureSource").GetString());
        Assert.AreEqual(503, details.RootElement.GetProperty("httpStatusCode").GetInt32());
        Assert.AreEqual("provider-ä", details.RootElement.GetProperty("providerCode").GetString());
        Assert.AreEqual("transport-λ", details.RootElement.GetProperty("transportCode").GetString());
    }

    [TestMethod]
    public async Task MissingAuthenticationDoesNotInventCallerTargetOrSuccessfulBusinessWork()
    {
        await using var fixture = new Fixture();
        var captured = new List<CapturedAuditRecord>();
        fixture.Writer.Submit = (record, _) =>
        {
            captured.Add(record);
            return Task.FromResult(Committed(record));
        };
        var audit = new SecurityAuthorizationAudit(null, ["country.read"], null,
            AuthorizationDecision.Deny(new(SecurityFailureKind.Unauthenticated, "No trusted caller.")));

        await fixture.Bridge.AuthorizationCompletedAsync(audit);
        await fixture.Bridge.AuthorizationCompletedAsync(audit);

        Assert.HasCount(2, captured);
        Assert.AreNotEqual(captured[0].AuditId, captured[1].AuditId);
        Assert.AreEqual(AuditActorKind.Unknown, captured[0].Record.Actor.Kind);
        Assert.IsNull(captured[0].Record.Target);
        Assert.AreEqual(AuditOutcome.Denied, captured[0].Record.Outcome);
    }

    [TestMethod]
    [DataRow(SecurityOperationOutcome.Completed, AuditOutcome.Succeeded, "completed")]
    [DataRow(SecurityOperationOutcome.Failed, AuditOutcome.Failed, "failed")]
    [DataRow(SecurityOperationOutcome.Canceled, AuditOutcome.Canceled, "canceled")]
    public async Task OperationReportsOnlyItsSuppliedBusinessFact(SecurityOperationOutcome reported, AuditOutcome outcome, string name)
    {
        await using var fixture = new Fixture();
        CapturedAuditRecord? captured = null;
        fixture.Writer.Submit = (record, _) =>
        {
            captured = record;
            return Task.FromResult(Committed(record));
        };
        string code = new('λ', 128);

        await fixture.Bridge.OperationCompletedAsync(new(Caller(), ["country.select", "country.select"],
            BusinessTarget, reported, code, "correlation"));

        Assert.IsNotNull(captured);
        Assert.AreEqual("cis.security.operation.reported", captured.Record.EventType);
        Assert.AreEqual(outcome, captured.Record.Outcome);
        Assert.AreEqual(code, captured.Record.Reason!.Code);
        Assert.IsNull(captured.Record.OperationReference);
        using JsonDocument details = JsonDocument.Parse(captured.Record.DetailsJson);
        Assert.AreEqual(name, details.RootElement.GetProperty("reportedOutcome").GetString());
        Assert.AreEqual(code, details.RootElement.GetProperty("outcomeCode").GetString());
        Assert.AreEqual(2, details.RootElement.GetProperty("capabilities").GetArrayLength());
    }

    [TestMethod]
    public async Task OnlyCompleteTrustedIngressAuthorizationBindingsAreSuppressed()
    {
        var target = new CapabilityTarget("audit-receiver", "test", "audit.subscription", "audit-ingress");
        string[] mutable = ["audit.receive", "audit.validate"];
        var bindings = new List<AuditSecurityIngressBinding> { new(target, mutable, ["audit.retry"]) };
        var options = new AuditSecurityBridgeOptions(ingressBindings: bindings);
        mutable[0] = "business.read";
        bindings.Clear();
        await using var fixture = new Fixture(options);
        int submitted = 0;
        fixture.Writer.Submit = (record, _) =>
        {
            submitted++;
            return Task.FromResult(Committed(record));
        };
        AuthorizationDecision denied = AuthorizationDecision.Deny(new(SecurityFailureKind.CapabilityDenied, "Denied."));

        await fixture.Bridge.AuthorizationCompletedAsync(new(Caller(), ["audit.validate", "audit.receive"], target, denied));
        await fixture.Bridge.AuthorizationCompletedAsync(new(Caller(), ["audit.retry"], target, AuthorizationDecision.Allow));
        Assert.AreEqual(0, submitted);

        await fixture.Bridge.AuthorizationCompletedAsync(new(Caller(), ["audit.receive"], target, denied));
        await fixture.Bridge.AuthorizationCompletedAsync(new(Caller(), ["audit.receive", "audit.receive"], target, denied));
        await fixture.Bridge.AuthorizationCompletedAsync(new(Caller(), ["audit.receive", "audit.validate", "business.read"], target, denied));
        await fixture.Bridge.AuthorizationCompletedAsync(new(Caller(), ["audit.receive", "audit.validate"], BusinessTarget, denied));
        await fixture.Bridge.AuthorizationCompletedAsync(new(null, ["audit.receive", "audit.validate"], null, denied));
        await fixture.Bridge.OperationCompletedAsync(new(Caller(), ["audit.retry"], target, SecurityOperationOutcome.Completed));
        Assert.AreEqual(6, submitted);
    }

    [TestMethod]
    [DataRow(MessageSubmissionOutcome.NotSubmitted)]
    [DataRow(MessageSubmissionOutcome.Unknown)]
    public async Task ExpectedFailuresKeepExactSubmissionReceiptAndCleanup(MessageSubmissionOutcome outcome)
    {
        await using var fixture = new Fixture();
        AuditSubmissionResult? expectedSubmission = null;
        fixture.Writer.Submit = (record, _) =>
        {
            MessageStageReceipt receipt = Receipt();
            TransactionCompletion? completion = outcome == MessageSubmissionOutcome.Unknown
                ? new(TransactionOutcome.Unknown, TransactionEndReason.CommitRequested, true, true, true,
                    cleanupErrors: [new(TransactionErrorPhase.TransactionDispose, "dispose.failed", "Safe cleanup failure.")])
                : null;
            expectedSubmission = new(record.AuditId, new(outcome, receipt, completion, new("audit.unavailable", "Safe submission context.")));
            return Task.FromResult(expectedSubmission!);
        };

        AuditSecurityBridgeException failure = await Assert.ThrowsExactlyAsync<AuditSecurityBridgeException>(
            () => fixture.Bridge.AuthorizationCompletedAsync(Allowed()).AsTask());

        Assert.AreSame(expectedSubmission, failure.Result);
        Assert.AreSame(expectedSubmission!.Submission, failure.Result.Submission);
        Assert.AreSame(expectedSubmission.Receipt, failure.Result.Receipt);
        Assert.AreSame(expectedSubmission.Completion, failure.Result.Completion);
        Assert.AreSame(expectedSubmission.Error, failure.Result.Error);
        Assert.IsFalse(failure.Message.Contains("Safe submission context.", StringComparison.Ordinal));
        Assert.AreEqual(1, fixture.Writer.Calls);
    }

    [TestMethod]
    public async Task CaptureRefusalIsKnownNotSubmittedAndDoesNotInvokeWriter()
    {
        await using var fixture = new Fixture();
        Assert.IsTrue(fixture.Budget.TryAcquireBytes(checked((int)fixture.Settings.MaximumEncodedBufferBytes), out IDisposable? held));
        try
        {
            AuditSecurityBridgeException overloaded = await Assert.ThrowsExactlyAsync<AuditSecurityBridgeException>(
                () => fixture.Bridge.AuthorizationCompletedAsync(Allowed()).AsTask());
            Assert.AreEqual(AuditSubmissionOutcome.NotSubmitted, overloaded.Result.Outcome);
            Assert.AreNotEqual(Guid.Empty, overloaded.Result.AuditId);
            Assert.IsNull(overloaded.Result.Receipt);
            Assert.AreEqual("audit.capture_overloaded", overloaded.Result.Error!.Code);
        }
        finally
        {
            held!.Dispose();
        }

        fixture.Engine.BeginClose();
        AuditSecurityBridgeException unavailable = await Assert.ThrowsExactlyAsync<AuditSecurityBridgeException>(
            () => fixture.Bridge.AuthorizationCompletedAsync(Allowed()).AsTask());
        Assert.AreEqual("audit.capture_unavailable", unavailable.Result.Error!.Code);
        Assert.AreEqual(0, fixture.Writer.Calls);
    }

    [TestMethod]
    public async Task OriginalFaultsAndCancellationRemainObservableWithoutReplay()
    {
        await using var fixture = new Fixture();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        OperationCanceledException pre = await Assert.ThrowsAsync<OperationCanceledException>(
            () => fixture.Bridge.AuthorizationCompletedAsync(Allowed(), cancelled.Token).AsTask());
        Assert.AreEqual(cancelled.Token, pre.CancellationToken);
        Assert.AreEqual(0, fixture.Writer.Calls);

        var original = new InvalidOperationException("Original programming failure.");
        fixture.Writer.Submit = (_, _) => Task.FromException<AuditSubmissionResult>(original);
        Assert.AreSame(original, await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => fixture.Bridge.AuthorizationCompletedAsync(Allowed()).AsTask()));

        var cancellation = new OperationCanceledException("Original independent cancellation.");
        fixture.Writer.Submit = (_, _) => Task.FromException<AuditSubmissionResult>(cancellation);
        Assert.AreSame(cancellation, await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => fixture.Bridge.AuthorizationCompletedAsync(Allowed()).AsTask()));
        Assert.AreEqual(2, fixture.Writer.Calls);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DeadlineOrEarlierCallerCancellationStillJoinsActualSettlementAndRetainsKnownCommit(bool callerFirst)
    {
        await using var fixture = new Fixture();
        using var caller = new CancellationTokenSource();
        var cancellationSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Writer.Submit = async (record, token) =>
        {
            using CancellationTokenRegistration registration = token.Register(() => cancellationSeen.TrySetResult());
            await release.Task.ConfigureAwait(false);
            return Committed(record);
        };

        Task pending = fixture.Bridge.AuthorizationCompletedAsync(Allowed(), caller.Token).AsTask();
        if (callerFirst)
        {
            caller.Cancel();
        }
        else
        {
            fixture.Clock.Advance(TimeSpan.FromSeconds(5));
        }
        await cancellationSeen.Task;
        Assert.IsFalse(pending.IsCompleted, "Cancellation cannot replace actual writer settlement.");
        release.SetResult();
        await pending;
        Assert.AreEqual(1, fixture.Writer.Calls);
    }

    [TestMethod]
    public async Task NullOrMismatchedWriterFactsAreProgrammingFaults()
    {
        await using var fixture = new Fixture();
        Func<CapturedAuditRecord, CancellationToken, Task<AuditSubmissionResult>>[] invalid =
        [
            (_, _) => null!,
            (_, _) => Task.FromResult<AuditSubmissionResult>(null!),
            (_, _) => Task.FromResult(new AuditSubmissionResult(Guid.NewGuid(), new(MessageSubmissionOutcome.NotSubmitted)))
        ];
        foreach (var behavior in invalid)
        {
            fixture.Writer.Submit = behavior;
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Bridge.AuthorizationCompletedAsync(Allowed()).AsTask());
        }
    }

    [TestMethod]
    public async Task ValidCommittedSubmissionWithoutOptionalCompletionRemainsCommitted()
    {
        await using var fixture = new Fixture();
        fixture.Writer.Submit = (record, _) => Task.FromResult(new AuditSubmissionResult(record.AuditId,
            new(MessageSubmissionOutcome.OutboxCommitted, Receipt())));

        await fixture.Bridge.AuthorizationCompletedAsync(Allowed());

        Assert.AreEqual(1, fixture.Writer.Calls);
    }

    [TestMethod]
    public void OptionsAndRegistrationAreFiniteExplicitAndPerformNoPrematureResolution()
    {
        Assert.AreEqual(TimeSpan.FromSeconds(5), new AuditSecurityBridgeOptions().SubmissionTimeout);
        Assert.AreEqual(TimeSpan.FromMinutes(1), new AuditSecurityBridgeOptions(TimeSpan.FromMinutes(1)).SubmissionTimeout);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new AuditSecurityBridgeOptions(TimeSpan.Zero));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new AuditSecurityBridgeOptions(TimeSpan.FromSeconds(61)));
        Assert.ThrowsExactly<ArgumentException>(() => new AuditSecurityIngressBinding(new("app", "test"), ["audit.receive"]));
        Assert.ThrowsExactly<ArgumentException>(() => new AuditSecurityIngressBinding(BusinessTarget, ["audit.receive", "audit.receive"]));
        Assert.AreEqual(8, new AuditSecurityIngressBinding(BusinessTarget,
            Enumerable.Range(0, 8).Select(index => "audit.action" + index).ToArray()).IngressCapabilities.Count);
        Assert.ThrowsExactly<ArgumentException>(() => new AuditSecurityIngressBinding(BusinessTarget,
            Enumerable.Range(0, 9).Select(index => "audit.action" + index).ToArray()));
        Assert.ThrowsExactly<ArgumentException>(() => new AuditSecurityBridgeOptions(ingressBindings:
            Enumerable.Range(0, 17).Select(_ => new AuditSecurityIngressBinding(BusinessTarget, ["audit.receive"])).ToArray()));

        int resolved = 0;
        var custom = new ServiceCollection();
        custom.AddSingleton<ISecurityAuditSink>(_ =>
        {
            resolved++;
            throw new InvalidOperationException("Must not resolve during registration.");
        });
        Assert.ThrowsExactly<InvalidOperationException>(() => custom.AddCisSecurityAuditBridge());
        Assert.AreEqual(0, resolved);

        var afterSecurity = new ServiceCollection();
        afterSecurity.AddCisSecurity(new SecurityOptions());
        Assert.ThrowsExactly<InvalidOperationException>(() => afterSecurity.AddCisSecurityAuditBridge());

        var selected = new ServiceCollection();
        selected.AddKeyedSingleton<ISecurityAuditSink>("independent", (_, _) => throw new InvalidOperationException());
        selected.AddCisSecurityAuditBridge();
        selected.AddCisSecurity(new SecurityOptions());
        Assert.AreEqual(1, selected.Count(descriptor => descriptor.ServiceType == typeof(ISecurityAuditSink) && !descriptor.IsKeyedService));
        Assert.ThrowsExactly<InvalidOperationException>(() => selected.AddCisSecurityAuditBridge());
    }

    private static SecurityAuthorizationAudit Allowed() => new(Caller(), ["country.read"], BusinessTarget, AuthorizationDecision.Allow);

    private static CallerContext Caller() => new("native", "certificate", new("issuer", "subject"), SecurityActorKind.Application,
        "caller-app", "test", new(Now.AddHours(-1)), "policy-v1", new("reported", "verified"),
        new("human-issuer", "human"), "verified-delegation");

    private static MessageStageReceipt Receipt() => new(Guid.NewGuid(), Guid.NewGuid(), "Ledger", "app", "test", new(new byte[32]));

    private static AuditSubmissionResult Committed(CapturedAuditRecord record) => new(record.AuditId,
        new(MessageSubmissionOutcome.OutboxCommitted, Receipt(),
            new(TransactionOutcome.Committed, TransactionEndReason.CommitRequested, true, false, false)));

    private sealed class Fixture : IAsyncDisposable
    {
        internal Fixture(AuditSecurityBridgeOptions? options = null)
        {
            Settings = new MessagingOptions().Capture();
            Budget = new(Settings);
            Engine = new(LocalApplicationIdentity.Capture(), new(), Clock, Budget);
            Bridge = new(Engine, Writer, options ?? new(), Clock);
        }

        internal MessagingSettings Settings
        {
            get;
        }
        internal MessagingResourceBudget Budget
        {
            get;
        }
        internal AuditCaptureEngine Engine
        {
            get;
        }
        internal ManualTimeProvider Clock { get; } = new(Now);
        internal ControlledWriter Writer { get; } = new();
        internal AuditSecurityBridge Bridge
        {
            get;
        }

        public async ValueTask DisposeAsync()
        {
            await Engine.DisposeAsync();
            await Budget.DisposeAsync();
        }
    }

    private sealed class ControlledWriter : IAuditWriter
    {
        internal Func<CapturedAuditRecord, CancellationToken, Task<AuditSubmissionResult>> Submit
        {
            get; set;
        }
            = (record, _) => Task.FromResult(Committed(record));

        internal int Calls
        {
            get; private set;
        }

        public Task<AuditSubmissionResult> SubmitAsync(CapturedAuditRecord record, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Submit(record, cancellationToken);
        }

        public Task<AuditStageResult> StageAsync(ILocalTransactionContext context, CapturedAuditRecord record,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("The Security bridge must submit independently.");
    }
}
