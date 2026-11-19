using CIS.Metrics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace CIS.Security.Hosting;

// Explicit registration-time marker and one host's finite retained status owner.
// The exporter can reserve capacity without resolving deferred policy or services.
internal sealed class SecurityComposition : ISecurityProfileStatusReporter, ISecurityProfileStatusSource
{
    private readonly object gate = new();
    private readonly Dictionary<string, SecurityProfileStatus> statuses = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IAuthorizationRequirement[]> nativeRequirements = new(StringComparer.Ordinal);
    private SecurityPolicySnapshot? policy;
    private IServiceProvider? root;
    private TimeProvider clock = TimeProvider.System;

    internal SecurityComposition(int profileCapacity)
    {
        if (profileCapacity is < 1 or > 4096)
        {
            throw new ArgumentOutOfRangeException(nameof(profileCapacity));
        }
        ProfileCapacity = profileCapacity;
        Telemetry = new(this);
    }

    internal int ProfileCapacity
    {
        get;
    }
    internal SecurityTelemetry Telemetry
    {
        get;
    }
    internal ServiceDescriptor? ResultHandlerRegistration
    {
        get; set;
    }
    internal IReadOnlyList<string> ProfileAliases
    {
        get
        {
            lock (gate)
            {
                return Array.AsReadOnly(statuses.Keys.Order(StringComparer.Ordinal).ToArray());
            }
        }
    }

    internal void CaptureNativeRequirements(string profile, AuthorizationPolicy generated)
    {
        lock (gate)
        {
            if (nativeRequirements.TryGetValue(profile, out IAuthorizationRequirement[]? existing))
            {
                if (!existing.SequenceEqual(generated.Requirements, ReferenceEqualityComparer.Instance))
                {
                    throw new InvalidOperationException("Replacing a security profile's native requirements requires a fresh host instance.");
                }
                return;
            }
            nativeRequirements.Add(profile, generated.Requirements.ToArray());
        }
    }

    internal bool RetainsNativeRequirements(string profile, AuthorizationPolicy actual)
    {
        lock (gate)
        {
            return nativeRequirements.TryGetValue(profile, out IAuthorizationRequirement[]? expected)
                && expected.All(requirement => actual.Requirements.Any(candidate => ReferenceEquals(candidate, requirement)));
        }
    }

    internal void Bind(SecurityPolicySnapshot snapshot, IServiceProvider provider, TimeProvider timeProvider)
    {
        lock (gate)
        {
            if (root is not null && !ReferenceEquals(root, provider))
            {
                throw new InvalidOperationException("A security composition belongs to one actual root provider.");
            }
            if (policy is not null && !ReferenceEquals(policy, snapshot))
            {
                throw new InvalidOperationException("A security composition retains one immutable startup policy.");
            }
            if (policy is not null)
            {
                return;
            }
            root = provider;
            policy = snapshot;
            clock = timeProvider;
            foreach (SecurityProfile profile in snapshot.Profiles)
            {
                statuses.Add(profile.Alias, new(profile.Alias, SecurityProfileAvailability.Unavailable,
                    new(SecurityFailureKind.CredentialUnavailable, "The selected integration has not reported availability.")));
            }
        }
    }

    public void Publish(SecurityProfileStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        lock (gate)
        {
            if (!statuses.ContainsKey(status.Profile))
            {
                throw new ArgumentException("Publish status only for a registered security profile.", nameof(status));
            }
            statuses[status.Profile] = status;
        }
    }

    public void RecordCertificateReload(string profile, SecurityCertificateReloadOutcome outcome)
    {
        SecurityText.Alias(profile, nameof(profile));
        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome));
        }
        lock (gate)
        {
            if (policy is null || !policy.TryGetProfile(profile, out SecurityProfile? selected)
                || selected.Kind is not (SecurityProfileKind.ClientCertificate or SecurityProfileKind.LocalDevelopment))
            {
                throw new ArgumentException("Record certificate transitions only for registered certificate profiles.", nameof(profile));
            }
        }
        Telemetry.RecordReload(profile, outcome);
    }

    public IReadOnlyList<SecurityProfileStatus> CaptureStatus()
    {
        lock (gate)
        {
            return Array.AsReadOnly(statuses.Values.OrderBy(status => status.Profile, StringComparer.Ordinal).ToArray());
        }
    }

    internal IReadOnlyList<MetricObservation> ObserveCertificateRemaining()
    {
        lock (gate)
        {
            if (policy is null)
            {
                return Array.Empty<MetricObservation>();
            }
            DateTimeOffset now = clock.GetUtcNow();
            return statuses.Values.Where(status => status.CredentialExpiresAtUtc is not null
                    && policy.GetProfile(status.Profile).Kind is SecurityProfileKind.ClientCertificate or SecurityProfileKind.LocalDevelopment)
                .Select(status => new MetricObservation(Math.Max(0, (status.CredentialExpiresAtUtc!.Value - now).TotalSeconds),
                    new MetricDimensions(("profile", status.Profile)))).ToArray();
        }
    }
}

// No handles or provider resolution until the owning collector explicitly enables security sources.
internal sealed class SecurityTelemetry : IMetricsSource
{
    private readonly object gate = new();
    private readonly MetricsAttachment attachment = new();
    private readonly SecurityComposition composition;
    private Handles? handles;

    internal SecurityTelemetry(SecurityComposition composition)
    {
        this.composition = composition;
    }

    void IMetricsSource.AttachMetrics(MetricsRuntime runtime) => AttachMetrics(runtime);

    internal void AttachMetrics(MetricsRuntime runtime)
    {
        lock (gate)
        {
            if (!attachment.Attach(runtime))
            {
                return;
            }
            try
            {
                ICounter authorization = runtime.CreateCounter(new("cis.security.authorization.decisions",
                    allowedDimensionKeys: ["outcome", "profile"]));
                ICounter reload = runtime.CreateCounter(new("cis.security.certificate.reloads",
                    allowedDimensionKeys: ["outcome", "profile"]));
                runtime.RegisterObservableGauge(new("cis.security.certificate.remaining", MetricUnit.Seconds,
                    allowedDimensionKeys: ["profile"]), composition.ObserveCertificateRemaining);
                Volatile.Write(ref handles, new(authorization, reload));
            }
            catch (Exception error) when (MetricsRuntime.IsOrdinary(error))
            {
                // Observation does not change authorization or business behavior.
            }
        }
    }

    internal void RecordAuthorization(string profile, bool allowed)
    {
        try
        {
            Volatile.Read(ref handles)?.Authorization.Add(1,
                new MetricDimensions(("outcome", allowed ? "allow" : "deny"), ("profile", profile)));
        }
        catch (Exception error) when (MetricsRuntime.IsOrdinary(error))
        {
            // Metrics failure cannot alter a security decision.
        }
    }

    internal void RecordReload(string profile, SecurityCertificateReloadOutcome outcome)
    {
        string value = outcome switch
        {
            SecurityCertificateReloadOutcome.Adopted => "adopted",
            SecurityCertificateReloadOutcome.Rejected => "rejected",
            _ => "deferred"
        };
        try
        {
            Volatile.Read(ref handles)?.Reload.Add(1, new MetricDimensions(("outcome", value), ("profile", profile)));
        }
        catch (Exception error) when (MetricsRuntime.IsOrdinary(error))
        {
            // Metrics failure cannot alter adoption or owned cleanup.
        }
    }

    private sealed record Handles(ICounter Authorization, ICounter Reload);
}
