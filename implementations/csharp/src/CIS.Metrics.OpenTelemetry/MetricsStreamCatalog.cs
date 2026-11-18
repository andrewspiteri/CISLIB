using System.Diagnostics.Metrics;
using global::OpenTelemetry.Metrics;

namespace CIS.Metrics.OpenTelemetry;

// Finite policy metadata and capacity reservations; aggregate values belong solely to the SDK.
internal sealed class MetricsStreamCatalog
{
    internal static readonly IReadOnlyList<double> DurationBounds = Array.AsReadOnly<double>([0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1, 2.5, 5, 7.5, 10]);
    private readonly object gate = new();
    private readonly MetricsSettings settings;
    private readonly Dictionary<string, StreamPolicy> policies = new(StringComparer.Ordinal);
    private readonly HashSet<string> selectedCore = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ModuleProfile> modules = new(StringComparer.Ordinal);
    private int capacities;

    internal MetricsStreamCatalog(MetricsSettings settings)
    {
        this.settings = settings;
        if (settings.CollectIncomingHttp)
        {
            Reserve("http.server.request.duration", MetricKind.Histogram, settings.HttpDurationCardinalityLimit,
                ["http.request.method", "http.route", "http.response.status_code"], DurationBounds);
            Reserve("http.server.active_requests", MetricKind.UpDownCounter, settings.CardinalityLimit,
                ["http.request.method", "url.scheme"], []);
        }
        if (settings.CollectOutgoingHttp)
        {
            Reserve("http.client.request.duration", MetricKind.Histogram, settings.HistogramCardinalityLimit,
                ["http.request.method"], DurationBounds);
            Reserve("http.client.active_requests", MetricKind.UpDownCounter, settings.CardinalityLimit,
                ["http.request.method"], []);
        }
        if (settings.CollectCoreModules)
        {
            Module("cis.logging.records", MetricKind.Counter, MetricUnit.Count, ["outcome"]);
            Module("cis.logging.processing.records", MetricKind.Counter, MetricUnit.Count, ["outcome"]);
            Module("cis.logging.discarded.records", MetricKind.Counter, MetricUnit.Count, ["reason"]);
            Module("cis.logging.writer.failures", MetricKind.Counter, MetricUnit.Count, []);
            Module("cis.heartbeat.execution.duration", MetricKind.Timer, MetricUnit.Seconds, ["outcome"]);
            Module("cis.heartbeat.active.executions", MetricKind.UpDownCounter, MetricUnit.Count, []);
            Module("cis.healthcheck.evaluation.duration", MetricKind.Timer, MetricUnit.Seconds, ["outcome"]);
            Module("cis.healthcheck.ready", MetricKind.ObservableGauge, MetricUnit.Count, []);
            Module("cis.healthcheck.monitors", MetricKind.ObservableGauge, MetricUnit.Count, ["state"]);
            Module("cis.startup.operation.duration", MetricKind.Timer, MetricUnit.Seconds, ["outcome", "stage"]);
            Module("cis.startup.completed", MetricKind.ObservableGauge, MetricUnit.Count, []);
            Module("cis.configuration.operation.duration", MetricKind.Timer, MetricUnit.Seconds, ["operation", "outcome"]);
            Module("cis.configuration.prepared", MetricKind.ObservableGauge, MetricUnit.Count, []);
            Module("cis.configuration.entries", MetricKind.ObservableGauge, MetricUnit.Count, ["state"]);
            Module("cis.configuration.initialization.duration", MetricKind.ObservableGauge, MetricUnit.Seconds, []);
            Module("cis.configuration.last.success", MetricKind.ObservableGauge, MetricUnit.Seconds, []);
            Module("cis.typemanager.assemblies", MetricKind.ObservableGauge, MetricUnit.Count, []);
            Module("cis.typemanager.types", MetricKind.ObservableGauge, MetricUnit.Count, []);
            Module("cis.typemanager.initialization.duration", MetricKind.ObservableGauge, MetricUnit.Seconds, []);
            if (settings.CollectRestClient)
            {
                Module("cis.restclient.operation.duration", MetricKind.Timer, MetricUnit.Seconds, ["client", "operation", "outcome"], 512);
                Module("cis.restclient.active.operations", MetricKind.UpDownCounter, MetricUnit.Count, ["client"]);
                Module("cis.restclient.payload.uploaded", MetricKind.Counter, MetricUnit.Bytes, ["client", "operation"]);
                Module("cis.restclient.payload.downloaded", MetricKind.Counter, MetricUnit.Bytes, ["client", "operation"]);
            }
            if (modules.Count > settings.MaxDefinitions)
            { throw new ArgumentException("The known CIS module definitions exceed the configured definition capacity."); }
        }
        // Explicit stream overrides reserve their budget even when the static producer appears later.
        foreach ((string name, StreamOverride profile) in settings.Streams)
        {
            if (policies.ContainsKey(name)) { continue; }
            Reserve(name, null, profile.CardinalityLimit ?? Math.Max(settings.CardinalityLimit, settings.HistogramCardinalityLimit), [], profile.HistogramBounds ?? []);
        }
    }

