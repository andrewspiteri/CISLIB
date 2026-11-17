using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using System.Text;
using CIS.Text;

namespace CIS.Metrics;

// One host's thin BCL adapter. No aggregate values, event history or global provider are stored here.
internal sealed class MetricsRuntime : IMetrics, IAsyncDisposable
{
    private static readonly double[] DurationBounds = [0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1, 2.5, 5, 7.5, 10];
    private readonly object gate = new();
    private readonly MetricsRuntimeOptions options;
    private Action<string>? diagnostic;
    private readonly Func<MetricRegistration, bool>? admitDefinition;
    private readonly TimeProvider clock;
    private readonly NoopTimer noopTimer;
    private readonly Dictionary<string, Entry> definitions = new(StringComparer.Ordinal);
    private readonly HashSet<string> exportedNames = new(StringComparer.Ordinal)
    { "target_info", "otel_scope_info" };
    private readonly TaskCompletionSource drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool startupSealed, stopping, declarationsStarted;
    private int activeCallbacks, diagnostics;

    internal MetricsRuntime(Meter meter, MetricsRuntimeOptions? options = null, Action<string>? diagnostic = null,
        TimeProvider? clock = null, Func<MetricRegistration, bool>? admitDefinition = null)
    {
        ArgumentNullException.ThrowIfNull(meter);
        Meter = meter;
        this.options = (options ?? new()).Copy();
        this.diagnostic = diagnostic;
        this.clock = clock ?? TimeProvider.System;
        if (this.clock.TimestampFrequency <= 0) { throw new ArgumentException("Use a positive monotonic clock frequency.", nameof(clock)); }
        this.admitDefinition = admitDefinition;
        noopTimer = new(this.clock);
    }

    // The root IMeterFactory owns disposal of this borrowed meter.
    internal Meter Meter { get; }
    internal bool IsStopping { get { lock (gate) { return stopping; } } }
    internal IReadOnlyList<MetricRegistration> Definitions
    {
        get { lock (gate) { return Array.AsReadOnly(definitions.Values.Select(entry => entry.Registration).ToArray()); } }
    }
    internal IReadOnlyList<string> ReservedExportedNames
    {
        get { lock (gate) { return Array.AsReadOnly(exportedNames.Order(StringComparer.Ordinal).ToArray()); } }
    }

