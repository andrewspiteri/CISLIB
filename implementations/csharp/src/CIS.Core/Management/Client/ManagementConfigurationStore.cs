using System.Text.Json;
using CIS.Configuration;
using CIS.Management.Wire;
using CIS.RestClient;

namespace CIS.Management;

/// <summary>Bootstrap-capable central paired reads, with optional explicit routing of ordinary roots to their existing store.</summary>
/// <remarks>The host owns both supplied clients/stores. This adapter creates no timer, polling policy or persistent cache.</remarks>
public sealed class ManagementConfigurationStore : IConfigurationStore, IConfigurationSnapshotStore
{
    private readonly object gate = new();
    private readonly IConfigurationStore? noncentral;
    private IManagementConfigurationClient client;
    private bool hostedClientAdopted;

    /// <summary>Captures borrowed clients and preserves the legacy store's watch/reconnect guarantee.</summary>
    public ManagementConfigurationStore(IManagementConfigurationClient client, IConfigurationStore? noncentralStore = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        this.client = client;
        noncentral = noncentralStore;
        ConfigurationNotificationMode selected = noncentralStore?.NotificationMode ?? ConfigurationNotificationMode.None;
        if (!Enum.IsDefined(selected))
        {
            throw new ArgumentException("Select a defined legacy notification guarantee.", nameof(noncentralStore));
        }
        NotificationMode = selected == ConfigurationNotificationMode.Reliable
            ? ConfigurationNotificationMode.RequiresReconciliation : selected;
    }

    /// <inheritdoc />
    public ConfigurationNotificationMode NotificationMode
    {
        get;
    }