    internal IReadOnlyList<string> NativeExportedNames
    {
        get
        {
            var names = new List<string>();
            if (settings.CollectIncomingHttp) { names.AddRange(["http_server_request_duration_seconds", "http_server_request_duration_seconds_bucket",
                "http_server_request_duration_seconds_sum", "http_server_request_duration_seconds_count",
                "http_server_request_duration_seconds_created", "http_server_active_requests"]); }
            if (settings.CollectOutgoingHttp) { names.AddRange(["http_client_request_duration_seconds", "http_client_request_duration_seconds_bucket",
                "http_client_request_duration_seconds_sum", "http_client_request_duration_seconds_count",
                "http_client_request_duration_seconds_created", "http_client_active_requests"]); }
            return names.AsReadOnly();
        }
    }
    internal IReadOnlyList<MetricsStreamSnapshot> Capture()
    {
        lock (gate)
        {
            return Array.AsReadOnly(policies.Values.OrderBy(policy => policy.Name, StringComparer.Ordinal)
                .Select(policy => new MetricsStreamSnapshot(policy.Name, policy.Capacity,
                    Array.AsReadOnly(policy.Keys.ToArray()), Array.AsReadOnly(policy.Bounds.ToArray()),
                    IsSelectedNative(policy.Name) || selectedCore.Contains(policy.Name))).ToArray());
        }
    }

    internal bool Admit(MetricRegistration registration)
    {
        lock (gate)
        {
            string name = registration.Definition.Name;
            if (modules.TryGetValue(name, out ModuleProfile? module)
                && (module.Kind != registration.Kind || module.Unit != registration.Definition.Unit
                    || !module.Keys.SequenceEqual(registration.Definition.AllowedDimensionKeys)))
            { throw new InvalidOperationException("The instrument conflicts with the fixed CIS module stream profile."); }
            bool histogram = registration.Kind is MetricKind.Histogram or MetricKind.Timer;
            StreamOverride? supplied = settings.Streams.GetValueOrDefault(name);
            if (!histogram && supplied?.HistogramBounds is not null)
            { throw new ArgumentException("A non-histogram stream cannot accept histogram boundaries."); }
            int capacity = supplied?.CardinalityLimit ?? (histogram ? settings.HistogramCardinalityLimit : settings.CardinalityLimit);
            IReadOnlyList<double> bounds = supplied?.HistogramBounds ?? registration.HistogramBounds;
            if (policies.TryGetValue(name, out StreamPolicy? existing))
            {
                if (existing.Kind is not null && existing.Kind != registration.Kind)
                { throw new InvalidOperationException("The instrument conflicts with a reserved stream kind."); }
                // An override without an explicit capacity reserved the larger possible default until kind was known.
                int effectiveCapacity = existing.Kind is null ? capacity : existing.Capacity;
                policies[name] = new(name, registration.Kind, effectiveCapacity, registration.Definition.AllowedDimensionKeys, bounds);
                capacities += effectiveCapacity - existing.Capacity;
                selectedCore.Add(name);
                return true;
            }
            if (policies.Count >= settings.MaxStreams || (long)capacities + capacity > settings.TotalCardinalityLimit) { return false; }
            policies.Add(name, new(name, registration.Kind, capacity, registration.Definition.AllowedDimensionKeys, bounds));
            capacities += capacity;
            selectedCore.Add(name);
            return true;
        }
    }

