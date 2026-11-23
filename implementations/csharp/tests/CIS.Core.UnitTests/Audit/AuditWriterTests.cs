using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.Audit;
using CIS.CommonTesting;
using CIS.Messaging;
using CIS.Security;
using CIS.Transactions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Audit;

[TestClass]
[DoNotParallelize]
public sealed class AuditWriterTests
{
    [TestMethod]
    public async Task StagingPassesTheOriginalParticipantAndExactRecordWithoutCompletingItsOwner()
    {
        CapturedAuditRecord record = await CaptureAsync("correlation");
        var messaging = new PublicationDouble();
        MessageStageReceipt receipt = Receipt();
        var staged = new MessageStageResult(MessageStageDisposition.Staged, receipt);
        messaging.Stage = (_, _, _, _) => Task.FromResult(staged);
        await using var writer = Writer(messaging);
        writer.BeginAccepting();
        var transactions = new ScriptedLocalTransactionFactory();
        transactions.Enqueue();
        await using ILocalTransaction owner = await transactions.BeginAsync("Ledger");
        using var token = new CancellationTokenSource();

        AuditStageResult result = await writer.StageAsync(owner.Context, record, token.Token);

        Assert.AreEqual(AuditStageDisposition.Staged, result.Disposition);
        Assert.AreSame(staged, result.Staging);
        Assert.AreSame(receipt, result.Receipt);
        Assert.AreEqual(record.AuditId, result.AuditId);
        Assert.AreSame(owner.Context, messaging.Context);
        Assert.AreSame(record.Record, messaging.Record);
        Assert.AreEqual("audit", messaging.Route);
        Assert.AreEqual(token.Token, messaging.Token);
        Assert.AreEqual(LocalTransactionStatus.Active, owner.Status);
        Assert.IsNull(owner.Completion);
        Assert.IsFalse(owner.Context.IsRollbackOnly);
        Assert.AreEqual(1, messaging.StageCalls);
        Assert.AreEqual(0, messaging.SubmitCalls);
        Assert.AreEqual("correlation", messaging.Diagnostics!.CorrelationId);
        Assert.IsNull(messaging.Diagnostics.Operation);
        Assert.IsNull(messaging.Diagnostics.CausationId);
        Assert.AreEqual(TransactionOutcome.Committed, (await owner.CommitAsync()).Outcome);
    }

    [TestMethod]
    [DataRow(MessageStageDisposition.Overloaded)]
    [DataRow(MessageStageDisposition.Unavailable)]
    public async Task UnderlyingRefusalPreservesItsFactsAndPoisonsAnIgnoredRequiredStage(MessageStageDisposition disposition)
    {
        CapturedAuditRecord record = await CaptureAsync();
        var messaging = new PublicationDouble();
        var refusal = new MessageStageResult(disposition, error: new("messaging.expected_refusal", "Selected scripted refusal."));
        messaging.Stage = (_, _, _, _) => Task.FromResult(refusal);
        await using var writer = Writer(messaging);
        writer.BeginAccepting();
        var transactions = new ScriptedLocalTransactionFactory();
        transactions.Enqueue();
        await using ILocalTransaction owner = await transactions.BeginAsync("Ledger");

        AuditStageResult result = await writer.StageAsync(owner.Context, record);

        Assert.AreSame(refusal, result.Staging);
        Assert.AreSame(refusal.Error, result.Error);
        Assert.IsTrue(owner.Context.IsRollbackOnly);
        Assert.IsNull(owner.Completion);
        Assert.AreEqual(TransactionOutcome.NotCommitted, (await owner.CommitAsync()).Outcome);
        Assert.AreEqual(1, messaging.StageCalls);
    }

