using CIS.Messaging;
using CIS.Messaging.PostgreSql;
using CIS.Transactions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace CIS.Tests.IntegrationTests.Messaging;

[TestClass]
[DoNotParallelize]
public sealed class PostgresMessageStoreTests
{
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task BusinessAndOutgoingResponsibilityCommitOrRollbackTogether(bool commit)
    {
        await using PostgresMessageFixture fixture = await PostgresMessageFixture.CreateAsync();
        EncodedMessage message = PostgresMessageFixture.Message();
        await using ILocalTransaction owner = await fixture.Factory.BeginAsync("Messaging");
        await fixture.InsertBusinessAsync(owner.Context, message.Envelope.MessageId);
        MessageStageReceipt receipt = await fixture.Store.StageAsync(owner.Context, PostgresMessageFixture.Outgoing(message));
        Assert.AreEqual(message.Envelope.MessageId, receipt.MessageId);
        Assert.AreEqual(0L, await fixture.CountAsync("business_record"));
        Assert.AreEqual(0L, await fixture.CountAsync("outbox"), "An independent publisher cannot see uncommitted staging.");
        TransactionCompletion completion = commit ? await owner.CommitAsync() : await owner.RollbackAsync();
        Assert.AreEqual(commit ? TransactionOutcome.Committed : TransactionOutcome.NotCommitted, completion.Outcome);
        Assert.AreEqual(commit ? 1L : 0L, await fixture.CountAsync("business_record"));
        Assert.AreEqual(commit ? 1L : 0L, await fixture.CountAsync("outbox"));
        await using ILocalTransaction read = await fixture.Factory.BeginAsync("Messaging");
        Assert.AreEqual(commit ? MessageSubmissionObservation.Found : MessageSubmissionObservation.NotFound,
            await fixture.Store.ReadSubmissionAsync(read.Context, receipt));
    }

