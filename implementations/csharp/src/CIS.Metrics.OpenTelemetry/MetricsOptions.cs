namespace CIS.Metrics.OpenTelemetry;

/// <summary>A finite startup profile for the optional collector. Changing it requires restart.</summary>
public sealed class MetricsOptions
{
    /// <summary>Gets or sets whether this integration collects and exposes metrics.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Gets or sets automatic existing CIS module observations.</summary>
    public bool CollectCoreModules { get; set; } = true;
    /// <summary>Gets or sets native ASP.NET request-duration and active-request collection.</summary>
    public bool CollectIncomingHttp { get; set; } = true;
    /// <summary>Gets or sets the maximum admitted CIS/application definitions.</summary>
    public int MaxDefinitions { get; set; } = 96;
    /// <summary>Gets or sets the total collected streams, including reserved native streams.</summary>
    public int MaxStreams { get; set; } = 128;
    /// <summary>Gets or sets ordinary per-stream dimension capacity; SDK special points are additional.</summary>
    public int CardinalityLimit { get; set; } = 128;
    /// <summary>Gets or sets generic histogram dimension capacity.</summary>
    public int HistogramCardinalityLimit { get; set; } = 64;
    /// <summary>Gets or sets inbound duration dimension capacity.</summary>
    public int HttpDurationCardinalityLimit { get; set; } = 1_024;
    /// <summary>Gets or sets the sum of admitted configured dimension capacities.</summary>
    public int TotalCardinalityLimit { get; set; } = 16_384;
    /// <summary>Gets or sets the exporter response-size limit (at least 85,000 bytes), distinct from a managed-memory guarantee.</summary>
    public int MaxScrapeResponseSizeBytes { get; set; } = 4_194_304;
    /// <summary>Gets or sets the HTTP wait/write timeout; synchronous callbacks are not preempted.</summary>
    public TimeSpan ScrapeTimeout { get; set; } = TimeSpan.FromSeconds(5);
    /// <summary>Gets or sets the exporter cache interval.</summary>
    public TimeSpan ScrapeCacheDuration { get; set; } = TimeSpan.FromMilliseconds(300);
    /// <summary>Gets or sets reserved-branch concurrent requests, with no waiting queue.</summary>
    public int MaxConcurrentScrapes { get; set; } = 2;
    /// <summary>Gets finite optional string resource attributes. Identity keys cannot be overridden.</summary>
    public IDictionary<string, string> ResourceAttributes { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
    /// <summary>Gets explicit per-instrument capacity/histogram overrides, captured once at registration.</summary>
    public IDictionary<string, MetricStreamOptions> Streams { get; } = new Dictionary<string, MetricStreamOptions>(StringComparer.Ordinal);
}

/// <summary>An explicit finite stream override; it cannot change its instrument's meaning or unit.</summary>
public sealed class MetricStreamOptions
{
    /// <summary>Gets or sets a positive dimension capacity.</summary>
    public int? CardinalityLimit { get; set; }
    /// <summary>Gets or sets optional finite, increasing nonnegative histogram boundaries.</summary>
    public IReadOnlyList<double>? HistogramBounds { get; set; }
}
