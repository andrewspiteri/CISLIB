namespace CIS.Metrics.Hosting;

// Optional container composition belongs to Metrics hosting, not the observed
// transport or policy. The alias exists only for enabled automatic collection.
internal static class MetricsHostAttachment
{
    internal static void Attach(object source, IServiceProvider services)
    {
        if (source is IMetricsSource observed && services.GetService(typeof(MetricsRuntime)) is MetricsRuntime runtime)
        { observed.AttachMetrics(runtime); }
    }
}
