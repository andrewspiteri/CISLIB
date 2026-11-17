using global::OpenTelemetry.Exporter;

namespace CIS.Metrics.OpenTelemetry;

internal static class MetricsExporterPolicy
{
    private static bool IdentityKey(string key) => key is "application" or "environment";

    internal static void Configure(PrometheusAspNetCoreOptions exporter, MetricsSettings settings)
    {
        exporter.ScrapeEndpointPath = "/_metrics";
        exporter.ResourceConstantLabels = IdentityKey;
        exporter.TargetInfoEnabled = true;
        exporter.ScopeInfoEnabled = true;
        exporter.MaxScrapeResponseSizeBytes = settings.MaxScrapeResponseSizeBytes;
        exporter.ScrapeResponseTimeoutMilliseconds = settings.ScrapeTimeoutMilliseconds;
        exporter.ScrapeResponseCacheDurationMilliseconds = settings.ScrapeCacheMilliseconds;
        exporter.TranslationStrategy = PrometheusAspNetCoreTranslationStrategy.UnderscoreEscapingWithSuffixes;
    }

    internal static void Validate(PrometheusAspNetCoreOptions exporter, MetricsSettings settings)
    {
        if (exporter.ScrapeEndpointPath != "/_metrics" || !Equals(exporter.ResourceConstantLabels, (Func<string, bool>)IdentityKey)
            || !exporter.TargetInfoEnabled || !exporter.ScopeInfoEnabled
            || exporter.MaxScrapeResponseSizeBytes != settings.MaxScrapeResponseSizeBytes
            || exporter.ScrapeResponseTimeoutMilliseconds != settings.ScrapeTimeoutMilliseconds
            || exporter.ScrapeResponseCacheDurationMilliseconds != settings.ScrapeCacheMilliseconds
            || exporter.TranslationStrategy != PrometheusAspNetCoreTranslationStrategy.UnderscoreEscapingWithSuffixes)
        { throw new InvalidOperationException("The active Prometheus exporter must retain the exact approved CIS Metrics profile."); }
    }
}