    // The integration reserves lazy native/exporter families and generated children before CIS declarations.
    // This stores finite names only; native stream profiles and resource accounting remain integration policy.
    internal void ReserveExportedNames(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        int count = names.Count;
        if (count < 0 || count > options.MaxReservedNames)
        { throw new ArgumentException("Native/exporter name reservations exceed the finite catalog.", nameof(names)); }
        var captured = new string[count];
        for (int index = 0; index < count; index++) { captured[index] = names[index]; }
        if (captured.Any(name => string.IsNullOrEmpty(name) || name.Length > 256
            || !name.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or ':')
            || char.IsAsciiDigit(name[0])))
        { throw new ArgumentException("Reserve bounded already-normalized exporter names.", nameof(names)); }
        lock (gate)
        {
            if (declarationsStarted || startupSealed || stopping)
            { throw new InvalidOperationException("Reserve native/exporter names before declaring host instruments."); }
            if (exportedNames.Count + captured.Distinct(StringComparer.Ordinal).Count(name => !exportedNames.Contains(name)) > options.MaxReservedNames)
            { throw new InvalidOperationException("Native/exporter name reservations exceed the finite catalog."); }
            foreach (string name in captured) { exportedNames.Add(name); }
        }
    }

    internal bool TryGetRegistration(Instrument instrument, [NotNullWhen(true)] out MetricRegistration? registration)
    {
        lock (gate)
        {
            if (ReferenceEquals(instrument.Meter, Meter) && definitions.TryGetValue(instrument.Name, out Entry? entry)
                && (ReferenceEquals(entry.Registration.Instrument, instrument)
                    || entry.Registration.Instrument is null && MatchesInstrument(entry.Registration, instrument)))
            {
                registration = entry.Registration;
                return true;
            }
        }
        registration = null;
        return false;
    }

    internal void SealStartup() { lock (gate) { startupSealed = true; } }

    public ICounter CreateCounter(MetricDefinition definition)
        => (ICounter?)Register(definition, MetricKind.Counter)?.Handle ?? NoopMetrics.Instance;
    public IUpDownCounter CreateUpDownCounter(MetricDefinition definition)
        => (IUpDownCounter?)Register(definition, MetricKind.UpDownCounter)?.Handle ?? NoopMetrics.Instance;
    public IGauge CreateGauge(MetricDefinition definition)
        => (IGauge?)Register(definition, MetricKind.Gauge)?.Handle ?? NoopMetrics.Instance;
    public IHistogram CreateHistogram(MetricDefinition definition)
        => (IHistogram?)Register(definition, MetricKind.Histogram)?.Handle ?? NoopMetrics.Instance;
    public ITimer CreateTimer(MetricDefinition definition)
        => (ITimer?)Register(definition, MetricKind.Timer)?.Handle ?? noopTimer;
    public void RegisterObservableGauge(MetricDefinition definition, Func<IReadOnlyList<MetricObservation>> observe)
    {
        ArgumentNullException.ThrowIfNull(observe);
        _ = Register(definition, MetricKind.ObservableGauge, observe);
    }

    // Stop controls callback owner access. HTTP cancellation is deliberately not used as callback settlement.
    internal void BeginStopping()
    {
        lock (gate)
        {
            stopping = true;
            if (activeCallbacks == 0) { CompleteStopLocked(); }
        }
    }

    internal Task StopAsync()
    {
        BeginStopping();
        return drained.Task;
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    private Entry? Register(MetricDefinition definition, MetricKind kind, Func<IReadOnlyList<MetricObservation>>? observe = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        MetricDefinition captured = Validate(definition, kind, out IReadOnlyList<double> bounds);
        string exported = ExportedName(captured.Name, captured.Unit);
        lock (gate)
        {
            declarationsStarted = true;
            if (stopping) { return null; }
            if (definitions.TryGetValue(captured.Name, out Entry? existing))
            {
                MetricRegistration current = existing.Registration;
                if (current.Kind != kind || current.Definition.Unit != captured.Unit
                    || current.Definition.Description != captured.Description
                    || !current.Definition.AllowedDimensionKeys.SequenceEqual(captured.AllowedDimensionKeys)
                    || !current.HistogramBounds.SequenceEqual(bounds) || !Equals(existing.Observe, observe))
                { throw new InvalidOperationException("A metric name must retain one kind, unit, description, dimension schema, profile and observable callback."); }
                return existing;
            }
            string[] names = ReservedNames(exported, kind);
            if (names.Any(exportedNames.Contains))
            { throw new InvalidOperationException("A metric conflicts with a translated metric family, generated child or exporter-reserved name."); }
            var registration = new MetricRegistration(captured, kind, exported, bounds);
            if (definitions.Count >= options.MaxDefinitions || exportedNames.Count + names.Length > options.MaxReservedNames
                || admitDefinition is not null && !admitDefinition(registration))
            {
                if (!startupSealed) { throw new InvalidOperationException("The known Metrics catalog exceeds its configured finite capacity."); }
                Diagnose(MetricsDiagnostic.Capacity);
                return null;
            }
            var entry = new Entry(registration, observe);
            // Insert metadata before CreateInstrument synchronously invokes the SDK view selector.
            definitions.Add(captured.Name, entry);
            foreach (string name in names) { exportedNames.Add(name); }
            try
            {
                string unit = UnitText(captured.Unit);
                switch (kind)
                {
                    case MetricKind.Counter:
                        var counter = Meter.CreateCounter<long>(captured.Name, unit, captured.Description);
                        registration.Instrument = counter;
                        entry.Handle = new CounterHandle(this, entry, counter);
                        break;
                    case MetricKind.UpDownCounter:
                        var upDown = Meter.CreateUpDownCounter<long>(captured.Name, unit, captured.Description);
                        registration.Instrument = upDown;
                        entry.Handle = new UpDownHandle(this, entry, upDown);
                        break;
                    case MetricKind.Gauge:
                        var gauge = Meter.CreateGauge<double>(captured.Name, unit, captured.Description);
                        registration.Instrument = gauge;
                        entry.Handle = new GaugeHandle(this, entry, gauge);
                        break;
                    case MetricKind.Histogram:
                    case MetricKind.Timer:
                        var histogram = Meter.CreateHistogram<double>(captured.Name, unit, captured.Description,
                            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = bounds });
                        registration.Instrument = histogram;
                        entry.Handle = new HistogramHandle(this, entry, histogram, clock);
                        break;
                    case MetricKind.ObservableGauge:
                        registration.Instrument = Meter.CreateObservableGauge<double>(captured.Name,
                            () => Observe(entry), unit, captured.Description);
                        break;
                    default: throw new ArgumentOutOfRangeException(nameof(kind));
                }
                return entry;
            }
            catch
            {
                definitions.Remove(captured.Name);
                foreach (string name in names) { exportedNames.Remove(name); }
                throw;
            }
        }
    }

    private IEnumerable<Measurement<double>> Observe(Entry entry)
    {
        lock (gate)
        {
            if (stopping) { return []; }
            activeCallbacks++;
        }
        try
        {
            IReadOnlyList<MetricObservation>? observations = entry.Observe!();
            int count = observations?.Count ?? -1;
            if (count < 0 || count > options.MaxObservableMeasurements)
            { Diagnose(MetricsDiagnostic.InvalidMeasurement); return []; }
            // Materialize while admitted: neither lazy/user indexers nor user Count run after release.
            var results = new List<Measurement<double>>(count);
            for (int index = 0; index < count; index++)
            {
                MetricObservation observation = observations![index];
                if (!double.IsFinite(observation.Value) || !TryTags(entry, observation.Dimensions, out var tags))
                { Diagnose(MetricsDiagnostic.InvalidMeasurement); continue; }
                results.Add(new(observation.Value, tags));
            }
            return results;
        }
        catch (Exception error) when (IsOrdinary(error))
        {
            Diagnose(MetricsDiagnostic.Callback);
            return [];
        }
        finally
        {
            lock (gate)
            {
                activeCallbacks--;
                if (stopping && activeCallbacks == 0) { CompleteStopLocked(); }
            }
        }
    }

    private void CompleteStopLocked()
    {
        foreach (Entry entry in definitions.Values) { entry.Observe = null; }
        // Borrowed module handles can outlive the host. Release its diagnostic owner
        // together with the actual callback owners before publishing settlement.
        Volatile.Write(ref diagnostic, null);
        drained.TrySetResult();
    }

    private MetricDefinition Validate(MetricDefinition definition, MetricKind kind, out IReadOnlyList<double> bounds)
    {
        if (!ValidName(definition.Name, options.MaxNameLength) || !Enum.IsDefined(definition.Unit)
            || !ValidText(definition.Description, options.MaxDescriptionBytes)
            || definition.AllowedDimensionKeys.Count > options.MaxDimensionKeys)
        { throw new ArgumentException("Supply a bounded lowercase ASCII metric name, finite unit, description and dimension schema.", nameof(definition)); }
        if (Translate(definition.Name).EndsWith("_total", StringComparison.Ordinal))
        { throw new ArgumentException("Declare logical quantities without the generated counter total suffix.", nameof(definition)); }
        string[] keys = definition.AllowedDimensionKeys.Order(StringComparer.Ordinal).ToArray();
        var translated = new HashSet<string>(StringComparer.Ordinal);
        foreach (string key in keys)
        {
            if (!ValidName(key, options.MaxKeyLength) || ReservedKey(Translate(key)) || !translated.Add(Translate(key)))
            { throw new ArgumentException("Supply unique nonreserved dimension keys, including after exporter translation.", nameof(definition)); }
        }
        if (kind == MetricKind.Timer && definition.Unit != MetricUnit.Seconds)
        { throw new ArgumentException("A timer measures seconds.", nameof(definition)); }
        bool histogram = kind is MetricKind.Histogram or MetricKind.Timer;
        double[] selected = definition.HistogramBounds?.ToArray()
            ?? (histogram && definition.Unit == MetricUnit.Seconds ? DurationBounds.ToArray() : []);
        if (!histogram && definition.HistogramBounds is not null || histogram && (selected.Length == 0 || selected.Length > options.MaxHistogramBounds))
        { throw new ArgumentException("Declare a finite histogram profile for its quantity; other instrument kinds do not accept bounds.", nameof(definition)); }
        for (int index = 0; index < selected.Length; index++)
        {
            if (!double.IsFinite(selected[index]) || selected[index] < 0 || index != 0 && selected[index] <= selected[index - 1])
            { throw new ArgumentException("Histogram bounds must be finite, nonnegative and strictly increasing.", nameof(definition)); }
        }
        bounds = Array.AsReadOnly(selected);
        return new(definition.Name, definition.Unit, definition.Description, keys, definition.HistogramBounds);
    }

    private bool TryTags(Entry entry, MetricDimensions dimensions, out KeyValuePair<string, object?>[] tags)
    {
        ReadOnlySpan<KeyValuePair<string, string>> pairs = dimensions.Pairs;
        if (dimensions.IsInvalid || pairs.Length > options.MaxDimensionKeys) { tags = []; return false; }
        tags = new KeyValuePair<string, object?>[pairs.Length];
        for (int index = 0; index < pairs.Length; index++)
        {
            KeyValuePair<string, string> pair = pairs[index];
            if (pair.Key is null || !entry.Registration.Definition.AllowedDimensionKeys.Contains(pair.Key, StringComparer.Ordinal)
                || !ValidText(pair.Value, options.MaxDimensionValueBytes)) { return false; }
            for (int previous = 0; previous < index; previous++)
            { if (pairs[previous].Key == pair.Key) { return false; } }
            tags[index] = new(pair.Key, pair.Value);
        }
        return true;
    }

    internal void Diagnose(MetricsDiagnostic category)
    {
        int mask = 1 << (int)category;
        if ((Interlocked.Or(ref diagnostics, mask) & mask) != 0) { return; }
        try { Volatile.Read(ref diagnostic)?.Invoke(category.ToString()); }
        catch (Exception error) when (IsOrdinary(error)) { /* Diagnostics never replace the observed business result. */ }
    }

    private static bool ValidName(string? value, int maximum)
        => value is { Length: > 0 } && value.Length <= maximum && value[0] is >= 'a' and <= 'z'
            && value.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_');
    private static bool ValidText(string? value, int maximumBytes)
        => value is not null && value.Length <= maximumBytes && UnicodeScalarText.IsValid(value, maximumBytes, allowControls: true)
            && Encoding.UTF8.GetByteCount(value) <= maximumBytes;
    private static bool ReservedKey(string key)
        => key is "application" or "environment" or "otel_metric_overflow" or "le" or "quantile"
            or "otel_scope_name" or "otel_scope_version" or "otel_scope_schema_url";
    internal static string UnitText(MetricUnit unit) => unit switch
    { MetricUnit.Count => "", MetricUnit.Seconds => "s", MetricUnit.Bytes => "By", MetricUnit.Ratio => "1", _ => throw new ArgumentOutOfRangeException(nameof(unit)) };
    internal static string ExportedName(string name, MetricUnit unit)
    {
        string translated = Translate(name);
        string suffix = unit switch { MetricUnit.Seconds => "seconds", MetricUnit.Bytes => "bytes", _ => "" };
        return suffix.Length != 0 && !translated.EndsWith(suffix, StringComparison.Ordinal) ? translated + "_" + suffix : translated;
    }
    private static string Translate(string name)
    {
        var result = new StringBuilder(name.Length);
        bool underscore = false;
        foreach (char character in name)
        {
            bool separator = character is '.' or '_';
            if (!separator || !underscore) { result.Append(separator ? '_' : character); }
            underscore = separator;
        }
        return result.ToString();
    }
    private static string[] ReservedNames(string name, MetricKind kind) => kind switch
    {
        MetricKind.Counter => [name, name + "_total", name + "_created"],
        MetricKind.Histogram or MetricKind.Timer => [name, name + "_bucket", name + "_sum", name + "_count", name + "_created"],
        _ => [name],
    };
    private static bool MatchesInstrument(MetricRegistration registration, Instrument instrument)
        => instrument.Unit == UnitText(registration.Definition.Unit) && instrument.Description == registration.Definition.Description
            && (registration.Kind switch
            {
                MetricKind.Counter => instrument is Counter<long>,
                MetricKind.UpDownCounter => instrument is UpDownCounter<long>,
                MetricKind.Gauge => instrument is Gauge<double>,
                MetricKind.Histogram or MetricKind.Timer => instrument is Histogram<double>,
                MetricKind.ObservableGauge => instrument is ObservableGauge<double>,
                _ => false,
            });
    internal static bool IsOrdinary(Exception error) => error is not OutOfMemoryException and not StackOverflowException and not AccessViolationException;

    private sealed class Entry(MetricRegistration registration, Func<IReadOnlyList<MetricObservation>>? observe)
    {
        internal MetricRegistration Registration { get; } = registration;
        internal Func<IReadOnlyList<MetricObservation>>? Observe { get; set; } = observe;
        internal object? Handle { get; set; }
    }

    private sealed class CounterHandle(MetricsRuntime runtime, Entry entry, Counter<long> instrument) : ICounter
    {
        public void Add(long amount = 1, MetricDimensions dimensions = default)
        {
            if (!instrument.Enabled) { return; }
            if (amount < 0 || !runtime.TryTags(entry, dimensions, out var tags)) { runtime.Diagnose(MetricsDiagnostic.InvalidMeasurement); return; }
            try { instrument.Add(amount, tags); }
            catch (Exception error) when (IsOrdinary(error)) { runtime.Diagnose(MetricsDiagnostic.Backend); }
        }
    }
    private sealed class UpDownHandle(MetricsRuntime runtime, Entry entry, UpDownCounter<long> instrument) : IUpDownCounter
    {
        public void Add(long delta, MetricDimensions dimensions = default)
        {
            if (!instrument.Enabled) { return; }
            if (!runtime.TryTags(entry, dimensions, out var tags)) { runtime.Diagnose(MetricsDiagnostic.InvalidMeasurement); return; }
            try { instrument.Add(delta, tags); }
            catch (Exception error) when (IsOrdinary(error)) { runtime.Diagnose(MetricsDiagnostic.Backend); }
        }
    }
    private sealed class GaugeHandle(MetricsRuntime runtime, Entry entry, Gauge<double> instrument) : IGauge
    {
        public void Set(double value, MetricDimensions dimensions = default)
        {
            if (!instrument.Enabled) { return; }
            if (!double.IsFinite(value) || !runtime.TryTags(entry, dimensions, out var tags)) { runtime.Diagnose(MetricsDiagnostic.InvalidMeasurement); return; }
            try { instrument.Record(value, tags); }
            catch (Exception error) when (IsOrdinary(error)) { runtime.Diagnose(MetricsDiagnostic.Backend); }
        }
    }
    private sealed class HistogramHandle(MetricsRuntime runtime, Entry entry, Histogram<double> instrument, TimeProvider clock) : IHistogram, ITimer
    {
        public void Record(double value, MetricDimensions dimensions = default)
        {
            if (!instrument.Enabled) { return; }
            if (!double.IsFinite(value) || value < 0 || !runtime.TryTags(entry, dimensions, out var tags)) { runtime.Diagnose(MetricsDiagnostic.InvalidMeasurement); return; }
            try { instrument.Record(value, tags); }
            catch (Exception error) when (IsOrdinary(error)) { runtime.Diagnose(MetricsDiagnostic.Backend); }
        }
        public ITimingScope Start(MetricDimensions dimensions = default)
        {
            if (!instrument.Enabled) { return new TimingScope(clock, static (_, _) => { }, default); }
            if (!runtime.TryTags(entry, dimensions, out _))
            {
                runtime.Diagnose(MetricsDiagnostic.InvalidMeasurement);
                return new TimingScope(clock, static (_, _) => { }, default);
            }
            return new TimingScope(clock, Record, dimensions);
        }
    }
}

internal enum MetricsDiagnostic { InvalidMeasurement, Capacity, Callback, Backend }