    /// <inheritdoc />
    public ValueTask<ConfigurationReadResult> ReadAsync(ConfigurationRecordKey key, string? sourceToken, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return noncentral is null ? ValueTask.FromResult(ConfigurationReadResult.Failure())
            : noncentral.ReadAsync(key, sourceToken, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<IAsyncDisposable> WatchAsync(IReadOnlyList<ConfigurationRecordKey> keys,
        Action<ConfigurationInvalidation> invalidated, CancellationToken cancellationToken)
        => noncentral is null ? ValueTask.FromException<IAsyncDisposable>(new NotSupportedException("The pure central store uses explicit polling."))
            : noncentral.WatchAsync(keys, invalidated, cancellationToken);

    // WORK-006 calls this once after real bootstrap REST retirement and before
    // Configuration activation. This adapter never disposes either borrowed port.
    internal void AdoptHostedClient(IManagementConfigurationClient hostedClient)
    {
        ArgumentNullException.ThrowIfNull(hostedClient);
        lock (gate)
        {
            if (hostedClientAdopted)
            {
                throw new InvalidOperationException("Adopt the final native configuration client exactly once.");
            }
            client = hostedClient;
            hostedClientAdopted = true;
        }
    }

    internal async Task<ManagementRuleWorkloadVersion?> ReadRuleVersionAsync(ConfigurationSnapshotTarget target,
        ConfigurationEnrollment enrollment, string rulesetId, string version, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(enrollment);
        token.ThrowIfCancellationRequested();
        if (!ManagementWire.IsUuid(version))
        {
            return null;
        }

        IManagementRuleWorkloadClient? selected;
        lock (gate)
        {
            selected = hostedClientAdopted ? client as IManagementRuleWorkloadClient : null;
        }
        if (selected is null)
        {
            throw new ManagementRuleWorkloadVersionReadException(invalidResponse: false);
        }
        var expected = new ManagementConfigurationSnapshotTarget(target.AuthorityId, target.ApplicationId, target.Environment,
            target.RegistrationId, enrollment.ConfigurationId, enrollment.SchemaVersion, target.HostApplicationName, target.CohortId);
        ManagementConfigurationCallResult<ManagementRuleWorkloadVersion> response = await selected.GetRegistrationRuleVersionAsync(
            expected, rulesetId, version, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        RestResult<ManagementRuleWorkloadVersion> exchange = response.Exchange;
        if (exchange.Success && exchange.Context.HasValue && exchange.Value is { } exact)
        {
            return exact;
        }
        if (exchange.Context.HttpStatusCode == 404 && response.Problem?.Code == "management.configuration.not_found")
        {
            return null;
        }
        throw new ManagementRuleWorkloadVersionReadException(exchange.Success || exchange.Context.FailureKind == RestFailureKind.InvalidResponse);
    }

    /// <inheritdoc />
    public async ValueTask<ConfigurationSnapshotReadResult> ReadSnapshotAsync(ConfigurationSnapshotRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Enrollment is not { } enrollment || request.ExpectedTarget is not { } target)
        {
            throw new ArgumentException("The central adapter requires explicit independent enrollment/target inputs.", nameof(request));
        }
        var expected = new ManagementConfigurationSnapshotTarget(target.AuthorityId, target.ApplicationId, target.Environment,
            target.RegistrationId, enrollment.ConfigurationId, enrollment.SchemaVersion, target.HostApplicationName, target.CohortId);
        ManagementConfigurationSnapshot? retained = Retained(request, expected);
        IManagementConfigurationClient selected;
        lock (gate)
        {
            selected = client;
        }
        ManagementConfigurationCallResult<ManagementConfigurationSnapshot> response = await selected.GetRegistrationConfigurationAsync(
            expected, retained, cancellationToken: cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (response.Exchange.Success && response.Exchange.Context.HasValue && response.Exchange.Value is { } snapshot)
        {
            ConfigurationDesiredIdentity desired = Desired(snapshot.Desired);
            if (!request.MatchesTarget(desired))
            {
                return ConfigurationSnapshotReadResult.Failure("Configuration.SnapshotTarget");
            }
            try
            {
                return ConfigurationSnapshotReadResult.Complete(Source(snapshot.CommonSource), Source(snapshot.ApplicationSource), snapshot.ETag,
                    new(desired, Reference(snapshot.CommonSource), Reference(snapshot.ApplicationSource)));
            }
            catch (JsonException)
            {
                return ConfigurationSnapshotReadResult.Failure("Configuration.SourceFormat", desired);
            }
        }
        RestResultContext facts = response.Exchange.Context;
        if (retained is not null && facts.HttpStatusCode == 304 && facts.FailureKind == RestFailureKind.Http
            && response.Exchange.ErrorCode == "http.304" && facts.ErrorBody is { Bytes.Count: 0 } && !facts.ErrorBodyTruncated
            && facts.Headers.TryGetValue("ETag", out IReadOnlyList<string>? tags) && tags.Count == 1 && tags[0] == retained.ETag)
        {
            return ConfigurationSnapshotReadResult.Unchanged();
        }
        if (response.ContentRejection is { } rejected)
        {
            ConfigurationDesiredIdentity desired = Desired(rejected.Desired);
            if (request.MatchesTarget(desired))
            {
                return ConfigurationSnapshotReadResult.Failure(ContentFailure(rejected.Code), desired);
            }
        }
        return ConfigurationSnapshotReadResult.Failure("Configuration.SnapshotRead");
    }

    private static ConfigurationDesiredIdentity Desired(ManagementConfigurationDesiredContext desired)
        => new(new(desired.AuthorityId, desired.ApplicationId, desired.Environment, desired.RegistrationId,
            desired.HostApplicationName, desired.CohortId), desired.ConfigurationId, desired.SchemaVersion, desired.ReleaseId, desired.Generation);

    private static string ContentFailure(string code) => code switch
    {
        "configuration.integrity" => "Configuration.SourceIntegrity",
        "configuration.content.schema" => "Configuration.SourceSchema",
        "configuration.version.unsupported" => "Configuration.SourceSchema",
        "configuration.content.scope" => "Configuration.SourceScope",
        "configuration.content.invalid" => "Configuration.SourceFormat",
        "configuration.content.tooLarge" => "Configuration.SourceSize",
        "configuration.content.fetch" => "Configuration.SourceFetch",
        _ => "Configuration.ContentRejected"
    };

    private static ConfigurationPublicationReference? Reference(ManagementConfigurationSourceContent source)
        => source.State == "absent" ? null : new(source.SourceId!, source.PublicationId!, source.Sha256!);

    private static ConfigurationReadResult Source(ManagementConfigurationSourceContent source)
    {
        if (source.State == "absent")
        {
            return ConfigurationReadResult.Missing();
        }
        if (source.State != "present" || source.PayloadJson is null)
        {
            throw new JsonException("The central port returned no valid source state.");
        }
        using JsonDocument document = JsonDocument.Parse(source.PayloadJson, new JsonDocumentOptions { MaxDepth = 16 });
        return ConfigurationReadResult.FoundExact(document.RootElement, source.PayloadJson);
    }

    private static ManagementConfigurationSnapshot? Retained(ConfigurationSnapshotRequest request, ManagementConfigurationSnapshotTarget expected)
    {
        ConfigurationSnapshotReadResult? pair = request.RetainedPair;
        if (pair?.Kind != ConfigurationSnapshotReadKind.Complete || pair.SnapshotToken != request.SnapshotToken
            || pair.Provenance is not { } provenance || !request.MatchesTarget(provenance.Desired))
        {
            return null;
        }
        ManagementConfigurationSourceContent? common = RetainedSource(pair.Common!, provenance.CommonSource, expected, "common");
        ManagementConfigurationSourceContent? application = RetainedSource(pair.Application!, provenance.ApplicationSource, expected, "application");
        if (common is null || application is null)
        {
            return null;
        }
        ConfigurationDesiredIdentity desired = provenance.Desired;
        return new(new(expected.AuthorityId, expected.ApplicationId, expected.Environment, expected.RegistrationId,
            expected.ConfigurationId, expected.SchemaVersion, expected.HostApplicationName, desired.ReleaseId, desired.AssignmentGeneration, expected.CohortId),
            pair.SnapshotToken!, common, application);
    }

    private static ManagementConfigurationSourceContent? RetainedSource(ConfigurationReadResult source,
        ConfigurationPublicationReference? reference, ManagementConfigurationSnapshotTarget target, string kind)
        => source.Kind == ConfigurationReadKind.Missing && reference is null ? new("absent")
            : source.Kind == ConfigurationReadKind.Found && source.ExactSourceText is { } exact && reference is not null
                ? new("present", PublicationId: reference.PublicationId, Sha256: reference.Sha256, PayloadJson: exact,
                    SourceId: reference.SourceId, ConfigurationId: target.ConfigurationId, SchemaVersion: target.SchemaVersion,
                    Environment: target.Environment, Kind: kind, ApplicationId: kind == "application" ? target.ApplicationId : null) : null;
}

internal sealed class ManagementRuleWorkloadVersionReadException(bool invalidResponse) : InvalidOperationException(
    invalidResponse ? "The retained Rule Engine version response was invalid." : "The retained Rule Engine version could not be read.")
{
    internal bool InvalidResponse { get; } = invalidResponse;
}
