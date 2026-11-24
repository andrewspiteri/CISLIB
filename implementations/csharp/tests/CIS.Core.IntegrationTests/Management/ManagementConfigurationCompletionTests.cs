using CIS.Audit;
using CIS.Management;
using CIS.Management.Service;
using CIS.Management.Service.Backends;
using CIS.Management.Service.Configuration;
using CIS.Management.Service.Metadata;
using CIS.Management.Service.Persistence;
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
[TestCategory("ManagementService")]
[DoNotParallelize]
public sealed class ManagementConfigurationCompletionTests
{
    [TestMethod]
    [DataRow("catalog", "unknown")]
    [DataRow("catalog", "cleanup")]
    [DataRow("publication", "unknown")]
    [DataRow("publication", "cleanup")]
    [DataRow("release", "unknown")]
    [DataRow("release", "cleanup")]
    [DataRow("assignment", "unknown")]
    [DataRow("assignment", "cleanup")]
    public async Task OriginalConfigurationDecisionRecoversAfterActualNativeCommitOrCleanupUncertainty(string family, string fault)
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
                });
                await PostgreSqlMessagingSchema.InstallAsync(install.Context, messagingSchema);
                Assert.AreEqual(TransactionOutcome.Committed, (await install.CommitAsync()).Outcome);
            }
            ManagementConfigurationOptions deployment = ManagementConfigurationCatalogTests.Deployment();
            var options = new ManagementServiceOptions(database.Source.ConnectionString, new byte[32], database.Options,
                operationsSchema: operationsSchema, messagingSchema: messagingSchema, configuration: deployment);
            var authority = new PostgreSqlManagementAuthority(database.Factory, options);
            var store = new PostgreSqlManagementStore(database.Factory, database.Options, authority);
            ManagementConfigurationCatalog catalog = (await store.ReadConfigurationCatalogAsync(deployment, CancellationToken.None)).Value!;
            LocalApplicationIdentity local = LocalApplicationIdentity.Capture();
            var caller = new FixedCaller();
            var authorizer = new ExactConfigurationAuthorizer(local, catalog.CatalogId);
            var limits = new AuditLimits();
            var snapshot = new MessagingSnapshot(new()
            {
                StoreAlias = "management"
            },
                [new MessageRouteDefinition<AuditRecord>(ManagementDeliveryOptions.AuditRoute, MessageKind.Event, new AuditRecordCodec(limits))],
                [new("management", "management")]);
            await using var resources = new MessagingResourceBudget(snapshot.Settings);
            await using var engine = new AuditCaptureEngine(local, limits, TimeProvider.System, resources);
            var messages = new PostgreSqlMessagingStore(new()
            {
                Schema = messagingSchema,
                MaximumRetainedEnvelopeBytes = 64L * 1024 * 1024
            });
            var outbox = new MessageOutbox(snapshot, messages, resources, local);
            await using var writer = new AuditWriter(new(ManagementDeliveryOptions.AuditRoute, limits), local, outbox,
                new MessageSubmissionClient(outbox, database.Factory));
            writer.BeginAccepting();
            try
            {
                var services = new ServiceCollection();
                services.AddSingleton(options);
                services.AddSingleton<ICallerContextAccessor>(caller);
                services.AddSingleton<ICapabilityAuthorizer>(authorizer);
                services.AddSingleton<ILocalTransactionFactory>(database.Factory);
                services.AddSingleton(store);
                services.AddSingleton(authority);
                services.AddSingleton<IAuditCapture>(new AuditCapture(engine, caller));
                services.AddSingleton<IAuditWriter>(writer);
                services.AddSingleton<IOperationJournal>(new PostgreSqlManagementJournal(new PostgreSqlOperationJournal(new()
                {
                    Schema = operationsSchema
                }), authority));
                services.AddCisOperation(ManagementConfigurationOperations.Catalog());
                services.AddCisOperation(ManagementConfigurationOperations.Publication());
                services.AddCisOperation(ManagementConfigurationOperations.Release());
                services.AddCisOperation(ManagementConfigurationOperations.Assignment());
                services.AddCisOperations();
                services.AddScoped<ManagementConfigurationTarget>();
                services.AddScoped<ManagementConfigurationService>();
                await using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
                var catalogCommand = new ManagementConfigurationCatalogCommand(1, deployment.ManifestDigest, "Native configuration boundary");
                var publicationCommand = new ManagementConfigurationPublicationCommand(1, "{\"Greeting\":\"Native original bytes\"}", "Native publication boundary");
                ManagementConfigurationDefinition? source = null;
                string? sourceETag = null;
                ManagementConfigurationBindingResource? binding = null;
                string? mapETag = null;
                var releaseCommand = new ManagementConfigurationReleaseCommand(1, new("absent"), new("absent"), "Native pinned defaults release");
                ManagementConfigurationAssignmentCommand? assignmentCommand = null;
                if (family != "catalog")
                {
                    await using (AsyncServiceScope preparation = provider.CreateAsyncScope())
                    {
                        ManagementConfigurationResponse initialized = await preparation.ServiceProvider.GetRequiredService<ManagementConfigurationService>()
                            .ApplyCatalogAsync(catalog, "fixture-catalog", catalog.ETag, catalogCommand, CancellationToken.None);
                        Assert.AreEqual(200, initialized.Status);
                    }
                    source = (await store.ReadConfigurationDefinitionsAsync(null, 16, CancellationToken.None)).Value!.Single(row => row.Kind == "common");
                    sourceETag = (await store.ReadConfigurationSourceAsync(source.SourceId, null, 1, CancellationToken.None)).Value!.ETag;
                    authorizer.SourceId = source.SourceId;
                    if (family is "release" or "assignment")
                    {
                        string applicationId = (await store.ReadConfigurationDefinitionsAsync(null, 16, CancellationToken.None)).Value!
                            .Single(row => row.HostApplicationName == "FirstHost").ApplicationId!;
                        binding = (await store.ReadConfigurationBindingAsync(applicationId, "shared.settings", "1", CancellationToken.None)).Value!;
                        authorizer.BindingId = binding.BindingId;
                        mapETag = (await store.ReadConfigurationAssignmentsAsync(binding, CancellationToken.None)).Value!.ETag;
                        if (family == "assignment")
                        {
                            await using AsyncServiceScope releaseSetup = provider.CreateAsyncScope();
                            ManagementConfigurationResponse created = await releaseSetup.ServiceProvider.GetRequiredService<ManagementConfigurationService>()
                                .CreateReleaseAsync(binding, "fixture-release", mapETag, releaseCommand, CancellationToken.None);
                            Assert.AreEqual(200, created.Status);
                            assignmentCommand = new(1, "replace", created.Result!.Result!.ReleaseId!, [], "Native original assignment");
                        }
                    }
                }
                long priorJournal = await Count(database, operationsSchema, "operation_journal");
                long priorAudit = await Count(database, messagingSchema, "outbox");
                int priorCommits = database.Boundary.ConfirmedNativeCommits;
                int priorRollbacks = database.Boundary.RollbackInvocations;
                if (fault == "unknown")
                {
                    database.Boundary.FailAfterNextCommit();
                }
                else
                {
                    database.Boundary.FailAfterNextTransactionDisposal();
                }
                ManagementConfigurationResponse submitted;
                await using (AsyncServiceScope attempt = provider.CreateAsyncScope())
                {
                    ManagementConfigurationService service = attempt.ServiceProvider.GetRequiredService<ManagementConfigurationService>();
                    submitted = await Submit(service, "native-original-key");
                }
                Assert.AreEqual(fault == "unknown" ? 503 : 200, submitted.Status);
                Assert.IsNotNull(submitted.Operation ?? submitted.Result?.Operation);
                if (fault == "unknown")
                {
                    Assert.AreEqual("management.configuration.operation_unknown", submitted.Code);
                    Assert.IsNull(submitted.Result, "Prepared bytes must not be presented as an original known committed decision.");
                }
                else
                {
                    Assert.AreEqual("committed", submitted.Result!.OriginalCompletion);
                    Assert.IsFalse(submitted.Result.AttemptCompletion!.CleanupSucceeded);
                }
                Assert.AreEqual(1, database.Boundary.ConfirmedNativeCommits - priorCommits);
                Assert.AreEqual(fault == "unknown" ? 1 : 0, database.Boundary.RollbackInvocations - priorRollbacks);
                Assert.AreEqual(priorJournal + 1, await Count(database, operationsSchema, "operation_journal"));
                Assert.AreEqual(priorAudit + 1, await Count(database, messagingSchema, "outbox"));

                // A genuinely rolled-back recovery read with late cleanup failure cannot undo the original commit.
                database.Boundary.FailAfterNextTransactionDisposal();
                ManagementConfigurationOperationResult recovered;
                await using (AsyncServiceScope recovery = provider.CreateAsyncScope())
                {
                    ManagementConfigurationService service = recovery.ServiceProvider.GetRequiredService<ManagementConfigurationService>();
                    ManagementConfigurationResponse found = await Lookup(service, "native-original-key");
                    Assert.AreEqual(200, found.Status);
                    recovered = found.Result!;
                    Assert.AreEqual("found", recovered.ResultSource);
                    Assert.AreEqual("committed", recovered.OriginalCompletion);
                    Assert.AreEqual("notCommitted", recovered.AttemptCompletion!.Outcome);
                    Assert.IsFalse(recovered.AttemptCompletion.CleanupSucceeded);
                    CollectionAssert.AreEqual(new[] { "transaction.transaction_disposal_failed" }, recovered.AttemptCompletion.CleanupCodes!.ToArray());
                    Assert.AreEqual("native-original-key", recovered.Operation.Key);
                }
                await using (AsyncServiceScope replay = provider.CreateAsyncScope())
                {
                    ManagementConfigurationService service = replay.ServiceProvider.GetRequiredService<ManagementConfigurationService>();
                    ManagementConfigurationResponse repeated = await Submit(service, "native-original-key");
                    Assert.AreEqual(200, repeated.Status);
                    Assert.AreEqual("replayed", repeated.Result!.ResultSource);
                    Assert.AreEqual(recovered.Operation, repeated.Result.Operation);
                    Assert.AreEqual(recovered.Result, repeated.Result.Result);
                    Assert.AreEqual("committed", repeated.Result.OriginalCompletion);
                }
                await using (AsyncServiceScope absence = provider.CreateAsyncScope())
                {
                    ManagementConfigurationService service = absence.ServiceProvider.GetRequiredService<ManagementConfigurationService>();
                    ManagementConfigurationResponse missing = await Lookup(service, "never-submitted-key");
                    Assert.AreEqual(404, missing.Status);
                    Assert.AreEqual("management.configuration.operation_not_found", missing.Code);
                }
                Assert.AreEqual(priorJournal + 1, await Count(database, operationsSchema, "operation_journal"));
                Assert.AreEqual(priorAudit + 1, await Count(database, messagingSchema, "outbox"));
                Assert.AreEqual(family == "publication" ? 1L : 0L, await Count(database, database.Options.Schema, "configuration_publication"));
                Assert.AreEqual(family is "release" or "assignment" ? 1L : 0L, await Count(database, database.Options.Schema, "configuration_release"));

                Task<ManagementConfigurationResponse> Submit(ManagementConfigurationService service, string key) => family switch
                {
                    "catalog" => service.ApplyCatalogAsync(catalog, key, catalog.ETag, catalogCommand, CancellationToken.None),
                    "publication" => service.PublishAsync(source!, key, sourceETag!, publicationCommand, CancellationToken.None),
                    "release" => service.CreateReleaseAsync(binding!, key, mapETag!, releaseCommand, CancellationToken.None),
                    "assignment" => service.ReplaceAssignmentsAsync(binding!, key, mapETag!, assignmentCommand!, CancellationToken.None),
                    _ => throw new AssertFailedException("Select one fixed configuration family.")
                };

                Task<ManagementConfigurationResponse> Lookup(ManagementConfigurationService service, string key) => family switch
                {
                    "catalog" => service.LookupCatalogAsync(catalog, key, CancellationToken.None),
                    "publication" => service.LookupPublicationAsync(source!, key, CancellationToken.None),
                    "release" => service.LookupReleaseAsync(binding!, key, CancellationToken.None),
                    "assignment" => service.LookupAssignmentAsync(binding!, key, CancellationToken.None),
                    _ => throw new AssertFailedException("Select one fixed configuration family.")
                };
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

    // Native credential proof belongs to the HTTPS suite. This bounded application-port substitute
    // checks exact resource/capability requests while the original SQL/commit/Audit graph remains real.
    private sealed class FixedCaller : ICallerContextAccessor
    {
        public CallerContext? Current
        {
            get;
        } = new("configuration-native", "validated", new("native-test", "application"),
            SecurityActorKind.Application, "native-managed", "Development", new(DateTimeOffset.UtcNow), "configuration-boundary-v1");
    }

    private sealed class ExactConfigurationAuthorizer(LocalApplicationIdentity local, string catalogId) : ICapabilityAuthorizer
    {
        internal string? SourceId
        {
            get; set;
        }
        internal string? BindingId
        {
            get; set;
        }

        public ValueTask<AuthorizationDecision> AuthorizeAsync(IReadOnlyList<string> capabilities, CapabilityTarget target,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool catalog = target.ResourceType == ManagementConfigurationCapabilities.CatalogResource && target.ResourceIdentifier == catalogId
                && capabilities.Count == 1 && capabilities[0] is ManagementConfigurationCapabilities.CatalogApply or ManagementConfigurationCapabilities.OperationsRead;
            bool publication = SourceId is not null && target.ResourceType == ManagementConfigurationCapabilities.SourceResource && target.ResourceIdentifier == SourceId
                && capabilities.Count == 1 && capabilities[0] is ManagementConfigurationCapabilities.Publish or ManagementConfigurationCapabilities.OperationsRead;
            bool binding = BindingId is not null && target.ResourceType == ManagementConfigurationCapabilities.BindingResource && target.ResourceIdentifier == BindingId
                && capabilities.Count == 1 && capabilities[0] is ManagementConfigurationCapabilities.ReleaseCreate or ManagementConfigurationCapabilities.AssignmentWrite
                    or ManagementConfigurationCapabilities.OperationsRead;
            if (target.Application != local.Application || target.Environment != local.Environment || !catalog && !publication && !binding)
            {
                throw new InvalidOperationException("Unexpected authority request in the bounded configuration commit proof.");
            }
            return ValueTask.FromResult(AuthorizationDecision.Allow);
        }
    }
}
