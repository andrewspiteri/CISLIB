using CIS.Audit;
using CIS.Management;
using CIS.Management.Service.Metadata;
using CIS.Messaging;
using CIS.Messaging.PostgreSql;
using CIS.Operations;
using CIS.Operations.Hosting;
using CIS.Operations.PostgreSql;
using CIS.Security;
using CIS.Transactions;
using CIS.Transactions.AdoNet;
using Microsoft.Extensions.DependencyInjection;

namespace CIS.Tests.IntegrationTests.Management;

[TestClass]
[TestCategory("RequiresDocker")]
[TestCategory("PostgreSql")]
[DoNotParallelize]
public sealed class ManagementMetadataCompletionTests
{
    [TestMethod]
    [DataRow("unknown")]
    [DataRow("cleanup")]
    public async Task ActualNativeCommitAndCleanupFactsRemainSeparateFromOriginalMetadataJournalAndAudit(string fault)
    {
        await using ManagementDatabaseFixture database = await ManagementDatabaseFixture.CreateAsync();
        string operationsSchema = database.Options.Schema + "_op";
        string messagingSchema = database.Options.Schema + "_msg";
        try
        {
            await using (ILocalTransaction install = await database.Factory.BeginAsync("management"))
            {
                await install.Context.UseAdoNetAsync(async (connection, transaction, token) =>
                {
                    await using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = PostgreSqlOperationSchema.GetInstallationSql(operationsSchema);
                    await command.ExecuteNonQueryAsync(token);
                    return true;
                });
                await PostgreSqlMessagingSchema.InstallAsync(install.Context, messagingSchema);
                Assert.AreEqual(TransactionOutcome.Committed, (await install.CommitAsync()).Outcome);
            }
            var receipt = await database.Store.RegisterAsync(new("native-managed", "Development", "native-test", "application", new("notProvided")),
                Guid.NewGuid().ToString("D"), ManagementHostFixture.Request("native-boundary"));
            Assert.IsTrue(receipt.IsKnownCommitted);
            ManagementApplication original = (await database.Store.ReadApplicationAsync(receipt.Value!.ApplicationId)).Value!;
            LocalApplicationIdentity local = LocalApplicationIdentity.Capture();
            var caller = new FixedCaller();
            var limits = new AuditLimits();
            var snapshot = new MessagingSnapshot(new()
            {
                StoreAlias = "management"
            },
                [new MessageRouteDefinition<AuditRecord>(ManagementDeliveryOptions.AuditRoute, MessageKind.Event, new AuditRecordCodec(limits))],
                [new("management", "management")]);
            await using var resources = new MessagingResourceBudget(snapshot.Settings);
            await using var engine = new AuditCaptureEngine(local, limits, TimeProvider.System, resources);
            var store = new PostgreSqlMessagingStore(new()
            {
                Schema = messagingSchema,
                MaximumRetainedEnvelopeBytes = 64L * 1024 * 1024
            });
            var outbox = new MessageOutbox(snapshot, store, resources, local);
            await using var writer = new AuditWriter(new(ManagementDeliveryOptions.AuditRoute, limits), local, outbox,
                new MessageSubmissionClient(outbox, database.Factory));
            writer.BeginAccepting();
            try
            {
                var services = new ServiceCollection();
                services.AddSingleton<ICallerContextAccessor>(caller);
                services.AddSingleton<ICapabilityAuthorizer>(new ExactProviderFaultAuthorizer(local, original.ApplicationId));
                services.AddSingleton<ILocalTransactionFactory>(database.Factory);
                services.AddSingleton(database.Store);
                services.AddSingleton<IAuditCapture>(new AuditCapture(engine, caller));
                services.AddSingleton<IAuditWriter>(writer);
                services.AddSingleton<IOperationJournal>(new PostgreSqlOperationJournal(new()
                {
                    Schema = operationsSchema
                }));
                services.AddCisOperation(ManagementMetadataDefinition.Create());
                services.AddCisOperations();
                services.AddScoped<ManagementMetadataTarget>();
                services.AddScoped<ManagementMetadataService>();
                await using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
                int priorRollbacks = database.Boundary.RollbackInvocations;
                int priorNativeCommits = database.Boundary.ConfirmedNativeCommits;
                if (fault == "unknown")
                {
                    database.Boundary.FailAfterNextCommit();
                }
                else
                {
                    database.Boundary.FailAfterNextTransactionDisposal();
                }
                ManagementMetadataResponse response;
                await using (AsyncServiceScope scope = provider.CreateAsyncScope())
                {
                    response = await scope.ServiceProvider.GetRequiredService<ManagementMetadataService>().ReplaceAsync(original,
                        "native-original-key", original.MetadataETag, new(1, "Durable actual edit"), CancellationToken.None);
                }
                Assert.AreEqual(fault == "unknown" ? 503 : 200, response.Status);
                Assert.AreEqual(fault == "unknown" ? "unknown" : "committed", response.Result!.OriginalCompletion);
                // The boundary throws only after the provider really committed. RM02 correctly
                // attempts cleanup rollback for Unknown; Npgsql rejects rollback of its completed
                // transaction. Preserve that actual failure separately from the original outcome.
                Assert.IsFalse(response.Result.AttemptCompletion!.CleanupSucceeded);
                Assert.IsNotNull(response.Result.AttemptCompletion.CleanupCodes);
                CollectionAssert.AreEqual(new[] { fault == "unknown" ? "transaction.rollback_failed" : "transaction.transaction_disposal_failed" },
                    response.Result.AttemptCompletion.CleanupCodes.ToArray());
                Assert.AreEqual(1, database.Boundary.ConfirmedNativeCommits - priorNativeCommits);
                Assert.AreEqual(fault == "unknown" ? 1 : 0, database.Boundary.RollbackInvocations - priorRollbacks);
                Assert.AreEqual("2", (await database.Store.ReadApplicationAsync(original.ApplicationId)).Value!.MetadataRevision);
                Assert.AreEqual(1L, await Count(database, operationsSchema, "operation_journal"));
                Assert.AreEqual(1L, await Count(database, messagingSchema, "outbox"));

                // The next read really rolls back and then reports a late native disposal fault.
                // Its cleanup cannot change the previously committed original decision.
                database.Boundary.FailAfterNextTransactionDisposal();
                await using (AsyncServiceScope scope = provider.CreateAsyncScope())
                {
                    ManagementMetadataResponse found = await scope.ServiceProvider.GetRequiredService<ManagementMetadataService>()
                        .LookupAsync(original, "native-original-key", CancellationToken.None);
                    Assert.AreEqual(200, found.Status);
                    Assert.AreEqual("found", found.Result!.ResultSource);
                    Assert.AreEqual("committed", found.Result.OriginalCompletion);
                    Assert.AreEqual("notCommitted", found.Result.AttemptCompletion!.Outcome);
                    Assert.IsFalse(found.Result.AttemptCompletion.CleanupSucceeded);
                    Assert.IsNotNull(found.Result.AttemptCompletion.CleanupCodes);
                    CollectionAssert.AreEqual(new[] { "transaction.transaction_disposal_failed" }, found.Result.AttemptCompletion.CleanupCodes.ToArray());
                    Assert.AreEqual(response.Result.Result, found.Result.Result);
                }
                await using (AsyncServiceScope scope = provider.CreateAsyncScope())
                {
                    ManagementMetadataResponse replay = await scope.ServiceProvider.GetRequiredService<ManagementMetadataService>().ReplaceAsync(original,
                        "native-original-key", original.MetadataETag, new(1, "Durable actual edit"), CancellationToken.None);
                    Assert.AreEqual(200, replay.Status);
                    Assert.AreEqual("replayed", replay.Result!.ResultSource);
                    Assert.AreEqual("committed", replay.Result.OriginalCompletion);
                    Assert.IsTrue(replay.Result.AttemptCompletion!.CleanupSucceeded);
                    Assert.IsNull(replay.Result.AttemptCompletion.CleanupCodes);
                    Assert.AreEqual(response.Result.Result, replay.Result.Result);
                }
                Assert.AreEqual(1L, await Count(database, operationsSchema, "operation_journal"));
                Assert.AreEqual(1L, await Count(database, messagingSchema, "outbox"));
            }
            finally
            {
                await outbox.StopAsync();
            }
        }
        finally
        {
            await using var cleanup = database.Source.CreateCommand($"DROP SCHEMA IF EXISTS \"{operationsSchema}\" CASCADE; DROP SCHEMA IF EXISTS \"{messagingSchema}\" CASCADE");
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    private static async Task<long> Count(ManagementDatabaseFixture database, string schema, string table)
    {
        await using var command = database.Source.CreateCommand($"SELECT count(*) FROM \"{schema}\".\"{table}\"");
        return (long)(await command.ExecuteScalarAsync())!;
    }

    // This provider-fault proof does not claim native credential authentication; the full HTTP suite proves that separately.
    private sealed class FixedCaller : ICallerContextAccessor
    {
        public CallerContext? Current
        {
            get;
        } = new("metadata-native", "validated", new("native-test", "application"),
            SecurityActorKind.Application, "native-managed", "Development", new(DateTimeOffset.UtcNow), "metadata-boundary-v1");
    }

    // A bounded application-port substitute for this SQL/completion proof, not native credential authorization.
    // Reject unexpected targets/capability sets immediately; the separate product HTTP suite uses the real authorizer.
    private sealed class ExactProviderFaultAuthorizer(LocalApplicationIdentity local, string applicationId) : ICapabilityAuthorizer
    {
        public ValueTask<AuthorizationDecision> AuthorizeAsync(IReadOnlyList<string> capabilities, CapabilityTarget target,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (target.Application != local.Application || target.Environment != local.Environment
                || target.ResourceType != "management.application" || target.ResourceIdentifier != applicationId
                || capabilities.Count != 1
                || capabilities[0] is not ("management.applications.metadata.write" or "management.operations.read"))
            {
                throw new InvalidOperationException("Unexpected authority request in the narrow provider-fault proof.");
            }
            return ValueTask.FromResult(AuthorizationDecision.Allow);
        }
    }
}
