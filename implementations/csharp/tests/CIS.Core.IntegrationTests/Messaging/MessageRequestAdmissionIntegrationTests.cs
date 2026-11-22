using CIS.Messaging;
using CIS.Messaging.Hosting;
using CIS.Metrics.OpenTelemetry;
using CIS.Tests.IntegrationTests.Metrics;
using CIS.Tests.MessagingCrashFixture;
using CIS.Transactions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Messaging;

[TestClass]
[DoNotParallelize]
public sealed class MessageRequestAdmissionIntegrationTests
{
    [TestMethod]
    public async Task ActualCommittedButUncertainSubmissionRemainsUnknownAndOverloadCannotCreateAnUntrackedRequest()
    {
        await using MessageRpcProcessFixture fixture = await MessageRpcProcessFixture.CreateAsync();
        MessagingSnapshot snapshot = fixture.Broker.Snapshot;
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(MessageRequestAdmissionIntegrationTests).Assembly.FullName,
            EnvironmentName = "MessagingRpcCollection"
        });
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddCisMetrics(options => options.ScrapeCacheDuration = TimeSpan.Zero);
        builder.Services.AddSingleton(snapshot);
        builder.Services.AddSingleton(new MessagingComposition(snapshot));
        builder.Services.AddSingleton(new MessagingInstrumentation(snapshot));
        await using WebApplication metrics = builder.Build();
        metrics.UseRouting();
        metrics.UseCisMetrics();
        await metrics.StartAsync().WaitAsync(CollectionTestHost.Deadline);
        await using var resources = new MessagingResourceBudget(snapshot.Settings, reserveReplyIngress: true);
        var outbox = new MessageOutbox(snapshot, fixture.Caller.Store, resources, fixture.Broker.Local);
        await using var requests = new MessageRequestCoordinator(snapshot, fixture.Caller.Store, fixture.Caller.Factory,
            fixture.Broker.Transport, outbox, resources, fixture.Broker.Local,
            instrumentation: metrics.Services.GetRequiredService<MessagingInstrumentation>());
        outbox.BindRequests(requests);
        await fixture.Broker.Transport.StartAsync((delivery, _) =>
        {
            delivery.RequestConsumerStop("fixture.unexpected_request_delivery");
            throw new InvalidOperationException("This case does not run a publisher or a responder.");
        });
        try
        {
            // Existing RM-03 native seam throws after the actual PostgreSQL commit, not before SQL.
            int committedBefore = fixture.Caller.Boundary.ConfirmedNativeCommits;
            fixture.Caller.Boundary.FailAfterNextCommit();
            MessageRequestResult<string> unknown = await requests.RequestAsync<int, string>(Request(), default);
            Assert.AreEqual(MessageExchangeOutcome.SubmissionUnknown, unknown.Outcome);
            Assert.AreEqual(MessageSubmissionOutcome.Unknown, unknown.Submission.Outcome);
            Assert.AreEqual(TransactionOutcome.Unknown, unknown.Submission.Completion!.Outcome);
            Assert.IsNotNull(unknown.Reference);
            Assert.AreEqual(committedBefore + 1, fixture.Caller.Boundary.ConfirmedNativeCommits);
            Assert.AreEqual(1L, await fixture.Caller.CountAsync("outbox"));
            await using (ILocalTransaction read = await fixture.Caller.Factory.BeginAsync("Messaging"))
            {
                Assert.AreEqual(MessageSubmissionObservation.Found,
                    await fixture.Caller.Store.ReadSubmissionAsync(read.Context, unknown.Reference.Receipt));
                await read.RollbackAsync();
            }
            Assert.AreEqual(MessageSubmissionOutcome.Unknown, unknown.Submission.Outcome,
                "A later independent observation must not rewrite the original uncertain completion.");

            var tickets = new List<MessageRequestTicket<string>>();
            await using ILocalTransaction original = await fixture.Caller.Factory.BeginAsync("Messaging");
            try
            {
                for (int index = 0; index < snapshot.Settings.MaximumOutstandingRequests; index++)
                {
                    MessageRequestStage<string> stage = await outbox.StageRequestAsync<int, string>(original.Context, Request());
                    Assert.AreEqual(MessageStageDisposition.Staged, stage.Disposition);
                    Assert.IsNotNull(stage.Ticket);
                    tickets.Add(stage.Ticket);
                }
                int nativeCommits = fixture.Caller.Boundary.CommitInvocations;
                MessageRequestResult<string> overloaded = await requests.RequestAsync<int, string>(Request(), default);
                Assert.AreEqual(MessageExchangeOutcome.Overloaded, overloaded.Outcome);
                Assert.AreEqual(MessageSubmissionOutcome.NotSubmitted, overloaded.Submission.Outcome);
                Assert.IsNull(overloaded.Reference);
                Assert.AreEqual(nativeCommits, fixture.Caller.Boundary.CommitInvocations);
                Assert.AreEqual(1L, await fixture.Caller.CountAsync("outbox"),
                    "The four accepted requests are still owned by the active original transaction; overload adds no committed row.");
                Assert.AreEqual(TransactionOutcome.NotCommitted, (await original.RollbackAsync()).Outcome);
            }
            finally
            {
                foreach (MessageRequestTicket<string> ticket in tickets)
                {
                    await ticket.DisposeAsync();
                }
            }
            Assert.AreEqual(1L, await fixture.Caller.CountAsync("outbox"));
            Assert.AreEqual(0L, await fixture.Server.CountAsync("processing"));
            using var client = new HttpClient { BaseAddress = new(metrics.Urls.Single()), Timeout = CollectionTestHost.Deadline };
            string exported = await client.GetStringAsync("/_metrics");
            CollectionText.AssertIdentity(exported);
            string[] observations = CollectionText.Samples(exported).Where(line =>
                line.StartsWith("cis_messaging_rpc_duration_seconds_count{", StringComparison.Ordinal)).ToArray();
            Assert.AreEqual(1d, OutcomeCount(observations, "submission_unknown"));
            Assert.AreEqual(1d, OutcomeCount(observations, "overloaded"));
            Assert.AreEqual((double)snapshot.Settings.MaximumOutstandingRequests, OutcomeCount(observations, "cancelled"));
            Assert.AreEqual(snapshot.Settings.MaximumOutstandingRequests + 2d, observations.Sum(CollectionText.Value));
            Assert.IsTrue(observations.All(line => line.Contains("request=\"SubmitRequest\"", StringComparison.Ordinal)));
            Assert.AreEqual(0d, CollectionText.Samples(exported).Where(line => line.StartsWith("cis_messaging_active{", StringComparison.Ordinal))
                .Sum(CollectionText.Value));
            Assert.IsGreaterThan(0d, CollectionText.Samples(exported).Where(line =>
                line.StartsWith("cis_messaging_rpc_duration_seconds_sum{", StringComparison.Ordinal)).Sum(CollectionText.Value));
            Assert.IsFalse(exported.Contains("message_id", StringComparison.Ordinal));
            Assert.IsFalse(exported.Contains("reply_endpoint", StringComparison.Ordinal));
        }
        finally
        {
            try
            {
                try
                {
                    await outbox.StopAsync();
                }
                finally
                {
                    await requests.StopAsync();
                }
            }
            finally
            {
                await metrics.StopAsync(CancellationToken.None).WaitAsync(CollectionTestHost.Deadline);
            }
        }
    }

    private static MessageRequest<int> Request() => new("SubmitRequest", 7, new("Submit", "requested-account", "rpc-key"));

    private static double OutcomeCount(IEnumerable<string> observations, string outcome) => observations
        .Where(line => line.Contains("outcome=\"" + outcome + "\"", StringComparison.Ordinal)).Sum(CollectionText.Value);
}