    [TestMethod]
    [DataRow("producer")]
    [DataRow("bytes")]
    [DataRow("depth")]
    public async Task AuditSideRefusalHappensBeforeMessagingAndProhibitsCommit(string boundary)
    {
        CapturedAuditRecord record = await CaptureAsync();
        AuditOptions options = new("audit");
        string expectedCode;
        if (boundary == "producer")
        {
            record = WithForeignProducerForInternalBoundaryTest(record);
            expectedCode = "audit.producer_mismatch";
        }
        else if (boundary == "bytes")
        {
            options = new("audit", new(record.OriginalBytes.Length - 1));
            expectedCode = "audit.record_too_large";
        }
        else
        {
            options = new("audit", new(maximumJsonDepth: 2));
            expectedCode = "audit.record_invalid";
        }
        var messaging = new PublicationDouble();
        await using var writer = Writer(messaging, options);
        writer.BeginAccepting();
        var transactions = new ScriptedLocalTransactionFactory();
        transactions.Enqueue();
        await using ILocalTransaction owner = await transactions.BeginAsync("Ledger");

        AuditStageResult stage = await writer.StageAsync(owner.Context, record);
        AuditSubmissionResult submission = await writer.SubmitAsync(record);

        Assert.AreEqual(AuditStageDisposition.Unavailable, stage.Disposition);
        Assert.AreEqual(expectedCode, stage.Error!.Code);
        Assert.IsTrue(owner.Context.IsRollbackOnly);
        Assert.AreEqual(AuditSubmissionOutcome.NotSubmitted, submission.Outcome);
        Assert.AreEqual(expectedCode, submission.Error!.Code);
        Assert.IsNull(stage.Receipt);
        Assert.IsNull(submission.Receipt);
        Assert.IsNull(submission.Completion);
        Assert.AreEqual(0, messaging.StageCalls);
        Assert.AreEqual(0, messaging.SubmitCalls);
        Assert.AreEqual(TransactionOutcome.NotCommitted, (await owner.CommitAsync()).Outcome);
    }

    [TestMethod]
    public async Task OriginalStagingFaultSurvivesFailureToPoisonAnUnusableContext()
    {
        CapturedAuditRecord record = await CaptureAsync();
        var messaging = new PublicationDouble();
        var original = new InvalidOperationException("original outbox programming fault");
        messaging.Stage = (_, _, _, _) => Task.FromException<MessageStageResult>(original);
        var context = new ContextDouble { PoisonFault = new ObjectDisposedException("transaction") };
        await using var writer = Writer(messaging);
        writer.BeginAccepting();

        Exception observed = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => writer.StageAsync(context, record));