    [TestMethod]
    public async Task InvalidStagingPoisonsTheOriginalUnitBeforeAnApparentlySuccessfulCommit()
    {
        await using PostgresMessageFixture fixture = await PostgresMessageFixture.CreateAsync();
        EncodedMessage message = PostgresMessageFixture.Message();
        await using ILocalTransaction owner = await fixture.Factory.BeginAsync("Messaging");
        await fixture.InsertBusinessAsync(owner.Context, message.Envelope.MessageId);
        MessageOutgoingRecord invalid = PostgresMessageFixture.Outgoing(message) with { Message = null! };
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => fixture.Store.StageAsync(owner.Context, invalid));
        Assert.IsTrue(owner.Context.IsRollbackOnly);
        Assert.AreEqual(TransactionOutcome.NotCommitted, (await owner.CommitAsync()).Outcome);
        Assert.AreEqual(0L, await fixture.CountAsync("business_record"));
        Assert.AreEqual(0L, await fixture.CountAsync("outbox"));
    }

    [TestMethod]
    public async Task ExpiredReservationAndDelayedFormerOwnerCannotOverwriteTheCurrentFence()
    {
        await using PostgresMessageFixture fixture = await PostgresMessageFixture.CreateAsync();
        EncodedMessage message = PostgresMessageFixture.Message();
        await StageCommittedAsync(fixture, message);
        MessageReservation old = (await fixture.ReserveCommittedAsync()).Single();
        Assert.AreEqual(message.Length, old.EncodedLength);
        Assert.AreEqual(message.Digest, old.Digest);
        await fixture.SqlAsync($"UPDATE {fixture.Table("outbox")} SET reserved_until=clock_timestamp()-INTERVAL '1 second'");
        MessageReservation current = (await fixture.ReserveCommittedAsync()).Single();
        Assert.AreEqual(old.RecordId, current.RecordId);
        Assert.AreEqual(old.MessageId, current.MessageId);
        Assert.AreEqual(old.Version + 1, current.Version);
        await using ILocalTransaction read = await fixture.Factory.BeginAsync("Messaging");
        Assert.IsNull(await fixture.Store.ReadReservedMessageAsync(read.Context, old));
        Assert.IsFalse(await fixture.Store.RecordPublicationAsync(read.Context, new(old, new(MessagePublicationDisposition.Confirmed))));
        EncodedMessage? retained = await fixture.Store.ReadReservedMessageAsync(read.Context, current);
        Assert.IsNotNull(retained);
        CollectionAssert.AreEqual(message.CopyBytes(), retained.CopyBytes());
        Assert.AreEqual(TransactionOutcome.Committed, (await read.CommitAsync()).Outcome);
        await fixture.PublishConfirmedAsync(current);
        Assert.AreEqual(0, (await fixture.ReserveCommittedAsync()).Length);
    }

    [TestMethod]
    public async Task UnknownPublicationRemainsRecoverableAndPermanentRoutingFailureIsInspectable()
    {
        await using PostgresMessageFixture fixture = await PostgresMessageFixture.CreateAsync();
        EncodedMessage message = PostgresMessageFixture.Message();
        await StageCommittedAsync(fixture, message);
        MessageReservation first = (await fixture.ReserveCommittedAsync()).Single();
        await using (ILocalTransaction update = await fixture.Factory.BeginAsync("Messaging"))
        {
            Assert.IsTrue(await fixture.Store.RecordPublicationAsync(update.Context,
                new(first, new(MessagePublicationDisposition.Unknown, MessagePublicationFailure.Transient, "messaging.confirm_unknown"))));
            Assert.AreEqual(TransactionOutcome.Committed, (await update.CommitAsync()).Outcome);
        }
        MessageReservation second = (await fixture.ReserveCommittedAsync()).Single();
        Assert.AreEqual(first.MessageId, second.MessageId);
        Assert.AreEqual(first.Digest, second.Digest);
        Assert.AreEqual(2, second.AttemptCount);
        await using ILocalTransaction owner = await fixture.Factory.BeginAsync("Messaging");
        Assert.IsTrue(await fixture.Store.RecordPublicationAsync(owner.Context,
            new(second, new(MessagePublicationDisposition.Rejected, MessagePublicationFailure.Unroutable, "messaging.unroutable"))));
        Assert.AreEqual(TransactionOutcome.Committed, (await owner.CommitAsync()).Outcome);
        await using ILocalTransaction inspect = await fixture.Factory.BeginAsync("Messaging");
        MessageInspectionRecord metadata = (await fixture.Store.InspectAsync(inspect.Context, new(PostgresMessageFixture.Application, PostgresMessageFixture.EnvironmentName))).Single();
        Assert.AreEqual("outbox.blocked", metadata.State);
        Assert.AreEqual("messaging.unroutable", metadata.SafeCode);
        Assert.AreEqual(0, (await fixture.ReserveCommittedAsync()).Length);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task UniqueInboxArbitrationProtectsOneCommittedEffectAcrossIndependentOwners(bool firstCommits)
    {
        await using PostgresMessageFixture fixture = await PostgresMessageFixture.CreateAsync();
        MessageProcessingAttempt attempt = PostgresMessageFixture.Attempt(PostgresMessageFixture.Message());
        await using ILocalTransaction first = await fixture.Factory.BeginAsync("Messaging");
        await using ILocalTransaction second = await fixture.Factory.BeginAsync("Messaging");
        MessageProcessingClaim claim = await fixture.Store.ClaimProcessingAsync(first.Context, attempt);
        Assert.AreEqual(MessageClaimDisposition.Acquired, claim.Disposition);
        int secondBackend = await fixture.BackendAsync(second.Context);
        Task<MessageProcessingClaim> waiting = fixture.Store.ClaimProcessingAsync(second.Context, attempt);
        await fixture.WaitForLockAsync(secondBackend);
        Assert.IsFalse(waiting.IsCompleted);
        if (firstCommits)
        {
            await fixture.InsertBusinessAsync(first.Context, attempt.Key.MessageId);
            Assert.IsTrue(await fixture.Store.CompleteProcessingAsync(first.Context,
                new(attempt.Key, claim.Record!.Version, 1)));
            Assert.AreEqual(TransactionOutcome.Committed, (await first.CommitAsync()).Outcome);
        }
        else
        {
            await fixture.InsertBusinessAsync(first.Context, attempt.Key.MessageId);
            await first.RollbackAsync();
        }
        MessageProcessingClaim winner = await waiting;
        Assert.AreEqual(firstCommits ? MessageClaimDisposition.HandledDuplicate : MessageClaimDisposition.Acquired, winner.Disposition);
        if (!firstCommits)
        {
            await fixture.InsertBusinessAsync(second.Context, attempt.Key.MessageId);
            Assert.IsTrue(await fixture.Store.CompleteProcessingAsync(second.Context, new(attempt.Key, winner.Record!.Version, 1)));
        }
        Assert.AreEqual(TransactionOutcome.Committed, (await second.CommitAsync()).Outcome);
        Assert.AreEqual(1L, await fixture.CountAsync("business_record"));
        Assert.AreEqual(1L, await fixture.CountAsync("processing"));
    }

    [TestMethod]
    public async Task IndependentLogicalSubscriptionsCanEachCommitTheSameEvent()
    {
        await using PostgresMessageFixture fixture = await PostgresMessageFixture.CreateAsync();
        EncodedMessage message = PostgresMessageFixture.Message();
        await using ILocalTransaction first = await fixture.Factory.BeginAsync("Messaging");
        await using ILocalTransaction second = await fixture.Factory.BeginAsync("Messaging");
        foreach ((ILocalTransaction owner, string subscription) in new[] { (first, "Countries"), (second, "Audit") })
        {
            MessageProcessingAttempt attempt = PostgresMessageFixture.Attempt(message, subscription);
            MessageProcessingClaim claim = await fixture.Store.ClaimProcessingAsync(owner.Context, attempt);
            Assert.AreEqual(MessageClaimDisposition.Acquired, claim.Disposition);
            await fixture.InsertBusinessAsync(owner.Context, message.Envelope.MessageId, subscription);
            Assert.IsTrue(await fixture.Store.CompleteProcessingAsync(owner.Context, new(attempt.Key, claim.Record!.Version, 1)));
        }
        Assert.AreEqual(TransactionOutcome.Committed, (await first.CommitAsync()).Outcome);
        Assert.AreEqual(TransactionOutcome.Committed, (await second.CommitAsync()).Outcome);
        Assert.AreEqual(2L, await fixture.CountAsync("processing"));
        Assert.AreEqual(2L, await fixture.CountAsync("business_record"));
    }

    [TestMethod]
    public async Task ContentAndProvenanceConflictsPreserveTheOriginalHandledRecord()
    {
        await using PostgresMessageFixture fixture = await PostgresMessageFixture.CreateAsync();
        MessageProcessingAttempt attempt = PostgresMessageFixture.Attempt(PostgresMessageFixture.Message());
        await HandleCommittedAsync(fixture, attempt);
        await using ILocalTransaction owner = await fixture.Factory.BeginAsync("Messaging");
        MessageProcessingRecord original = (await fixture.Store.ReadProcessingAsync(owner.Context, attempt.Key))!;
        MessageProcessingClaim content = await fixture.Store.ClaimProcessingAsync(owner.Context,
            attempt with { Message = PostgresMessageFixture.Message(attempt.Key.MessageId, 2) });
        Assert.AreEqual(MessageClaimDisposition.ContentConflict, content.Disposition);
        MessageProcessingClaim provenance = await fixture.Store.ClaimProcessingAsync(owner.Context,
            attempt with { Provenance = PostgresMessageFixture.Provenance("other-publisher") });
        Assert.AreEqual(MessageClaimDisposition.ProvenanceConflict, provenance.Disposition);
        Assert.AreEqual(original, await fixture.Store.ReadProcessingAsync(owner.Context, attempt.Key));
        Assert.AreEqual(1L, await fixture.CountAsync("business_record"));
    }

    [TestMethod]
    public async Task FreshFailureBookkeepingPreservesAConcurrentSuccessInTheRollbackGap()
    {
        await using PostgresMessageFixture fixture = await PostgresMessageFixture.CreateAsync();
        MessageProcessingAttempt attempt = PostgresMessageFixture.Attempt(PostgresMessageFixture.Message());
        await using (ILocalTransaction failed = await fixture.Factory.BeginAsync("Messaging"))
        {
            await fixture.Store.ClaimProcessingAsync(failed.Context, attempt);
            await fixture.InsertBusinessAsync(failed.Context, attempt.Key.MessageId);
            await failed.RollbackAsync();
        }
        await HandleCommittedAsync(fixture, attempt);
        await using ILocalTransaction disposition = await fixture.Factory.BeginAsync("Messaging");
        MessageDispositionResult result = await fixture.Store.SecureDispositionAsync(disposition.Context,
            new(attempt, MessageProcessingDisposition.RetryScheduled, PostgresMessageFixture.Handoff(attempt), "messaging.test_failure"));
        Assert.AreEqual(MessageDispositionResultKind.AlreadyHandled, result.Disposition);
        Assert.AreEqual(TransactionOutcome.Committed, (await disposition.CommitAsync()).Outcome);
        Assert.AreEqual(0L, await fixture.CountAsync("outbox"));
        Assert.AreEqual(1L, await fixture.CountAsync("business_record"));
    }

    [TestMethod]
    public async Task OneRetryChainSurvivesFailedAttemptRollbackAndRejectsForgedInternalOrdinals()
    {
        await using PostgresMessageFixture fixture = await PostgresMessageFixture.CreateAsync();
        MessageProcessingAttempt initial = PostgresMessageFixture.Attempt(PostgresMessageFixture.Message());
        MessageDispositionResult secured = await SecureCommittedAsync(fixture, initial);
        Assert.AreEqual(MessageDispositionResultKind.Secured, secured.Disposition);
        Assert.AreEqual(MessageDispositionResultKind.AlreadySecured, (await SecureCommittedAsync(fixture, initial)).Disposition);
        Assert.AreEqual(1L, await fixture.CountAsync("outbox"));
        MessageProcessingAttempt retry = initial with { DeliveryOrdinal = 2, Purpose = MessageRecordPurpose.Retry, HandoffRecordId = secured.HandoffRecordId };
        await using (ILocalTransaction owner = await fixture.Factory.BeginAsync("Messaging"))
        {
            Assert.AreEqual(MessageClaimDisposition.InvalidOrdinal, (await fixture.Store.ClaimProcessingAsync(owner.Context,
                retry with { HandoffRecordId = Guid.NewGuid() })).Disposition);
            MessageProcessingClaim claim = await fixture.Store.ClaimProcessingAsync(owner.Context, retry);
            Assert.AreEqual(MessageClaimDisposition.Acquired, claim.Disposition);
            await fixture.InsertBusinessAsync(owner.Context, retry.Key.MessageId);
            await owner.RollbackAsync();
        }
        MessageDispositionResult next = await SecureCommittedAsync(fixture, retry);
        Assert.AreEqual(MessageDispositionResultKind.Secured, next.Disposition);
        Assert.AreEqual(3, next.Record!.DeliveryOrdinal);
        Assert.AreNotEqual(secured.HandoffRecordId, next.HandoffRecordId);
        Assert.AreEqual(2L, await fixture.CountAsync("outbox"));
        Assert.AreEqual(0L, await fixture.CountAsync("business_record"));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FutureUntrustedFailureOrdinalsCannotMasqueradeAsKnownHandledOrTerminalWinners(bool terminal)
    {
        await using PostgresMessageFixture fixture = await PostgresMessageFixture.CreateAsync();
        MessageProcessingAttempt original = PostgresMessageFixture.Attempt(PostgresMessageFixture.Message());
        if (terminal)
        {
            await SecureCommittedAsync(fixture, original, true);
        }
        else
        {
            await HandleCommittedAsync(fixture, original);
        }
        await using ILocalTransaction owner = await fixture.Factory.BeginAsync("Messaging");
        MessageProcessingRecord before = (await fixture.Store.ReadProcessingAsync(owner.Context, original.Key))!;
        foreach (MessageProcessingAttempt future in new[]
        {
            original with { Purpose = MessageRecordPurpose.Retry, DeliveryOrdinal = 2, HandoffRecordId = Guid.NewGuid() },
            original with { Purpose = MessageRecordPurpose.Redrive, Generation = 1, HandoffRecordId = Guid.NewGuid() }
        })
        {
            MessageDispositionResult result = await fixture.Store.SecureDispositionAsync(owner.Context,
                new(future, MessageProcessingDisposition.RetryScheduled, PostgresMessageFixture.Handoff(future), "messaging.test_failure"));
            Assert.AreEqual(MessageDispositionResultKind.InvalidOrdinal, result.Disposition);
            Assert.AreEqual(before, await fixture.Store.ReadProcessingAsync(owner.Context, original.Key));
        }
        Assert.AreEqual(TransactionOutcome.Committed, (await owner.CommitAsync()).Outcome);
        Assert.AreEqual(terminal ? 1L : 0L, await fixture.CountAsync("outbox"));
    }

    [TestMethod]
    public async Task SeparateRejectionTargetsOnlyItsQuarantineWithoutPoisoningCanonicalIdentity()
    {
        await using PostgresMessageFixture fixture = await PostgresMessageFixture.CreateAsync();
        MessageProcessingAttempt legitimate = PostgresMessageFixture.Attempt(PostgresMessageFixture.Message());
        await HandleCommittedAsync(fixture, legitimate);
        EncodedMessage conflict = PostgresMessageFixture.Message(legitimate.Key.MessageId, 99);
        var quarantine = new MessageOutgoingRecord(Guid.NewGuid(), PostgresMessageFixture.Application, PostgresMessageFixture.EnvironmentName,
            new("CountryEvents"), conflict, MessageRecordPurpose.Rejection, DiagnosticCode: "messaging.content_conflict", TargetSubscription: "Audit");
        await using (ILocalTransaction reject = await fixture.Factory.BeginAsync("Messaging"))
        {
            await fixture.Store.RecordRejectionAsync(reject.Context, new(Guid.NewGuid(), PostgresMessageFixture.Application,
                PostgresMessageFixture.EnvironmentName, "Audit", conflict, "messaging.content_conflict", quarantine));
            Assert.AreEqual(TransactionOutcome.Committed, (await reject.CommitAsync()).Outcome);
        }
        MessageReservation reservation = (await fixture.ReserveCommittedAsync()).Single();
        Assert.AreEqual("Audit", reservation.TargetSubscription);
        Assert.IsNull(reservation.ProcessingKey);
        Assert.AreEqual(MessageRecordPurpose.Rejection, reservation.Purpose);
        await using ILocalTransaction read = await fixture.Factory.BeginAsync("Messaging");
        Assert.AreEqual(MessageProcessingDisposition.Handled, (await fixture.Store.ReadProcessingAsync(read.Context, legitimate.Key))!.Disposition);
        Assert.AreEqual(MessageClaimDisposition.HandledDuplicate, (await fixture.Store.ClaimProcessingAsync(read.Context, legitimate)).Disposition);
        Assert.AreEqual(1L, await fixture.CountAsync("processing"));
        Assert.AreEqual(1L, await fixture.CountAsync("rejections"));
    }

    [TestMethod]
    public async Task RealCommitThenSimulatedAcknowledgementLossRetainsAuthoritativeSubmission()
    {
        await using PostgresMessageFixture fixture = await PostgresMessageFixture.CreateAsync();
        EncodedMessage message = PostgresMessageFixture.Message();
        await using ILocalTransaction owner = await fixture.Factory.BeginAsync("Messaging");
        await fixture.InsertBusinessAsync(owner.Context, message.Envelope.MessageId);
        MessageStageReceipt receipt = await fixture.Store.StageAsync(owner.Context, PostgresMessageFixture.Outgoing(message));
        int before = fixture.Boundary.CommitInvocations;
        fixture.Boundary.FailAfterNextCommit();
        Assert.AreEqual(TransactionOutcome.Unknown, (await owner.CommitAsync()).Outcome);
        Assert.AreEqual(before + 1, fixture.Boundary.CommitInvocations);
        Assert.AreEqual(1L, await fixture.CountAsync("business_record"));
        await using ILocalTransaction fresh = await fixture.Factory.BeginAsync("Messaging");
        Assert.AreEqual(MessageSubmissionObservation.Found, await fixture.Store.ReadSubmissionAsync(fresh.Context, receipt));
    }

    [TestMethod]
    public async Task ConcurrentFiniteStorageAdmissionCannotOvercommitAndRollbackRestoresCapacity()
    {
        EncodedMessage message = PostgresMessageFixture.Message();
        await using PostgresMessageFixture fixture = await PostgresMessageFixture.CreateAsync(capacity: message.Length);
        await using ILocalTransaction first = await fixture.Factory.BeginAsync("Messaging");
        await using ILocalTransaction second = await fixture.Factory.BeginAsync("Messaging");
        await fixture.Store.StageAsync(first.Context, PostgresMessageFixture.Outgoing(message));
        int backend = await fixture.BackendAsync(second.Context);
        Task<MessageStageReceipt> waiting = fixture.Store.StageAsync(second.Context, PostgresMessageFixture.Outgoing(message));
        await fixture.WaitForLockAsync(backend);
        Assert.IsFalse(waiting.IsCompleted);
        Assert.AreEqual(TransactionOutcome.Committed, (await first.CommitAsync()).Outcome);
        await Assert.ThrowsExactlyAsync<MessageStorageCapacityException>(() => waiting);
        Assert.IsTrue(second.Context.IsRollbackOnly);
        Assert.AreEqual(TransactionOutcome.NotCommitted, (await second.CommitAsync()).Outcome);
        Assert.AreEqual(1L, await fixture.CountAsync("outbox"));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ExplicitRedrivePreservesIdentityAndEnforcesTheCapturedReplayHorizon(bool expired)
    {
        var policy = new MessageRetentionPolicy(supportedRedriveHorizon: TimeSpan.FromHours(1));
        await using PostgresMessageFixture fixture = await PostgresMessageFixture.CreateAsync(policy);
        MessageProcessingAttempt initial = PostgresMessageFixture.Attempt(PostgresMessageFixture.Message());
        MessageDispositionResult terminal = await SecureCommittedAsync(fixture, initial, true);
        if (expired)
        {
            await fixture.SqlAsync($"UPDATE {fixture.Table("processing")} SET created_at=clock_timestamp()-INTERVAL '2 hours'");
        }
        await using ILocalTransaction owner = await fixture.Factory.BeginAsync("Messaging");
        MessageRedriveResult redrive = await fixture.Store.RedriveAsync(owner.Context,
            new(initial.Key, terminal.Record!.Version, new("CountryEvents"), TimeSpan.FromHours(1), "messaging.operator_redrive"));
        Assert.AreEqual(expired ? MessageRedriveDisposition.OutsideHorizon : MessageRedriveDisposition.Prepared, redrive.Disposition);
        Assert.AreEqual(TransactionOutcome.Committed, (await owner.CommitAsync()).Outcome);
        Assert.AreEqual(expired ? 1L : 2L, await fixture.CountAsync("outbox"));
        if (!expired)
        {
            Assert.AreEqual(initial.Key, redrive.Record!.Key);
            Assert.AreEqual(1, redrive.Record.Generation);
            Assert.AreEqual(1, redrive.Record.DeliveryOrdinal);
            await using ILocalTransaction retry = await fixture.Factory.BeginAsync("Messaging");
            MessageProcessingClaim claim = await fixture.Store.ClaimProcessingAsync(retry.Context,
                initial with { Purpose = MessageRecordPurpose.Redrive, Generation = 1, HandoffRecordId = redrive.HandoffRecordId });
            Assert.AreEqual(MessageClaimDisposition.Acquired, claim.Disposition);
        }
    }

    [TestMethod]
    public async Task PayloadCleanupLeavesAProtectionTombstoneAndPreservesPendingResponsibility()
    {
        var policy = new MessageRetentionPolicy(publishedPayloadRetention: TimeSpan.FromMilliseconds(1));
        await using PostgresMessageFixture fixture = await PostgresMessageFixture.CreateAsync(policy);
        MessageProcessingAttempt handled = PostgresMessageFixture.Attempt(PostgresMessageFixture.Message());
        await HandleCommittedAsync(fixture, handled);
        await StageCommittedAsync(fixture, PostgresMessageFixture.Message());
        await fixture.SqlAsync($"UPDATE {fixture.Table("processing")} SET payload_expires=clock_timestamp()-INTERVAL '1 second'");
        await fixture.SqlAsync($"UPDATE {fixture.Table("outbox")} SET payload_expires=clock_timestamp()-INTERVAL '1 second'");
        await using (ILocalTransaction maintain = await fixture.Factory.BeginAsync("Messaging"))
        {
            MessageMaintenanceCounts counts = await fixture.Store.MaintainAsync(maintain.Context,
                new(PostgresMessageFixture.Application, PostgresMessageFixture.EnvironmentName, policy));
            Assert.AreEqual(1, counts.PayloadsCleared);
            Assert.AreEqual(0, counts.ProcessingDeleted);
            Assert.AreEqual(TransactionOutcome.Committed, (await maintain.CommitAsync()).Outcome);
        }
        Assert.AreEqual(1L, await fixture.CountAsync("outbox"));
        await using ILocalTransaction repeat = await fixture.Factory.BeginAsync("Messaging");
        MessageProcessingClaim duplicate = await fixture.Store.ClaimProcessingAsync(repeat.Context, handled);
        Assert.AreEqual(MessageClaimDisposition.HandledDuplicate, duplicate.Disposition);
        Assert.IsFalse(duplicate.Record!.HasPayload);
    }

    [TestMethod]
    public async Task InspectionAndBacklogAreBoundedAndUnknownCountsAreNotReportedAsEmpty()
    {
        await using PostgresMessageFixture fixture = await PostgresMessageFixture.CreateAsync();
        await StageCommittedAsync(fixture, PostgresMessageFixture.Message());
        await StageCommittedAsync(fixture, PostgresMessageFixture.Message());
        await using ILocalTransaction read = await fixture.Factory.BeginAsync("Messaging");
        MessageStoreObservation observation = await fixture.Store.ObserveAsync(read.Context,
            new(PostgresMessageFixture.Application, PostgresMessageFixture.EnvironmentName, 1));
        Assert.IsFalse(observation.IsComplete);
        Assert.IsNull(observation.PendingCount);
        Assert.IsTrue(observation.OldestAgeSeconds is { } age && age >= 0);
        IReadOnlyList<MessageInspectionRecord> page = await fixture.Store.InspectAsync(read.Context,
            new(PostgresMessageFixture.Application, PostgresMessageFixture.EnvironmentName, 1));
        Assert.AreEqual(1, page.Count);
        IReadOnlyList<MessageInspectionRecord> next = await fixture.Store.InspectAsync(read.Context,
            new(PostgresMessageFixture.Application, PostgresMessageFixture.EnvironmentName, 1, AfterRecordId: page[0].RecordId));
        Assert.AreEqual(1, next.Count);
        Assert.AreNotEqual(page[0].RecordId, next[0].RecordId);
    }

    [TestMethod]
    public async Task MaintenanceChangesAtMost256RowsAndRetainsPublishedMetadata()
    {
        var policy = new MessageRetentionPolicy(publishedPayloadRetention: TimeSpan.FromMilliseconds(1));
        await using PostgresMessageFixture fixture = await PostgresMessageFixture.CreateAsync(policy);
        await StageCommittedAsync(fixture, PostgresMessageFixture.Message());
        // Controlled stored publication facts qualify bounded SQL maintenance, not broker confirmation.
        await fixture.SqlAsync($"""
            INSERT INTO {fixture.Table("outbox")}
                (record_id,application,environment,message_id,route_alias,purpose,digest,envelope,encoded_length,ordinal,generation,state,published_at,payload_expires)
            SELECT gen_random_uuid(),application,environment,message_id,route_alias,purpose,digest,envelope,encoded_length,ordinal,generation,2,
                clock_timestamp()-INTERVAL '2 seconds',clock_timestamp()-INTERVAL '1 second'
            FROM {fixture.Table("outbox")} CROSS JOIN generate_series(1,256);
            UPDATE {fixture.Table("outbox")} SET state=2,published_at=clock_timestamp()-INTERVAL '2 seconds',payload_expires=clock_timestamp()-INTERVAL '1 second';
            """);
        Assert.AreEqual(257L, await fixture.CountAsync("outbox"));
        await using (ILocalTransaction first = await fixture.Factory.BeginAsync("Messaging"))
        {
            MessageMaintenanceCounts counts = await fixture.Store.MaintainAsync(first.Context,
                new(PostgresMessageFixture.Application, PostgresMessageFixture.EnvironmentName, policy));
            Assert.AreEqual(256, counts.PayloadsCleared);
            Assert.AreEqual(TransactionOutcome.Committed, (await first.CommitAsync()).Outcome);
        }
        await using ILocalTransaction second = await fixture.Factory.BeginAsync("Messaging");
        Assert.AreEqual(1, (await fixture.Store.MaintainAsync(second.Context,
            new(PostgresMessageFixture.Application, PostgresMessageFixture.EnvironmentName, policy))).PayloadsCleared);
        Assert.AreEqual(TransactionOutcome.Committed, (await second.CommitAsync()).Outcome);
        Assert.AreEqual(257L, await fixture.CountAsync("outbox"), "Payload cleanup retains submission/digest metadata.");
    }

    [TestMethod]
    public async Task RetryEvidenceIncludesTheExactOperationIdentityAndProtectedResource()
    {
        await using PostgresMessageFixture fixture = await PostgresMessageFixture.CreateAsync();
        MessageProcessingAttempt attempt = PostgresMessageFixture.Attempt(PostgresMessageFixture.Message()) with
        {
            OperationBinding = new(new(PostgresMessageFixture.Application, PostgresMessageFixture.EnvironmentName, "scope:MT", "Update", "original-key"),
                new(PostgresMessageFixture.Application, PostgresMessageFixture.EnvironmentName, "country", "MT"))
        };
        MessageDispositionResult secured = await SecureCommittedAsync(fixture, attempt);
        MessageProcessingAttempt retry = attempt with { DeliveryOrdinal = 2, Purpose = MessageRecordPurpose.Retry, HandoffRecordId = secured.HandoffRecordId };
        await using ILocalTransaction owner = await fixture.Factory.BeginAsync("Messaging");
        Assert.AreEqual(MessageClaimDisposition.ProvenanceConflict, (await fixture.Store.ClaimProcessingAsync(owner.Context, retry with
        {
            OperationBinding = new(attempt.OperationBinding!.Identity,
                new(PostgresMessageFixture.Application, PostgresMessageFixture.EnvironmentName, "country", "IT"))
        })).Disposition);
        MessageProcessingClaim valid = await fixture.Store.ClaimProcessingAsync(owner.Context, retry);
        Assert.AreEqual(MessageClaimDisposition.Acquired, valid.Disposition);
        Assert.AreEqual(attempt.OperationBinding, valid.Record!.OperationBinding);
        Assert.IsFalse(await fixture.Store.CompleteProcessingAsync(owner.Context, new(retry.Key, valid.Record.Version, 2)));
        Assert.IsTrue(await fixture.Store.CompleteProcessingAsync(owner.Context,
            new(retry.Key, valid.Record.Version, 2, OperationBinding: attempt.OperationBinding)));
    }

    [TestMethod]
    public async Task CompletionRequiresItsOwnClaimAndAnActualLinkedReplyInTheSameTransaction()
    {
        await using PostgresMessageFixture fixture = await PostgresMessageFixture.CreateAsync();
        MessageProcessingAttempt attempt = PostgresMessageFixture.Attempt(PostgresMessageFixture.Message());
        await using ILocalTransaction owner = await fixture.Factory.BeginAsync("Messaging");
        MessageProcessingClaim claim = await fixture.Store.ClaimProcessingAsync(owner.Context, attempt);
        Assert.IsFalse(await fixture.Store.CompleteProcessingAsync(owner.Context,
            new(attempt.Key, claim.Record!.Version, 1, ReplyRecordId: Guid.NewGuid())));
        Guid replyMessage = Guid.NewGuid();
        EncodedMessage reply = MessageWire.Encode(new(replyMessage, MessageKind.Reply, new("country.reply", 1),
            PostgresMessageFixture.Application, PostgresMessageFixture.EnvironmentName, DateTimeOffset.UnixEpoch, "null"u8.ToArray(),
            requestAttemptId: Guid.NewGuid(), inReplyToMessageId: attempt.Key.MessageId));
        var outgoing = new MessageOutgoingRecord(Guid.NewGuid(), PostgresMessageFixture.Application, PostgresMessageFixture.EnvironmentName,
            new("CountryEvents", new("producer", PostgresMessageFixture.EnvironmentName, Guid.NewGuid())), reply,
            MessageRecordPurpose.Reply, ProcessingKey: attempt.Key);
        await fixture.Store.StageAsync(owner.Context, outgoing);
        Assert.IsTrue(await fixture.Store.CompleteProcessingAsync(owner.Context,
            new(attempt.Key, claim.Record.Version, 1, ReplyRecordId: outgoing.RecordId)));
        Assert.AreEqual(TransactionOutcome.Committed, (await owner.CommitAsync()).Outcome);
        await using ILocalTransaction other = await fixture.Factory.BeginAsync("Messaging");
        Assert.IsFalse(await fixture.Store.CompleteProcessingAsync(other.Context,
            new(attempt.Key, claim.Record.Version, 1, ReplyRecordId: outgoing.RecordId)));
    }

    [TestMethod]
    public async Task MissingSchemaVersionFailsWithoutImplicitInstallationOrRepair()
    {
        await using PostgresMessageFixture fixture = await PostgresMessageFixture.CreateAsync();
        await fixture.SqlAsync($"DELETE FROM {fixture.Table("schema_version")}");
        await using ILocalTransaction owner = await fixture.Factory.BeginAsync("Messaging");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Store.ObserveAsync(owner.Context,
            new(PostgresMessageFixture.Application, PostgresMessageFixture.EnvironmentName)));
        Assert.IsTrue(owner.Context.IsRollbackOnly);
        Assert.AreEqual(0L, await fixture.CountAsync("schema_version"));
        Assert.AreEqual(0L, await fixture.CountAsync("outbox"));
    }

    [TestMethod]
    public async Task FiniteQuotaRollbackDoesNotConsumeCapacityAndCannotBeRetroactivelyDisabled()
    {
        EncodedMessage message = PostgresMessageFixture.Message();
        await using PostgresMessageFixture fixture = await PostgresMessageFixture.CreateAsync(capacity: message.Length);
        await using (ILocalTransaction rollback = await fixture.Factory.BeginAsync("Messaging"))
        {
            await fixture.Store.StageAsync(rollback.Context, PostgresMessageFixture.Outgoing(message));
            await rollback.RollbackAsync();
        }
        await StageCommittedAsync(fixture, message);
        var differentMode = new PostgreSqlMessagingStore(new() { Schema = fixture.Schema });
        await using ILocalTransaction owner = await fixture.Factory.BeginAsync("Messaging");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => differentMode.StageAsync(owner.Context,
            PostgresMessageFixture.Outgoing(message)));
        Assert.IsTrue(owner.Context.IsRollbackOnly);
        Assert.AreEqual(1L, await fixture.CountAsync("outbox"));
    }

    [TestMethod]
    public async Task ANewOwnersStorageModeAndOutgoingRowsShareTheOriginalTransaction()
    {
        EncodedMessage message = PostgresMessageFixture.Message();
        await using PostgresMessageFixture fixture = await PostgresMessageFixture.CreateAsync(capacity: message.Length);
        MessageOutgoingRecord outgoing = PostgresMessageFixture.Outgoing(message) with { Application = "another-service" };
        await using (ILocalTransaction first = await fixture.Factory.BeginAsync("Messaging"))
        {
            await fixture.Store.StageAsync(first.Context, outgoing);
            await first.RollbackAsync();
        }
        // A different mode is permissible because the first owner's mode never committed.
        var indefinite = new PostgreSqlMessagingStore(new() { Schema = fixture.Schema });
        await using ILocalTransaction second = await fixture.Factory.BeginAsync("Messaging");
        MessageStageReceipt receipt = await indefinite.StageAsync(second.Context, outgoing);
        Assert.AreEqual("another-service", receipt.Application);
        Assert.AreEqual(TransactionOutcome.Committed, (await second.CommitAsync()).Outcome);
        Assert.AreEqual(1L, await fixture.CountAsync("outbox"));
    }

    [TestMethod]
    public async Task FutureEligibleResponsibilityIsCountedWithoutInventingAnOldestEligibleAge()
    {
        await using PostgresMessageFixture fixture = await PostgresMessageFixture.CreateAsync();
        await using (ILocalTransaction stage = await fixture.Factory.BeginAsync("Messaging"))
        {
            await fixture.Store.StageAsync(stage.Context, PostgresMessageFixture.Outgoing(PostgresMessageFixture.Message()) with { Delay = TimeSpan.FromHours(1) });
            Assert.AreEqual(TransactionOutcome.Committed, (await stage.CommitAsync()).Outcome);
        }
        await using ILocalTransaction observe = await fixture.Factory.BeginAsync("Messaging");
        MessageStoreObservation snapshot = await fixture.Store.ObserveAsync(observe.Context,
            new(PostgresMessageFixture.Application, PostgresMessageFixture.EnvironmentName));
        Assert.IsTrue(snapshot.IsComplete);
        Assert.AreEqual(1L, snapshot.PendingCount);
        Assert.IsNull(snapshot.OldestAgeSeconds);
        Assert.AreEqual(0, (await fixture.ReserveCommittedAsync()).Length);
    }

    [TestMethod]
    public async Task FiniteDeletionWaitsForResolvedHandoffsAndEndsOnlyActuallyDeletedProtection()
    {
        var policy = new MessageRetentionPolicy(inboxProtectionRetention: TimeSpan.FromSeconds(1),
            terminalRetention: TimeSpan.FromSeconds(1), supportedRedriveHorizon: TimeSpan.FromSeconds(1), maximumDeliveryAge: TimeSpan.FromSeconds(1));
        await using PostgresMessageFixture fixture = await PostgresMessageFixture.CreateAsync(policy);
        MessageProcessingAttempt success = PostgresMessageFixture.Attempt(PostgresMessageFixture.Message());
        MessageProcessingAttempt failure = PostgresMessageFixture.Attempt(PostgresMessageFixture.Message());
        await HandleCommittedAsync(fixture, success);
        await SecureCommittedAsync(fixture, failure, true);
        EncodedMessage rejected = PostgresMessageFixture.Message();
        await using (ILocalTransaction rejection = await fixture.Factory.BeginAsync("Messaging"))
        {
            var handoff = new MessageOutgoingRecord(Guid.NewGuid(), PostgresMessageFixture.Application, PostgresMessageFixture.EnvironmentName,
                new("CountryEvents"), rejected, MessageRecordPurpose.Rejection, TargetSubscription: "Audit");
            await fixture.Store.RecordRejectionAsync(rejection.Context, new(Guid.NewGuid(), PostgresMessageFixture.Application,
                PostgresMessageFixture.EnvironmentName, "Audit", rejected, "messaging.denied", handoff));
            Assert.AreEqual(TransactionOutcome.Committed, (await rejection.CommitAsync()).Outcome);
        }
        await fixture.SqlAsync($"""
            UPDATE {fixture.Table("processing")} SET created_at=clock_timestamp()-INTERVAL '3 seconds',
                protection_expires=clock_timestamp()-INTERVAL '1 second', terminal_expires=clock_timestamp()-INTERVAL '1 second';
            UPDATE {fixture.Table("rejections")} SET terminal_expires=clock_timestamp()-INTERVAL '1 second';
            """);
        await using (ILocalTransaction first = await fixture.Factory.BeginAsync("Messaging"))
        {
            MessageMaintenanceCounts counts = await fixture.Store.MaintainAsync(first.Context,
                new(PostgresMessageFixture.Application, PostgresMessageFixture.EnvironmentName, policy));
            Assert.AreEqual(1, counts.ProcessingDeleted);
            Assert.AreEqual(0, counts.TerminalDeleted, "Pending handoffs retain their source provenance/responsibility.");
            Assert.AreEqual(TransactionOutcome.Committed, (await first.CommitAsync()).Outcome);
        }
        Assert.AreEqual(1L, await fixture.CountAsync("processing"));
        Assert.AreEqual(1L, await fixture.CountAsync("rejections"));
        foreach (MessageReservation outgoing in await fixture.ReserveCommittedAsync())
        {
            await fixture.PublishConfirmedAsync(outgoing);
        }
        await using (ILocalTransaction second = await fixture.Factory.BeginAsync("Messaging"))
        {
            MessageMaintenanceCounts counts = await fixture.Store.MaintainAsync(second.Context,
                new(PostgresMessageFixture.Application, PostgresMessageFixture.EnvironmentName, policy));
            Assert.AreEqual(2, counts.TerminalDeleted);
            Assert.AreEqual(TransactionOutcome.Committed, (await second.CommitAsync()).Outcome);
        }
        Assert.AreEqual(0L, await fixture.CountAsync("processing"));
        Assert.AreEqual(0L, await fixture.CountAsync("rejections"));
        await using ILocalTransaction later = await fixture.Factory.BeginAsync("Messaging");
        Assert.AreEqual(MessageClaimDisposition.Acquired, (await fixture.Store.ClaimProcessingAsync(later.Context, success)).Disposition,
            "Actual deletion ends store duplicate protection; historical application admission remains a separate policy.");
    }

    [TestMethod]
    public async Task CleanupReleasesFiniteEnvelopeCapacityOnlyWithItsActualCommit()
    {
        EncodedMessage message = PostgresMessageFixture.Message();
        var policy = new MessageRetentionPolicy(publishedPayloadRetention: TimeSpan.FromMilliseconds(1));
        await using PostgresMessageFixture fixture = await PostgresMessageFixture.CreateAsync(policy, message.Length);
        await StageCommittedAsync(fixture, message);
        await fixture.PublishConfirmedAsync((await fixture.ReserveCommittedAsync()).Single());
        await fixture.SqlAsync($"UPDATE {fixture.Table("outbox")} SET payload_expires=clock_timestamp()-INTERVAL '1 second'");
        await using (ILocalTransaction rollback = await fixture.Factory.BeginAsync("Messaging"))
        {
            Assert.AreEqual(1, (await fixture.Store.MaintainAsync(rollback.Context,
                new(PostgresMessageFixture.Application, PostgresMessageFixture.EnvironmentName, policy))).PayloadsCleared);
            await rollback.RollbackAsync();
        }
        await using (ILocalTransaction blocked = await fixture.Factory.BeginAsync("Messaging"))
        {
            await Assert.ThrowsExactlyAsync<MessageStorageCapacityException>(() => fixture.Store.StageAsync(blocked.Context,
                PostgresMessageFixture.Outgoing(PostgresMessageFixture.Message())));
        }
        await using (ILocalTransaction committed = await fixture.Factory.BeginAsync("Messaging"))
        {
            Assert.AreEqual(1, (await fixture.Store.MaintainAsync(committed.Context,
                new(PostgresMessageFixture.Application, PostgresMessageFixture.EnvironmentName, policy))).PayloadsCleared);
            Assert.AreEqual(TransactionOutcome.Committed, (await committed.CommitAsync()).Outcome);
        }
        await StageCommittedAsync(fixture, PostgresMessageFixture.Message());
        Assert.AreEqual(2L, await fixture.CountAsync("outbox"));
    }

    [TestMethod]
    public async Task LargestFiniteCapacitySupportsNegativeCleanupAccountingWithoutOverflow()
    {
        var policy = new MessageRetentionPolicy(publishedPayloadRetention: TimeSpan.FromMilliseconds(1));
        await using PostgresMessageFixture fixture = await PostgresMessageFixture.CreateAsync(policy, long.MaxValue);
        await StageCommittedAsync(fixture, PostgresMessageFixture.Message());
        await fixture.PublishConfirmedAsync((await fixture.ReserveCommittedAsync()).Single());
        await fixture.SqlAsync($"UPDATE {fixture.Table("outbox")} SET payload_expires=clock_timestamp()-INTERVAL '1 second'");
        await using (ILocalTransaction cleanup = await fixture.Factory.BeginAsync("Messaging"))
        {
            MessageMaintenanceCounts counts = await fixture.Store.MaintainAsync(cleanup.Context,
                new(PostgresMessageFixture.Application, PostgresMessageFixture.EnvironmentName, policy));
            Assert.AreEqual(1, counts.PayloadsCleared);
            Assert.AreEqual(TransactionOutcome.Committed, (await cleanup.CommitAsync()).Outcome);
        }
        await StageCommittedAsync(fixture, PostgresMessageFixture.Message());
        Assert.AreEqual(2L, await fixture.CountAsync("outbox"));
    }

    [TestMethod]
    public async Task OnlyNamedRetiredRepliesReleaseResponsibilityWhileOrdinaryBlockedRepliesProtectTheirReferences()
    {
        var policy = new MessageRetentionPolicy(TimeSpan.FromMilliseconds(1), TimeSpan.FromSeconds(1),
            supportedRedriveHorizon: TimeSpan.FromSeconds(1), maximumDeliveryAge: TimeSpan.FromSeconds(1));
        await using PostgresMessageFixture fixture = await PostgresMessageFixture.CreateAsync(policy);
        var pairs = new List<(MessageProcessingAttempt Attempt, MessageOutgoingRecord Outgoing)>();
        for (int index = 0; index < 2; index++)
        {
            MessageProcessingAttempt attempt = PostgresMessageFixture.Attempt(PostgresMessageFixture.Message());
            await using ILocalTransaction owner = await fixture.Factory.BeginAsync("Messaging");
            MessageProcessingClaim claim = await fixture.Store.ClaimProcessingAsync(owner.Context, attempt);
            EncodedMessage reply = MessageWire.Encode(new(Guid.NewGuid(), MessageKind.Reply, new("country.reply", 1),
                PostgresMessageFixture.Application, PostgresMessageFixture.EnvironmentName, DateTimeOffset.UnixEpoch, "null"u8.ToArray(),
                requestAttemptId: Guid.NewGuid(), inReplyToMessageId: attempt.Key.MessageId));
            var outgoing = new MessageOutgoingRecord(Guid.NewGuid(), PostgresMessageFixture.Application, PostgresMessageFixture.EnvironmentName,
                new("CountryEvents", new("producer", PostgresMessageFixture.EnvironmentName, Guid.NewGuid())), reply,
                MessageRecordPurpose.Reply, ProcessingKey: attempt.Key);
            await fixture.Store.StageAsync(owner.Context, outgoing);
            Assert.IsTrue(await fixture.Store.CompleteProcessingAsync(owner.Context, new(attempt.Key, claim.Record!.Version, 1,
                ReplyRecordId: outgoing.RecordId)));
            Assert.AreEqual(TransactionOutcome.Committed, (await owner.CommitAsync()).Outcome);
            pairs.Add((attempt, outgoing));
        }
        await StageCommittedAsync(fixture, PostgresMessageFixture.Message());
        foreach (MessageReservation reservation in await fixture.ReserveCommittedAsync())
        {
            bool expired = reservation.RecordId == pairs[0].Outgoing.RecordId;
            await using ILocalTransaction settle = await fixture.Factory.BeginAsync("Messaging");
            if (reservation.Purpose == MessageRecordPurpose.Application)
            {
                var forbidden = new MessagePublicationResult(MessagePublicationDisposition.Rejected, MessagePublicationFailure.ReplyDestinationExpired);
                await Assert.ThrowsExactlyAsync<ArgumentException>(() => fixture.Store.RecordPublicationAsync(settle.Context, new(reservation, forbidden)));
                Assert.IsFalse(await fixture.Store.RecordPublicationAsync(settle.Context,
                    new(reservation with { Purpose = MessageRecordPurpose.Reply }, forbidden)), "The SQL guard checks actual stored purpose too.");
            }
            Assert.IsTrue(await fixture.Store.RecordPublicationAsync(settle.Context, new(reservation,
                new(MessagePublicationDisposition.Rejected, expired ? MessagePublicationFailure.ReplyDestinationExpired : MessagePublicationFailure.Unroutable,
                    "messaging.unroutable"))));
            Assert.AreEqual(TransactionOutcome.Committed, (await settle.CommitAsync()).Outcome);
        }
        await using (ILocalTransaction read = await fixture.Factory.BeginAsync("Messaging"))
        {
            MessageStoreObservation observation = await fixture.Store.ObserveAsync(read.Context,
                new(PostgresMessageFixture.Application, PostgresMessageFixture.EnvironmentName));
            Assert.AreEqual(2L, observation.PendingCount);
            IReadOnlyList<MessageInspectionRecord> records = await fixture.Store.InspectAsync(read.Context,
                new(PostgresMessageFixture.Application, PostgresMessageFixture.EnvironmentName));
            MessageInspectionRecord retired = records.Single(row => row.RecordId == pairs[0].Outgoing.RecordId);
            Assert.AreEqual("outbox.reply_destination_expired", retired.State);
            Assert.AreEqual("messaging.reply_destination_expired", retired.SafeCode);
            Assert.AreEqual("outbox.blocked", records.Single(row => row.RecordId == pairs[1].Outgoing.RecordId).State);
        }
        await fixture.SqlAsync($"""
            UPDATE {fixture.Table("outbox")} SET payload_expires=clock_timestamp()-INTERVAL '1 second';
            UPDATE {fixture.Table("processing")} SET payload_expires=clock_timestamp()-INTERVAL '1 second',
                protection_expires=clock_timestamp()-INTERVAL '1 second';
            """);
        await using (ILocalTransaction maintain = await fixture.Factory.BeginAsync("Messaging"))
        {
            MessageMaintenanceCounts counts = await fixture.Store.MaintainAsync(maintain.Context,
                new(PostgresMessageFixture.Application, PostgresMessageFixture.EnvironmentName, policy));
            Assert.AreEqual(2, counts.PayloadsCleared);
            Assert.AreEqual(1, counts.ProcessingDeleted);
            Assert.AreEqual(TransactionOutcome.Committed, (await maintain.CommitAsync()).Outcome);
        }
        await using ILocalTransaction check = await fixture.Factory.BeginAsync("Messaging");
        Assert.IsNull(await fixture.Store.ReadProcessingAsync(check.Context, pairs[0].Attempt.Key));
        Assert.IsNotNull(await fixture.Store.ReadProcessingAsync(check.Context, pairs[1].Attempt.Key));
        Assert.AreEqual(3L, await fixture.CountAsync("outbox"), "Retirement and payload cleanup retain their named metadata; ordinary work is never expired.");
        Assert.AreEqual(0, (await fixture.ReserveCommittedAsync()).Length);
    }

    [TestMethod]
    [DataRow(TransactionIsolation.RepeatableRead)]
    [DataRow(TransactionIsolation.Serializable)]
    public async Task UnsupportedIsolationIsRejectedWithoutDowngradingOrPoisoningOtherWork(TransactionIsolation isolation)
    {
        await using PostgresMessageFixture fixture = await PostgresMessageFixture.CreateAsync();
        await using ILocalTransaction owner = await fixture.Factory.BeginAsync("Messaging", new LocalTransactionOptions { Isolation = isolation });
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => fixture.Store.ObserveAsync(owner.Context,
            new(PostgresMessageFixture.Application, PostgresMessageFixture.EnvironmentName)));
        Assert.AreEqual(isolation, owner.Context.Isolation);
        Assert.IsFalse(owner.Context.IsRollbackOnly);
    }

    private static async Task<MessageStageReceipt> StageCommittedAsync(PostgresMessageFixture fixture, EncodedMessage message)
    {
        await using ILocalTransaction owner = await fixture.Factory.BeginAsync("Messaging");
        MessageStageReceipt receipt = await fixture.Store.StageAsync(owner.Context, PostgresMessageFixture.Outgoing(message));
        Assert.AreEqual(TransactionOutcome.Committed, (await owner.CommitAsync()).Outcome);
        return receipt;
    }

    private static async Task HandleCommittedAsync(PostgresMessageFixture fixture, MessageProcessingAttempt attempt)
    {
        await using ILocalTransaction owner = await fixture.Factory.BeginAsync("Messaging");
        MessageProcessingClaim claim = await fixture.Store.ClaimProcessingAsync(owner.Context, attempt);
        Assert.AreEqual(MessageClaimDisposition.Acquired, claim.Disposition);
        await fixture.InsertBusinessAsync(owner.Context, attempt.Key.MessageId, attempt.Key.Subscription);
        Assert.IsTrue(await fixture.Store.CompleteProcessingAsync(owner.Context, new(attempt.Key, claim.Record!.Version, attempt.DeliveryOrdinal,
            attempt.Generation, OperationBinding: attempt.OperationBinding)));
        Assert.AreEqual(TransactionOutcome.Committed, (await owner.CommitAsync()).Outcome);
    }

    private static async Task<MessageDispositionResult> SecureCommittedAsync(PostgresMessageFixture fixture,
        MessageProcessingAttempt attempt, bool terminal = false)
    {
        await using ILocalTransaction owner = await fixture.Factory.BeginAsync("Messaging");
        MessageDispositionResult result = await fixture.Store.SecureDispositionAsync(owner.Context,
            new(attempt, terminal ? MessageProcessingDisposition.Terminal : MessageProcessingDisposition.RetryScheduled,
                PostgresMessageFixture.Handoff(attempt, terminal), "messaging.test_failure"));
        Assert.AreEqual(TransactionOutcome.Committed, (await owner.CommitAsync()).Outcome);
        return result;
    }
}