    private bool IsSelectedNative(string name) => name switch
    {
        "http.server.request.duration" or "http.server.active_requests" => settings.CollectIncomingHttp,
        "http.client.request.duration" or "http.client.active_requests" => settings.CollectOutgoingHttp,
        _ => false
    };

    private void Module(string name, MetricKind kind, MetricUnit unit, IReadOnlyList<string> keys, int? capacity = null)
    {
        modules.Add(name, new(kind, unit, keys));
        bool histogram = kind is MetricKind.Histogram or MetricKind.Timer;
        Reserve(name, kind, capacity ?? (histogram ? settings.HistogramCardinalityLimit : settings.CardinalityLimit),
            keys, histogram ? DurationBounds : []);
    }

    internal MetricStreamConfiguration Select(Instrument instrument, MetricsRuntime runtime, Meter hostingMeter, Meter? outgoingMeter = null)
    {
        StreamPolicy? policy;
        if (ReferenceEquals(instrument.Meter, runtime.Meter) && runtime.TryGetRegistration(instrument, out MetricRegistration? registration))
        {
            lock (gate) { policy = selectedCore.Contains(registration.Definition.Name) ? policies.GetValueOrDefault(registration.Definition.Name) : null; }
        }
        else if (settings.CollectIncomingHttp && ReferenceEquals(instrument.Meter, hostingMeter)
            && (instrument.Name == "http.server.request.duration" && instrument is Histogram<double> && instrument.Unit == "s"
                && instrument.Description == "Duration of HTTP server requests."
                || instrument.Name == "http.server.active_requests" && instrument is UpDownCounter<long> && instrument.Unit == "{request}"
                && instrument.Description == "Number of active HTTP server requests."))
        { lock (gate) { policy = policies.GetValueOrDefault(instrument.Name); } }
        else if (settings.CollectOutgoingHttp && ReferenceEquals(instrument.Meter, outgoingMeter)
            && (instrument.Name == "http.client.request.duration" && instrument is Histogram<double> && instrument.Unit == "s"
                || instrument.Name == "http.client.active_requests" && instrument is UpDownCounter<long> && instrument.Unit == "{request}"))
        { lock (gate) { policy = policies.GetValueOrDefault(instrument.Name); } }
        else { return MetricStreamConfiguration.Drop; }
        if (policy is null) { return MetricStreamConfiguration.Drop; }
        if (policy.Kind is MetricKind.Histogram or MetricKind.Timer)
        {
            return new ExplicitBucketHistogramConfiguration
            {
                Boundaries = policy.Bounds.ToArray(),
                TagKeys = policy.Keys.ToArray(),
                CardinalityLimit = policy.Capacity,
            };
        }
        return new MetricStreamConfiguration { TagKeys = policy.Keys.ToArray(), CardinalityLimit = policy.Capacity };
    }

    private void Reserve(string name, MetricKind? kind, int capacity, IReadOnlyList<string> keys, IReadOnlyList<double> bounds)
    {
        StreamOverride? supplied = settings.Streams.GetValueOrDefault(name);
        capacity = supplied?.CardinalityLimit ?? capacity;
        bounds = supplied?.HistogramBounds ?? bounds;
        if (kind is not null and not MetricKind.Histogram and not MetricKind.Timer && supplied?.HistogramBounds is not null)
        { throw new ArgumentException("A non-histogram stream cannot accept histogram boundaries."); }
        if (policies.Count >= settings.MaxStreams || (long)capacities + capacity > settings.TotalCardinalityLimit)
        { throw new ArgumentException("The known Metrics catalog exceeds its stream or total dimension capacity."); }
        policies.Add(name, new(name, kind, capacity, keys, bounds));
        capacities += capacity;
    }

    private sealed record StreamPolicy(string Name, MetricKind? Kind, int Capacity, IReadOnlyList<string> Keys, IReadOnlyList<double> Bounds);
    private sealed record ModuleProfile(MetricKind Kind, MetricUnit Unit, IReadOnlyList<string> Keys);
}

internal sealed record MetricsStreamSnapshot(string Name, int CardinalityLimit, IReadOnlyList<string> DimensionKeys,
    IReadOnlyList<double> HistogramBounds, bool Selected);
