using System.Diagnostics.Metrics;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Azure.Core.Pipeline;
using CIS.Audit;
using CIS.Configuration;
using CIS.Examples.CountryData;
using CIS.Hosting;
using CIS.Management;
using CIS.Management.Hosting;
using CIS.Messaging;
using CIS.Messaging.Hosting;
using CIS.Messaging.PostgreSql;
using CIS.Messaging.RabbitMQ;
using CIS.RestClient;
using CIS.RestClient.Http;
using CIS.RuleEngine;
using CIS.Security;
using CIS.Security.Entra;
using CIS.Security.Hosting;
using CIS.Transactions;
using CIS.Transactions.AdoNet;
using CIS.Transactions.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CIS.Tests.MessagingCrashFixture;

internal static partial class Program
{
    private const string CountryEntraProfile = "country-rule-workload";
    private const string CountryBrokerProfile = "country-rule-broker";
    private const string CountryTenant = "11111111-1111-4111-8111-111111111111";
    private const string CountryObjectId = "22222222-2222-4222-8222-222222222222";
    private const string CountryClientId = "33333333-3333-4333-8333-333333333333";
    private const string CountryIssuer = "https://login.microsoftonline.com/" + CountryTenant + "/v2.0";

    static partial void SelectCountryDataCommand(string[] args, ref Task? command)
    {
        if (args is ["rule-engine-country-data", string descriptor])
        {
            command = RunCountryDataCommandAsync(descriptor);
        }
    }

    private static async Task RunCountryDataCommandAsync(string descriptor)
    {
        string phase = "validate-handoff";
        try
        {
            await RunCountryDataAsync(Absolute(descriptor), next => phase = next);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("CountryData fixture phase " + phase + " failed: " + error.GetType().Name);
            throw;
        }
    }

    private static async Task RunCountryDataAsync(string descriptor, Action<string> setPhase)
    {
        string directory = Path.GetDirectoryName(descriptor)!;
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(75));
        CancellationToken token = lifetime.Token;
        RuleEngineCountryDataInput input = await ReadCountryJsonAsync<RuleEngineCountryDataInput>(descriptor);
        if (input.ContractVersion != RuleEngineAuditEvents.Version || !OwnUuid(input.ExpectedVersion)
            || input.ExpectedReleaseId is { } releaseId && !OwnUuid(releaseId)
            || input.ResidenceCountry is not { Length: 3 } || !input.ResidenceCountry.All(char.IsAsciiLetterUpper)
            || !CountryLoopback(input.ManagementBaseUrl, requireTls: true) || !CountryLoopback(input.CountrySourceUrl, requireTls: false))
        {
            throw new InvalidDataException("Select an owned CountryData provider, Management endpoint and exact desired version.");
        }

