using System.Text;
using CIS.Text;

namespace CIS.Metrics.OpenTelemetry;

internal sealed class MetricsSettings
{
    internal MetricsSettings(MetricsOptions options)
    {
        Enabled = options.Enabled;
        CollectCoreModules = options.CollectCoreModules;
        CollectIncomingHttp = options.CollectIncomingHttp;
        OutgoingHttpSelection = options.CollectOutgoingHttp;
        CollectOutgoingHttp = options.CollectOutgoingHttp == true;
        MaxDefinitions = Positive(options.MaxDefinitions);
        MaxStreams = Positive(options.MaxStreams);
        CardinalityLimit = Positive(options.CardinalityLimit);
        HistogramCardinalityLimit = Positive(options.HistogramCardinalityLimit);
        HttpDurationCardinalityLimit = Positive(options.HttpDurationCardinalityLimit);
        TotalCardinalityLimit = Positive(options.TotalCardinalityLimit);
        MaxScrapeResponseSizeBytes = Positive(options.MaxScrapeResponseSizeBytes);
        if (MaxScrapeResponseSizeBytes < 85_000)
        { throw new ArgumentException("The pinned exporter requires a response cap of at least its 85,000-byte initial buffer.", nameof(options)); }
        MaxConcurrentScrapes = Positive(options.MaxConcurrentScrapes);
        ScrapeTimeoutMilliseconds = Milliseconds(options.ScrapeTimeout, allowZero: false);
        ScrapeCacheMilliseconds = Milliseconds(options.ScrapeCacheDuration, allowZero: true);
        if (MaxDefinitions > MaxStreams || MaxStreams > TotalCardinalityLimit || MaxStreams > (int.MaxValue - 2) / 5)
        { throw new ArgumentException("Definitions, streams and their finite capacity/name reservations must fit the total Metrics profile.", nameof(options)); }
        if (options.ResourceAttributes.Count > 16 || options.Streams.Count > MaxStreams)
        { throw new ArgumentException("Metrics resource attributes and stream overrides must fit the finite profile.", nameof(options)); }
        var resource = new Dictionary<string, string>(StringComparer.Ordinal);
        var translated = new HashSet<string>(StringComparer.Ordinal) { "application", "environment" };
        foreach ((string key, string value) in options.ResourceAttributes)
        {
            if (!Name(key, 64) || !Text(value, 1_024) || !translated.Add(Normalize(key)))
            { throw new ArgumentException("Supply bounded unique resource keys/values without identity or translated collisions.", nameof(options)); }
            resource.Add(key, value);
        }
        ResourceAttributes = resource;
        var streams = new Dictionary<string, StreamOverride>(StringComparer.Ordinal);
        foreach ((string name, MetricStreamOptions value) in options.Streams)
        {
            if (!Name(name, 128) || value is null)
            { throw new ArgumentException("Supply bounded stream names and nonnull overrides.", nameof(options)); }
            int? capacity = value.CardinalityLimit;
            if (capacity is <= 0) { throw new ArgumentException("Stream capacity must be positive.", nameof(options)); }
            double[]? bounds = null;
            if (value.HistogramBounds is { } supplied)
            {
                int count = supplied.Count;
                if (count is < 1 or > 32) { throw new ArgumentException("Histogram profiles accept one through 32 finite bounds.", nameof(options)); }
                bounds = new double[count];
                for (int index = 0; index < count; index++)
                {
                    double bound = supplied[index];
                    if (!double.IsFinite(bound) || bound < 0 || index != 0 && bound <= bounds[index - 1])
                    { throw new ArgumentException("Histogram bounds must be finite, nonnegative and increasing.", nameof(options)); }
                    bounds[index] = bound;
                }
            }
            streams.Add(name, new(capacity, bounds is null ? null : Array.AsReadOnly(bounds)));
        }
        Streams = streams;
        Application = Enabled ? EnvironmentOptions.ApplicationName : string.Empty;
        Environment = Enabled ? EnvironmentOptions.EnvironmentName : string.Empty;
    }

    internal bool Enabled { get; }
    internal bool CollectCoreModules { get; }
    internal bool CollectIncomingHttp { get; }
    internal bool? OutgoingHttpSelection { get; }
    internal bool CollectOutgoingHttp { get; }
    internal bool CollectRestClient { get; }
    internal int MaxDefinitions { get; }
    internal int MaxStreams { get; }
    internal int CardinalityLimit { get; }
    internal int HistogramCardinalityLimit { get; }
    internal int HttpDurationCardinalityLimit { get; }
    internal int TotalCardinalityLimit { get; }
    internal int MaxScrapeResponseSizeBytes { get; }
    internal int MaxConcurrentScrapes { get; }
    internal int ScrapeTimeoutMilliseconds { get; }
    internal int ScrapeCacheMilliseconds { get; }
    internal string Application { get; }
    internal string Environment { get; }
    internal IReadOnlyDictionary<string, string> ResourceAttributes { get; }
    internal IReadOnlyDictionary<string, StreamOverride> Streams { get; }