        Assert.AreSame(original, observed);
        Assert.AreEqual(1, context.PoisonCalls);
        await writer.DrainAsync();
    }

    [TestMethod]
    public async Task ShutdownAlsoJoinsActualRollbackOnlyCleanupAfterTheOutboxFaults()
    {
        CapturedAuditRecord record = await CaptureAsync();
        var messaging = new PublicationDouble();
        var original = new InvalidOperationException("original staging fault");
        messaging.Stage = (_, _, _, _) => Task.FromException<MessageStageResult>(original);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var context = new ContextDouble
        {
            OnPoison = () =>
            {
                entered.Set();
                release.Wait();
            }
        };
        await using var writer = Writer(messaging);
        writer.BeginAccepting();
        Task<AuditStageResult> pending = Task.Run(() => writer.StageAsync(context, record));
        try
        {
            Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(10)));
            Task drain = writer.DrainAsync();
            Assert.IsFalse(drain.IsCompleted, "Rollback-only cleanup is part of the admitted staging operation.");
            release.Set();
            Exception observed = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => pending);
            Assert.AreSame(original, observed);
            await drain.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            release.Set();
            try
            {
                await pending.WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (InvalidOperationException error) when (ReferenceEquals(error, original))
            {
                // The original expected failure was asserted above; cleanup still joins the task.
            }
        }
    }

    [TestMethod]
    public async Task CanceledEntryAndNullRecordPoisonBeforeMessagingWithoutReplacingTheirOriginalFaults()
    {
        CapturedAuditRecord record = await CaptureAsync();
        var messaging = new PublicationDouble();
        await using var writer = Writer(messaging);
        writer.BeginAccepting();
        var context = new ContextDouble();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        OperationCanceledException observed = await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => writer.StageAsync(context, record, cancellation.Token));

        Assert.AreEqual(cancellation.Token, observed.CancellationToken);
        Assert.IsTrue(context.IsRollbackOnly);
        Assert.AreEqual("audit.stage_cancelled", context.RollbackReasonCode);
        var nullContext = new ContextDouble();
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => writer.StageAsync(nullContext, null!));
        Assert.IsTrue(nullContext.IsRollbackOnly);
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => writer.SubmitAsync(record, cancellation.Token));
        Assert.AreEqual(0, messaging.StageCalls);
        Assert.AreEqual(0, messaging.SubmitCalls);
    }

    [TestMethod]
    [DataRow(TransactionIsolation.Serializable, false)]
    [DataRow(TransactionIsolation.ReadCommitted, true)]
    public async Task IneligiblePortableContextsNeverReachMessaging(TransactionIsolation isolation, bool alreadyPoisoned)
    {
        CapturedAuditRecord record = await CaptureAsync();
        var messaging = new PublicationDouble();
        await using var writer = Writer(messaging);
        writer.BeginAccepting();
        var context = new ContextDouble { Isolation = isolation };
        if (alreadyPoisoned)
        {
            context.MarkRollbackOnly("application.first_reason");
        }

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => writer.StageAsync(context, record));

        Assert.IsTrue(context.IsRollbackOnly);
        Assert.AreEqual(0, messaging.StageCalls);
        if (alreadyPoisoned)
        {
            Assert.AreEqual("application.first_reason", context.RollbackReasonCode);
        }
    }

    [TestMethod]
    [DataRow(TransactionOutcome.Committed, AuditSubmissionOutcome.OutboxCommitted)]
    [DataRow(TransactionOutcome.NotCommitted, AuditSubmissionOutcome.NotSubmitted)]
    [DataRow(TransactionOutcome.Unknown, AuditSubmissionOutcome.Unknown)]
    public async Task SubmissionPreservesActualOutcomeReceiptCompletionAndCleanupObjects(TransactionOutcome outcome, AuditSubmissionOutcome expected)
    {
        CapturedAuditRecord record = await CaptureAsync();
        var messaging = new PublicationDouble();
        MessageStageReceipt receipt = Receipt();
        TransactionCompletion completion = Completion(outcome);
        MessageSubmissionOutcome actual = outcome switch
        {
            TransactionOutcome.Committed => MessageSubmissionOutcome.OutboxCommitted,
            TransactionOutcome.NotCommitted => MessageSubmissionOutcome.NotSubmitted,
            _ => MessageSubmissionOutcome.Unknown
        };
        var submission = new MessageSubmission(actual, receipt, completion,
            new("messaging.scripted_fact", "Selected scripted completion fact."));
        messaging.Submit = (_, _, _) => Task.FromResult(submission);
        await using var writer = Writer(messaging);
        writer.BeginAccepting();

        AuditSubmissionResult result = await writer.SubmitAsync(record);

        Assert.AreSame(submission, result.Submission);
        Assert.AreSame(receipt, result.Receipt);
        Assert.AreSame(completion, result.Completion);
        Assert.IsNotNull(result.Completion);
        Assert.AreSame(completion.CleanupErrors[0], result.Completion.CleanupErrors[0]);
        Assert.AreSame(submission.Error, result.Error);
        Assert.AreEqual(record.AuditId, result.AuditId);
        Assert.AreEqual(expected, result.Outcome);
        Assert.AreEqual(1, messaging.SubmitCalls, "Unknown must not be automatically retried.");
        Assert.AreEqual(0, messaging.StageCalls);
        Assert.AreSame(record.Record, messaging.Record);
    }

    [TestMethod]
    [DataRow("stage-null-result")]
    [DataRow("stage-null-task")]
    [DataRow("submit-null-result")]
    [DataRow("submit-null-task")]
    [DataRow("submit-not-confirmed")]
    public async Task InconsistentSubstitutionsFaultAndAlwaysReleaseTheWriterBoundary(string boundary)
    {
        CapturedAuditRecord record = await CaptureAsync();
        var messaging = new PublicationDouble();
        await using var writer = Writer(messaging);
        writer.BeginAccepting();
        var context = new ContextDouble();
        if (boundary.StartsWith("stage", StringComparison.Ordinal))
        {
            messaging.Stage = (_, _, _, _) => boundary.EndsWith("task", StringComparison.Ordinal)
                ? null! : Task.FromResult<MessageStageResult>(null!);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => writer.StageAsync(context, record));
            Assert.IsTrue(context.IsRollbackOnly);
        }
        else if (boundary == "submit-not-confirmed")
        {
            messaging.Submit = (_, _, _) => Task.FromResult(new MessageSubmission(MessageSubmissionOutcome.NotConfirmed, Receipt()));
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => writer.SubmitAsync(record));
        }
        else
        {
            messaging.Submit = (_, _, _) => boundary.EndsWith("task", StringComparison.Ordinal)
                ? null! : Task.FromResult<MessageSubmission>(null!);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => writer.SubmitAsync(record));
        }
        await writer.DrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
    }

    [TestMethod]
    [DataRow("ascii-correlation", true)]
    [DataRow("😀😀😀😀😀😀😀😀😀😀😀😀😀😀😀😀😀😀😀😀😀😀😀😀😀😀😀😀😀😀😀😀😀", false)]
    public async Task OuterCorrelationHonorsMessagingBytesWithoutTruncatingAuditData(string correlation, bool fits)
    {
        CapturedAuditRecord record = await CaptureAsync(correlation);
        var messaging = new PublicationDouble();
        await using var writer = Writer(messaging);
        writer.BeginAccepting();

        await writer.SubmitAsync(record);

        Assert.AreEqual(correlation, record.Record.CorrelationId);
        Assert.AreEqual(fits ? correlation : null, messaging.Diagnostics!.CorrelationId);
        Assert.IsNull(messaging.Diagnostics.CausationId, "An AuditId is not an outer MessageId.");
        Assert.IsNull(messaging.Diagnostics.Operation, "Audit operation data does not create a durable-command binding.");
        Assert.IsEmpty(messaging.Diagnostics.Headers);
    }

    [TestMethod]
    public async Task InitialAndClosedWriterRefusalsAreExplicitAndCannotBeReopened()
    {
        CapturedAuditRecord record = await CaptureAsync();
        var messaging = new PublicationDouble();
        await using var writer = Writer(messaging);
        var context = new ContextDouble();

        AuditStageResult stage = await writer.StageAsync(context, record);
        AuditSubmissionResult submission = await writer.SubmitAsync(record);

        Assert.AreEqual(AuditStageDisposition.Unavailable, stage.Disposition);
        Assert.AreEqual(AuditSubmissionOutcome.NotSubmitted, submission.Outcome);
        Assert.IsTrue(context.IsRollbackOnly);
        Assert.AreEqual("audit.not_accepting", submission.Error!.Code);
        writer.BeginAccepting();
        writer.BeginAccepting();
        await writer.DrainAsync();
        Assert.ThrowsExactly<InvalidOperationException>(() => writer.BeginAccepting());
        Assert.AreEqual(AuditSubmissionOutcome.NotSubmitted, (await writer.SubmitAsync(record)).Outcome);
        Assert.AreEqual(0, messaging.StageCalls);
        Assert.AreEqual(0, messaging.SubmitCalls);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ShutdownJoinsActualAdmittedPortWorkAndPreservesItsLateKnownResult(bool staging)
    {
        CapturedAuditRecord record = await CaptureAsync();
        var messaging = new PublicationDouble();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        MessageStageReceipt receipt = Receipt();
        TransactionCompletion completion = Completion(TransactionOutcome.Committed);
        messaging.Stage = async (_, _, _, _) =>
        {
            entered.TrySetResult();
            await release.Task.ConfigureAwait(false);
            return new(MessageStageDisposition.Staged, receipt);
        };
        messaging.Submit = async (_, _, _) =>
        {
            entered.TrySetResult();
            await release.Task.ConfigureAwait(false);
            return new(MessageSubmissionOutcome.OutboxCommitted, receipt, completion);
        };
        await using var writer = Writer(messaging);
        writer.BeginAccepting();
        var context = new ContextDouble();
        using var cancellation = new CancellationTokenSource();
        Task<AuditStageResult>? stage = staging ? writer.StageAsync(context, record, cancellation.Token) : null;
        Task<AuditSubmissionResult>? submit = staging ? null : writer.SubmitAsync(record, cancellation.Token);
        Task pending = (Task?)stage ?? submit!;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cancellation.Cancel();
            Task drain = writer.DrainAsync();
            Assert.IsFalse(drain.IsCompleted, "Requested cancellation must not detach the admitted port operation or its cleanup.");
            Assert.IsFalse(pending.IsCompleted);
            Assert.AreEqual(AuditSubmissionOutcome.NotSubmitted, (await writer.SubmitAsync(record)).Outcome);
            release.TrySetResult();

            if (staging)
            {
                Assert.AreEqual(AuditStageDisposition.Staged, (await stage!).Disposition);
                Assert.IsFalse(context.IsRollbackOnly);
            }
            else
            {
                AuditSubmissionResult result = await submit!;
                Assert.AreEqual(AuditSubmissionOutcome.OutboxCommitted, result.Outcome);
                Assert.AreSame(completion, result.Completion);
            }
            await drain.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.AreEqual(cancellation.Token, messaging.Token);
        }
        finally
        {
            release.TrySetResult();
            await pending.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private static AuditWriter Writer(PublicationDouble messaging, AuditOptions? options = null)
        => new(options ?? new("audit"), LocalApplicationIdentity.Capture(), messaging, messaging);

    private static async Task<CapturedAuditRecord> CaptureAsync(string? correlation = null)
    {
        await using var budget = new MessagingResourceBudget(new MessagingOptions().Capture());
        await using var engine = new AuditCaptureEngine(LocalApplicationIdentity.Capture(), new(), TimeProvider.System, budget);
        var definition = new AuditEventDefinition<int>("ledger.applied", 1, AuditCategory.Business, new DetailsCodec());
        return engine.Capture(definition, 42, new(AuditOutcome.Succeeded, correlationId: correlation,
            causationAuditId: Guid.NewGuid(), operationReference: new("ledger.apply", "tenant", "key")), null);
    }

    private static CapturedAuditRecord WithForeignProducerForInternalBoundaryTest(CapturedAuditRecord source)
    {
        JsonNode json = JsonNode.Parse(source.OriginalBytes.Span)!;
        json["producer"]!["application"] = "different-producer";
        Assert.IsTrue(new AuditRecordCodec().TryDecode(Encoding.UTF8.GetBytes(json.ToJsonString()), out AuditRecord? record, out _));
        Assert.IsNotNull(record);
        // Only this friend test can bypass capture eligibility; production consumers have no such constructor.
        return new(record);
    }

    private static MessageStageReceipt Receipt()
        => new(Guid.NewGuid(), Guid.NewGuid(), "Ledger", EnvironmentOptions.ApplicationName,
            EnvironmentOptions.EnvironmentName, new MessageDigest(new byte[32]));

    private static TransactionCompletion Completion(TransactionOutcome outcome)
        => new(outcome, TransactionEndReason.CommitRequested, commitAttempted: outcome != TransactionOutcome.NotCommitted,
            rollbackAttempted: outcome != TransactionOutcome.Committed, rollbackConfirmed: outcome == TransactionOutcome.NotCommitted,
            cleanupErrors: [new(TransactionErrorPhase.ConnectionDispose, "fixture.cleanup", "Selected cleanup fact.")]);

    private sealed class DetailsCodec : IAuditDetailsCodec<int>
    {
        public void Validate(int details)
        {
            if (details < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(details));
            }
        }

        public void Write(Utf8JsonWriter writer, int details)
        {
            writer.WriteStartObject();
            writer.WriteNumber("value", details);
            writer.WriteEndObject();
        }
    }

    private sealed class ContextDouble : ILocalTransactionContext
    {
        internal Exception? PoisonFault
        {
            get; init;
        }
        internal Action? OnPoison
        {
            get; init;
        }
        internal int PoisonCalls
        {
            get; private set;
        }
        public string DatabaseName => "Ledger";
        public TransactionIsolation Isolation { get; init; } = TransactionIsolation.ReadCommitted;
        public bool IsRollbackOnly
        {
            get; private set;
        }
        public string? RollbackReasonCode
        {
            get; private set;
        }

        public void MarkRollbackOnly(string reasonCode)
        {
            PoisonCalls++;
            OnPoison?.Invoke();
            if (PoisonFault is { } fault)
            {
                throw fault;
            }
            IsRollbackOnly = true;
            RollbackReasonCode ??= reasonCode;
        }
    }

    private sealed class PublicationDouble : IMessageOutbox, IMessagingClient
    {
        internal Func<ILocalTransactionContext, AuditRecord, MessageSendOptions?, CancellationToken, Task<MessageStageResult>> Stage
        {
            get; set;
        }
            = (_, _, _, _) => Task.FromResult(new MessageStageResult(MessageStageDisposition.Unavailable));
        internal Func<AuditRecord, MessageSendOptions?, CancellationToken, Task<MessageSubmission>> Submit
        {
            get; set;
        }
            = (_, _, _) => Task.FromResult(new MessageSubmission(MessageSubmissionOutcome.NotSubmitted));
        internal int StageCalls
        {
            get; private set;
        }
        internal int SubmitCalls
        {
            get; private set;
        }
        internal ILocalTransactionContext? Context
        {
            get; private set;
        }
        internal AuditRecord? Record
        {
            get; private set;
        }
        internal string? Route
        {
            get; private set;
        }
        internal MessageSendOptions? Diagnostics
        {
            get; private set;
        }
        internal CancellationToken Token
        {
            get; private set;
        }

        public Task<MessageStageResult> PublishAsync<T>(ILocalTransactionContext context, string routeAlias, T value,
            MessageSendOptions? options = null, CancellationToken cancellationToken = default)
        {
            StageCalls++;
            Context = context;
            var record = (AuditRecord)(object)value!;
            Record = record;
            Route = routeAlias;
            Diagnostics = options;
            Token = cancellationToken;
            return Stage(context, record, options, cancellationToken);
        }

        public Task<MessageSubmission> PublishAsync<T>(string routeAlias, T value,
            MessageSendOptions? options = null, CancellationToken cancellationToken = default)
        {
            SubmitCalls++;
            var record = (AuditRecord)(object)value!;
            Record = record;
            Route = routeAlias;
            Diagnostics = options;
            Token = cancellationToken;
            return Submit(record, options, cancellationToken);
        }

        public Task<MessageStageResult> SendAsync<T>(ILocalTransactionContext context, string routeAlias, T value,
            MessageSendOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<MessageSubmission> SendAsync<T>(string routeAlias, T value,
            MessageSendOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<MessageRequestStage<TResponse>> StageRequestAsync<TRequest, TResponse>(ILocalTransactionContext context,
            MessageRequest<TRequest> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<MessageRequestResult<TResponse>> RequestAsync<TRequest, TResponse>(MessageRequest<TRequest> request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<MessageRequestResult<TResponse>> WaitForReplyAsync<TResponse>(MessageRequestTicket<TResponse> ticket,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