        Schema(input.MessagingSchema);
        var postgres = new NpgsqlConnectionStringBuilder(await CountryTextAsync(directory, "postgres.txt"));
        if (postgres.Host is not ("localhost" or "127.0.0.1" or "::1") || !postgres.Pooling || postgres.MaxPoolSize is < 1 or > 6
            || postgres.Enlist || string.IsNullOrWhiteSpace(postgres.Database) || string.IsNullOrWhiteSpace(postgres.Username)
            || postgres.ApplicationName != input.MessagingSchema)
        {
            throw new InvalidDataException("Use the isolated CountryData message outbox fixture.");
        }
        RuleEngineCountryDataBroker broker = await ReadCountryJsonAsync<RuleEngineCountryDataBroker>(Path.Combine(directory, "rabbit.json"));
        if (broker.Host is not ("localhost" or "127.0.0.1" or "::1") || broker.ServerName != "localhost" || broker.Port is < 1 or > 65_535
            || !broker.VirtualHost.StartsWith("/cis.tc.", StringComparison.Ordinal)
            || !broker.Namespace.StartsWith(broker.VirtualHost[1..] + ".management.", StringComparison.Ordinal)
            || broker.SubjectIssuer != "rm07-fixture" || !broker.User.StartsWith("cis_other_", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(broker.Password))
        {
            throw new InvalidDataException("Use only the owned RabbitMQ producer handoff.");
        }
        LocalApplicationIdentity identity = LocalApplicationIdentity.Capture();
        var rabbit = new RabbitMqOptions
        {
            HostName = broker.Host,
            Port = broker.Port,
            ServerName = broker.ServerName,
            VirtualHost = broker.VirtualHost,
            Namespace = broker.Namespace,
            SubjectIssuer = broker.SubjectIssuer,
            SecurityProfile = CountryBrokerProfile,
            UserName = broker.User,
            Password = broker.Password,
            TrustedRootCertificatePem = await CountryTextAsync(directory, "rabbit-ca.pem"),
            // The producer uses the existing installed route and performs passive native topology validation.
            ValidateTopologyEquivalence = false,
            EndpointNamespaces = [new(identity.Application, identity.Environment, "management")]
        };
        var management = new ManagementClientOptions("country-rule-management") { BaseUrl = input.ManagementBaseUrl };
        var configuration = new ManagementConfigurationClientOptions("country-rule-configuration") { BaseUrl = input.ManagementBaseUrl };
        var registration = new ManagementRegistrationOptions(management.RestClientName)
        {
            ReportingInterval = TimeSpan.FromMilliseconds(100),
            ControlCheckInterval = TimeSpan.FromMilliseconds(100)
        };
        using var handler = new CountryWorkloadExchange(await CountryTextAsync(directory, "workload-token.txt"));
        using var sdkHttp = new HttpClient(handler);
        using var trustedRoot = X509Certificate2.CreateFromPem(await CountryTextAsync(directory, "management-ca.pem"));
        var trust = new RestHttpServerTrust([trustedRoot], X509RevocationMode.NoCheck);
        string assertion = Path.Combine(directory, "country-assertion.txt");
        await File.WriteAllTextAsync(assertion, "owned.fixture.country.assertion", token);
        var central = new CISApplicationCentralConfigurationOptions(management, configuration, registration,
            async cancellationToken => (await CountryNativePreparation.CreateAsync(management, configuration, rabbit,
                identity, trust, assertion, new HttpClientTransport(sdkHttp), cancellationToken)).Descriptor)
        {
            Environment = identity.Environment,
            ExpectedCohortId = input.ExpectedCohortId,
            Timeout = TimeSpan.FromSeconds(30)
        };

        setPhase("compose-host");
        var assembly = typeof(CountryApplication).Assembly;
        string hostFile = Path.GetFileName(assembly.Location);
        File.Copy(assembly.Location, Path.Combine(directory, hostFile));
        string inventory = Path.Combine(directory, "country.types.json");
        await File.WriteAllBytesAsync(inventory, JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            hostAssembly = assembly.FullName,
            assemblies = new[] { new { assemblyName = assembly.FullName, relativePath = hostFile } }
        }), token);
        await using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(postgres.ConnectionString);
        await using CISApplication app = CountryApplication.CreateService([], builder =>
        {
            builder.WebHost!.UseKestrel().UseUrls("http://127.0.0.1:0");
            builder.Host.Logging.ClearProviders();
            builder.LoggingEnabled = false;
            builder.TypeManager = new(assembly)
            {
                AssemblyManifestPath = inventory
            };
            builder.Configuration = new(assembly.GetName().Name!)
            {
                Factories = new Dictionary<Type, Func<IConfigurationObject>>
                {
                    [typeof(CountrySettings)] = () => new CountrySettings { SourceUrl = input.CountrySourceUrl }
                }
            };
            builder.Services.AddCisAdoNetDatabase("Messaging", _ => dataSource,
                new()
                {
                    SupportedIsolationLevels = [TransactionIsolation.ReadCommitted]
                });
            builder.Services.AddSingleton<IMessagingStore>(new PostgreSqlMessagingStore(new()
            {
                Schema = input.MessagingSchema
            }));
            builder.Services.AddCisMessageStore(new("Messaging", "Messaging"));
            builder.Services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, BrokerOnlyScheme>("cis-rabbitmq", _ => { });
            builder.Services.AddCisRabbitMqMessaging(rabbit);
            CountryResidenceRules.Configure(builder, central, TimeSpan.FromMilliseconds(100),
                new MessagingOptions { IdlePollInterval = TimeSpan.FromMilliseconds(50) });
            if (input.HoldAfterCommittedStart)
            {
                ServiceDescriptor original = builder.Services.Single(service => service.ServiceType == typeof(IRuleEvaluator));
                builder.Services.Replace(ServiceDescriptor.Scoped<IRuleEvaluator>(scope => (IRuleEvaluator)original.ImplementationFactory!(
                    new CountryAuditGateScope(scope, directory))));
            }
        });
        setPhase("start-host");
        await app.StartAsync(token);
        var runtime = app.Services.GetRequiredService<ConfigurationRuntime>();
        var session = app.Services.GetRequiredService<ManagementRegistration>();
        setPhase("adopt-version");
        await WaitForCountryAsync(() =>
        {
            ConfigurationAcceptanceStatus? acceptance = runtime.CaptureStatus().Entries
                .Single(entry => entry.ConfigurationType == typeof(CountryResidenceRules)).Acceptance;
            return Task.FromResult(acceptance is { HasIntactAcceptedState: true, LastAccepted: { } accepted }
                && (input.ExpectedReleaseId is null || input.ExpectedReleaseId == accepted.Desired.ReleaseId)
                && session.CaptureStatus().Admission?.AdmissionAllowed == true);
        }, token);

        setPhase("evaluate");
        string correlation = "country-example-" + Guid.NewGuid().ToString("N");
        RuleEvaluationResult result;
        await using (AsyncServiceScope scope = app.Services.CreateAsyncScope())
        {
            result = await scope.ServiceProvider.GetRequiredService<IRuleEvaluator>().EvaluateAsync(
                CountryResidenceRules.CreateRequest(input.ResidenceCountry, input.Pensioner,
                    input.RequestExactVersion ? input.ExpectedVersion : null, correlation), token);
        }
        string registrationId = session.CaptureStatus().RegistrationId;
        var report = new RuleEngineCountryDataResult(System.Environment.ProcessId, identity.InstanceId, registrationId,
            result.ExecutionId, result.RulesetId, result.Version, result.Status.ToString(),
            result.Matches.Select(row => new RuleEngineCountryDataMatch(row.RuleId, row.Ordinal, row.WeightText, row.Metadata?.GetString())).ToArray(),
            result.Trace.Select(row => new RuleEngineCountryDataTrace(row.RowIndex, row.State.ToString(), row.Outcome.ToString())).ToArray(),
            result.Error?.Code);
        setPhase("confirm-audit-delivery");
        if (result.Status == RuleExecutionStatus.Completed)
        {
            // Each child owns a fresh schema and exactly one execution. Let normal delivery confirm both occurrences before shutdown.
            await WaitForCountryAsync(async () =>
            {
                await using NpgsqlCommand command = dataSource.CreateCommand(
                    $"SELECT count(*) FROM \"{input.MessagingSchema}\".outbox WHERE state=2");
                return await command.ExecuteScalarAsync(token) is 2L;
            }, token);
        }
        await MarkerAsync(Path.Combine(directory, "country-result.json"), report);
        setPhase("stop-host");
        await app.StopAsync(token);
    }

    private static async Task WaitForCountryAsync(Func<Task<bool>> ready, CancellationToken token)
    {
        while (!await ready())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50), token);
        }
    }

    private static bool CountryLoopback(string value, bool requireTls)
        => Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && uri.IsLoopback && uri.Port > 0
            && (uri.Scheme == Uri.UriSchemeHttps || !requireTls && uri.Scheme == Uri.UriSchemeHttp)
            && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0;

    private static async Task<T> ReadCountryJsonAsync<T>(string path) where T : class
        => JsonSerializer.Deserialize<T>(await ReadBoundedAsync(path), Json)
            ?? throw new InvalidDataException("The owned CountryData input was empty.");

    private static async Task<string> CountryTextAsync(string directory, string name)
        => Encoding.UTF8.GetString(await ReadBoundedAsync(Path.Combine(directory, name))).Trim();

    private sealed class CountryAuditGateScope(IServiceProvider scope, string directory) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(IAuditWriter)
            ? new CountryAuditGate(scope.GetRequiredService<IAuditWriter>(), directory) : scope.GetService(serviceType);
    }

    private sealed class CountryAuditGate(IAuditWriter inner, string directory) : IAuditWriter
    {
        public Task<AuditStageResult> StageAsync(ILocalTransactionContext context, CapturedAuditRecord record,
            CancellationToken cancellationToken = default) => inner.StageAsync(context, record, cancellationToken);

        public async Task<AuditSubmissionResult> SubmitAsync(CapturedAuditRecord record, CancellationToken cancellationToken = default)
        {
            AuditSubmissionResult result = await inner.SubmitAsync(record, cancellationToken);
            if (record.Record.EventType == RuleEngineAuditEvents.ExecutionStarted && result.Outcome == AuditSubmissionOutcome.OutboxCommitted)
            {
                using var details = JsonDocument.Parse(record.Record.DetailsJson);
                await MarkerAsync(Path.Combine(directory, "country-started.json"),
                    new RuleEngineCountryDataStarted(details.RootElement.GetProperty("executionId").GetGuid(),
                        details.RootElement.GetProperty("version").GetString()!));
                await WaitForCountryAsync(() => Task.FromResult(File.Exists(Path.Combine(directory, "country-proceed"))), cancellationToken);
            }
            return result;
        }
    }

    private sealed class CountryNativePreparation : IAsyncDisposable
    {
        private readonly EntraPreparedProfile prepared;
        private readonly RestHttpTransport managementBootstrap;
        private readonly RestHttpTransport configurationBootstrap;
        private readonly List<RestHttpTransport> finalTransports = [];

        private CountryNativePreparation(EntraPreparedProfile prepared, ManagementClientOptions management,
            ManagementConfigurationClientOptions configuration, RestHttpServerTrust trust)
        {
            this.prepared = prepared;
            managementBootstrap = RestHttpTransport.CreateSecurityBound(prepared.Binding, serverTrust: trust);
            configurationBootstrap = RestHttpTransport.CreateSecurityBound(prepared.Binding, serverTrust: trust);
            Descriptor = new(this, Bootstrap(management.RestClientName), Bootstrap(configuration.RestClientName), services =>
            {
                services.AddCisSecurity(prepared.Policy);
                services.AddCisEntraSecurity(prepared);
                services.AddCisEntraRestClient(new RestClientOptions(management.RestClientName) { LogCalls = false }
                    .ConfigureCisManagementRestClient(management), CountryEntraProfile, Final);
                services.AddCisManagementClient(management);
                services.AddCisEntraRestClient(new RestClientOptions(configuration.RestClientName) { LogCalls = false }
                    .ConfigureCisManagementConfigurationRestClient(configuration), CountryEntraProfile, Final);
                services.AddCisManagementConfigurationClient(configuration);
            }, prepared.ActivateFinalAsync, RetireAsync, managementBootstrap, configurationBootstrap);

            RestClientOptions Bootstrap(string name) => new(name)
            {
                SecurityBinding = prepared.Binding,
                Authenticate = prepared.AuthenticateAsync,
                LogCalls = false
            };
            RestHttpTransport Final(IServiceProvider root, RestSecurityBinding binding)
            {
                RestHttpTransport transport = RestHttpTransport.CreateSecurityBound(binding,
                    root.GetRequiredService<IMeterFactory>(), serverTrust: trust);
                finalTransports.Add(transport);
                return transport;
            }
        }

        internal CISApplicationCentralConfigurationPreparation Descriptor
        {
            get;
        }

        internal static async Task<CountryNativePreparation> CreateAsync(ManagementClientOptions management,
            ManagementConfigurationClientOptions configuration, RabbitMqOptions rabbit, LocalApplicationIdentity identity,
            RestHttpServerTrust trust, string assertion, HttpClientTransport transport, CancellationToken token)
        {
            var policy = new SecurityOptions { PolicyVersion = "country-rule-fixture-v1" };
            RabbitMqSettings broker = rabbit.Capture();
            policy.Profiles.Add(new(CountryEntraProfile, SecurityProfileKind.EntraWorkloadIdentity, SecurityProfileRoles.Outbound,
                outboundIdentity: new(identity.Application, identity.Environment, new(CountryIssuer, CountryObjectId)),
                allowedOrigins: [new Uri(management.BaseUrl!).GetLeftPart(UriPartial.Authority)], requiredForReadiness: true));
            policy.Profiles.Add(new(CountryBrokerProfile, SecurityProfileKind.Native, SecurityProfileRoles.Inbound,
                authenticationScheme: "cis-rabbitmq",
                identityMappings: [new(new(broker.SubjectIssuer, broker.UserName), identity.Application, identity.Environment)]));
            EntraPreparedProfile prepared = await EntraPreparedProfile.PrepareAsync(SecurityPolicySnapshot.Create(policy),
                CountryEntraProfile, new()
                {
                    TenantId = CountryTenant,
                    Outbound = new()
                    {
                        ClientId = CountryClientId,
                        ServicePrincipalObjectId = CountryObjectId,
                        FederatedSubject = "system:serviceaccount:rm08:configuration-bootstrap",
                        FederatedTokenFile = assertion,
                        Scope = "api://management-native-service/.default",
                        Transport = transport
                    }
                }, cancellationToken: token);
            try
            {
                return new(prepared, management, configuration, trust);
            }
            catch
            {
                await prepared.DisposeAsync();
                throw;
            }
        }

        private async Task RetireAsync()
        {
            await managementBootstrap.DisposeAsync();
            await configurationBootstrap.DisposeAsync();
            await prepared.RetireBootstrapAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await RetireAsync();
            foreach (RestHttpTransport transport in finalTransports)
            {
                await transport.DisposeAsync();
            }
            await prepared.DisposeAsync();
        }
    }

    // Only the cloud token exchange is substituted. The prepared SDK and Management TLS/registration stay native.
    private sealed class CountryWorkloadExchange(string accessToken) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.RequestUri is not { Host: "login.microsoftonline.com" } uri)
            {
                throw new InvalidDataException("The fixture exchange accepts only its owned Entra discovery and token calls.");
            }

            object body = uri.AbsolutePath switch
            {
                var path when path.EndsWith("/token", StringComparison.Ordinal) => new
                {
                    access_token = accessToken,
                    token_type = "Bearer",
                    expires_in = 3600
                },
                var path when path.EndsWith("/discovery/instance", StringComparison.Ordinal) => new
                {
                    tenant_discovery_endpoint = CountryIssuer + "/.well-known/openid-configuration",
                    metadata = new[] { new { preferred_network = uri.Host, preferred_cache = uri.Host, aliases = new[] { uri.Host } } }
                },
                _ => new
                {
                    issuer = CountryIssuer,
                    token_endpoint = "https://login.microsoftonline.com/" + CountryTenant + "/oauth2/v2.0/token",
                    authorization_endpoint = "https://login.microsoftonline.com/" + CountryTenant + "/oauth2/v2.0/authorize",
                    jwks_uri = "https://login.microsoftonline.com/discovery/v2.0/keys"
                }
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
            });
        }
    }
}