    internal bool Equivalent(MetricsSettings other) => Enabled == other.Enabled && CollectCoreModules == other.CollectCoreModules
        && CollectIncomingHttp == other.CollectIncomingHttp && OutgoingHttpSelection == other.OutgoingHttpSelection
        && MaxDefinitions == other.MaxDefinitions && MaxStreams == other.MaxStreams
        && CardinalityLimit == other.CardinalityLimit && HistogramCardinalityLimit == other.HistogramCardinalityLimit
        && HttpDurationCardinalityLimit == other.HttpDurationCardinalityLimit && TotalCardinalityLimit == other.TotalCardinalityLimit
        && MaxScrapeResponseSizeBytes == other.MaxScrapeResponseSizeBytes && MaxConcurrentScrapes == other.MaxConcurrentScrapes
        && ScrapeTimeoutMilliseconds == other.ScrapeTimeoutMilliseconds && ScrapeCacheMilliseconds == other.ScrapeCacheMilliseconds
        && ResourceAttributes.Count == other.ResourceAttributes.Count && ResourceAttributes.All(pair => other.ResourceAttributes.TryGetValue(pair.Key, out string? value) && pair.Value == value)
        && Streams.Count == other.Streams.Count && Streams.All(pair => other.Streams.TryGetValue(pair.Key, out StreamOverride? value)
            && pair.Value.CardinalityLimit == value.CardinalityLimit
            && (pair.Value.HistogramBounds ?? []).SequenceEqual(value.HistogramBounds ?? []));

    // Configured options stay immutable and comparable independently of registration order.
    // Only the final context derives composition before any provider/producer is activated.
    internal MetricsSettings ForComposition(bool hasRestClient) => new(this, hasRestClient);

    private MetricsSettings(MetricsSettings configured, bool hasRestClient)
    {
        Enabled = configured.Enabled; CollectCoreModules = configured.CollectCoreModules;
        CollectIncomingHttp = configured.CollectIncomingHttp; OutgoingHttpSelection = configured.OutgoingHttpSelection;
        CollectOutgoingHttp = OutgoingHttpSelection ?? hasRestClient;
        CollectRestClient = hasRestClient && CollectCoreModules;
        MaxDefinitions = configured.MaxDefinitions; MaxStreams = configured.MaxStreams;
        CardinalityLimit = configured.CardinalityLimit; HistogramCardinalityLimit = configured.HistogramCardinalityLimit;
        HttpDurationCardinalityLimit = configured.HttpDurationCardinalityLimit; TotalCardinalityLimit = configured.TotalCardinalityLimit;
        MaxScrapeResponseSizeBytes = configured.MaxScrapeResponseSizeBytes; MaxConcurrentScrapes = configured.MaxConcurrentScrapes;
        ScrapeTimeoutMilliseconds = configured.ScrapeTimeoutMilliseconds; ScrapeCacheMilliseconds = configured.ScrapeCacheMilliseconds;
        Application = configured.Application; Environment = configured.Environment;
        ResourceAttributes = configured.ResourceAttributes; Streams = configured.Streams;
    }

    private static int Positive(int value) => value > 0 ? value : throw new ArgumentException("Metrics limits must be positive and finite.");
    private static int Milliseconds(TimeSpan value, bool allowZero)
        => value.Ticks % TimeSpan.TicksPerMillisecond == 0 && value.TotalMilliseconds >= (allowZero ? 0 : 1) && value.TotalMilliseconds <= int.MaxValue
            ? (int)value.TotalMilliseconds : throw new ArgumentException("Use a finite whole-millisecond Metrics duration.");
    internal static bool Name(string? value, int maximum) => value is { Length: > 0 } && value.Length <= maximum
        && value[0] is >= 'a' and <= 'z' && value.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_');
    internal static bool Text(string? value, int maximum) => value is not null && value.Length <= maximum
        && UnicodeScalarText.IsValid(value, maximum, allowControls: true) && Encoding.UTF8.GetByteCount(value) <= maximum;
    internal static string Normalize(string value)
    {
        var result = new StringBuilder(value.Length);
        bool separator = false;
        foreach (char character in value)
        {
            bool next = character is '.' or '_';
            if (!next || !separator) { result.Append(next ? '_' : character); }
            separator = next;
        }
        return result.ToString();
    }
}

internal sealed record StreamOverride(int? CardinalityLimit, IReadOnlyList<double>? HistogramBounds);
