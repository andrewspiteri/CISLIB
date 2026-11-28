using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CIS.RuleEngine;

namespace CIS.Configuration;

internal sealed class ConfigurationValuePipeline(Action<Type, string>? diagnostic = null) : IConfigurationValuePipeline, IConfigurationSnapshotValuePipeline
{
    public ConfigurationPreparedValues Prepare(ConfigurationEntry entry, JsonElement? common, JsonElement? application)
    {
        ArgumentNullException.ThrowIfNull(entry);
        try
        {
            return PrepareCore(entry, common, application);
        }
        catch (CoreConfigurationException) { throw; }
        catch (Exception) { throw Error(entry, "Configuration.Mapping", "The provided record could not be mapped as supported passive configuration data."); }
    }

    private ConfigurationPreparedValues PrepareCore(ConfigurationEntry entry, JsonElement? common, JsonElement? application)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ConfigurationByteBudget.Reservation? ownership = null;
        try
        {
            if (entry.ByteBudget is { } budget)
            {
                long input = ConfigurationCentralLimits.MeasureJson(entry.Defaults);
                if (common is { } commonSource)
                {
                    input += ConfigurationCentralLimits.MeasureJson(commonSource, source: true);
                }
                if (application is { } applicationSource)
                {
                    input += ConfigurationCentralLimits.MeasureJson(applicationSource, source: true);
                }
                ownership = budget.Reserve(checked(input * 3));
            }
            ConfigurationPreparedValues prepared = PrepareValues(entry, common, application, ownership);
            ownership = null;
            return prepared;
        }
        finally
        {
            ownership?.Dispose();
        }
    }

    private ConfigurationPreparedValues PrepareValues(ConfigurationEntry entry, JsonElement? common, JsonElement? application,
        ConfigurationByteBudget.Reservation? ownership)
    {
        var preparation = new Preparation(entry, diagnostic, ownership);
        ConfigurationMappedValue effective = preparation.Read(entry.Shape, entry.Defaults, null, allowsNull: false, source: false);
        if (common is { } commonRecord)
        {
            effective = preparation.Merge(effective, preparation.ReadRecord(commonRecord));
        }
        if (application is { } applicationRecord)
        {
            effective = preparation.Merge(effective, preparation.ReadRecord(applicationRecord));
        }
        if (ownership is not null)
        {
            ownership.Resize(checked(ConfigurationCentralLimits.MeasureCompleted(effective) * 3));
        }
        effective = preparation.Complete(effective, null, allowsNull: false);
        byte[] canonical = ConfigurationCanonicalValues.Write(effective);
        IConfigurationObject candidate = CreateCandidate(entry);
        IReadOnlyList<ConfigurationAssignment> candidateValues = preparation.Assignments(effective);
        try
        {
            foreach (ConfigurationAssignment assignment in candidateValues)
            {
                assignment.Property.SetValue(candidate, assignment.Value);
            }
        }
        catch (Exception) { throw Error(entry, "Configuration.Mapping", "A detached candidate setter failed; configuration properties must be passive and nonthrowing."); }
        PreparedRuleDefinition? preparedRule = null;
        try
        {
            entry.Validator?.Invoke(candidate);
            if (entry.Enrollment is not null && entry.Instance is RuleEngineConfiguration rules)
            {
                preparedRule = rules.PrepareSnapshot(common, application);
            }
        }
        catch (Exception) { throw Error(entry, "Configuration.Validation", "The complete detached configuration candidate failed application validation."); }

        // Validators inspect data; retaining/mutating their candidate must not give
        // them a reference to the replacements eventually handed to the live root.
        JsonElement validated;
        try
        {
            if (ownership is not null)
            {
                ownership.Resize(checked(ConfigurationCentralLimits.MeasureDefault(entry.Shape, candidate) * 3));
            }
            validated = ConfigurationDefaults.Capture(entry.Shape, candidate);
        }
        catch (Exception) { throw Error(entry, "Configuration.Validation", "The validator left an unsupported candidate shape."); }
        ConfigurationMappedValue afterValidation;
        try
        {
            afterValidation = preparation.Complete(preparation.Read(entry.Shape, validated, null, false, false), null, false);
        }
        catch (Exception) { throw Error(entry, "Configuration.Validation", "The validator left invalid mapped values."); }
        if (!canonical.AsSpan().SequenceEqual(ConfigurationCanonicalValues.Write(afterValidation)))
        {
            throw Error(entry, "Configuration.Validation", "Validators must inspect and reject candidates without changing their mapped values.");
        }
        string checksum = Convert.ToHexStringLower(SHA256.HashData(canonical));
        return new(checksum, preparation.Assignments(effective))
        {
            Ownership = ownership,
            RuleDefinition = preparedRule
        };
    }

    public ConfigurationSnapshotCommit ApplySnapshot(ConfigurationEntry entry, ConfigurationPreparedValues prepared,
        ConfigurationSnapshotProvenance provenance, long sessionEpoch, CancellationToken cancellationToken, bool initial)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(provenance);
        lock (entry.ValueGate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.SnapshotAdmission?.IsClosed == true || entry.SessionEpoch != sessionEpoch
                || entry.SnapshotTarget != provenance.Desired.Target)
            {
                throw ConfigurationCentralLimits.Error("Configuration.SessionChanged", "A superseded target/session cannot accept configuration.");
            }
            if (entry.ObservedProvenance != provenance || entry.ObservedDesired != provenance.Desired)
            {
                throw ConfigurationCentralLimits.Error("Configuration.Superseded", "A superseded desired candidate cannot be accepted.");
            }
            if (entry.Disabled)
            {
                throw Error(entry, "Configuration.Disabled", "A broken live-setter contract has disabled central updates.");
            }
            bool sameValues = entry.AcceptedChecksum == prepared.Checksum;
            ConfigurationSnapshotProvenance? accepted = entry.Acceptance?.LastAccepted;
            if (!initial && entry.Enrollment!.UpdateMode == ConfigurationUpdateMode.RestartRequired && !sameValues)
            {
                var pending = new ConfigurationAcceptanceStatus(ConfigurationAcceptanceOutcome.PendingRestart,
                    provenance.Desired, accepted, entry.Revision, accepted is not null && !entry.Disabled);
                entry.Acceptance = pending;
                return new(false, pending);
            }
            // The outer gate fences immutable session facts. The existing commit
            // remains the one setter/checksum/revision owner; no provider callback
            // or observer is invoked while admitting the candidate.
            bool changed = Apply(entry, prepared, cancellationToken, static () => false, initial);
            var acceptance = new ConfigurationAcceptanceStatus(initial || changed
                ? ConfigurationAcceptanceOutcome.Applied : ConfigurationAcceptanceOutcome.AppliedUnchanged,
                provenance.Desired, provenance, entry.Revision, true,
                notificationFailureCode: changed ? null : entry.Acceptance?.NotificationFailureCode);
            entry.AcceptedRuleDefinition = prepared.RuleDefinition;
            entry.Acceptance = acceptance;
            return new(changed, acceptance);
        }
    }

    public bool Apply(ConfigurationEntry entry, ConfigurationPreparedValues prepared, CancellationToken cancellationToken,
        Func<bool> stopping, bool initial = false)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(stopping);
        lock (entry.ValueGate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (stopping())
            {
                throw new OperationCanceledException(new CancellationToken(canceled: true));
            }
            if (entry.Disabled)
            {
                throw Error(entry, "Configuration.Disabled", "A broken live-setter contract has disabled further updates for this entry.");
            }
            if (entry.AcceptedChecksum == prepared.Checksum)
            {
                return false;
            }
            if (initial && entry.AcceptedChecksum is not null)
            {
                throw Error(entry, "Configuration.Commit", "An accepted initial baseline cannot be replaced by another initial commit.");
            }
            if (!initial && entry.Revision == long.MaxValue)
            {
                throw Error(entry, "Configuration.Revision", "The local accepted revision is exhausted; no further change can be committed.");
            }
            long nextRevision = initial ? 0 : entry.Revision + 1;
            try
            {
                // This is the admitted commit. Cancellation after admission cannot
                // certify partial settlement or suppress its later notification.
                foreach (ConfigurationAssignment assignment in prepared.Assignments)
                {
                    assignment.Property.SetValue(entry.Instance, assignment.Value);
                }
            }
            catch (Exception)
            {
                entry.Disabled = true;
                if (entry.SnapshotAdmission is { } admission)
                {
                    // Close shared admission at the actual broken-setter boundary,
                    // before any coordinator/observer continuation can run.
                    Volatile.Write(ref admission.Faulted, 1);
                }
                throw Error(entry, "Configuration.Commit", "A live configuration setter failed. Updates are disabled; partial assignments cannot be claimed rolled back.");
            }
            entry.AcceptedChecksum = prepared.Checksum;
            entry.Revision = nextRevision;
            return !initial;
        }
    }

    private static IConfigurationObject CreateCandidate(ConfigurationEntry entry)
    {
        IConfigurationObject candidate;
        try
        {
            candidate = entry.Factory();
        }
        catch (Exception) { throw Error(entry, "Configuration.Factory", "Constructing a fresh detached candidate failed."); }
        if (candidate is null || candidate.GetType() != entry.Type)
        {
            throw Error(entry, "Configuration.Factory", "A candidate factory must return a fresh object of the exact configuration type.");
        }
        try
        {
            ConfigurationEvents.ValidateAndClaim(candidate, new object());
        }
        catch (CoreConfigurationException error) when (error.Issues.Any(issue => issue.Code == "Configuration.Ownership"))
        {
            throw Error(entry, "Configuration.Ownership", "A candidate factory returned an already owned object; no candidate assignment was attempted.");
        }
        catch (Exception) { throw Error(entry, "Configuration.Factory", "A candidate must retain the supported passive ConfigurationChanged accessors."); }
        return candidate;
    }

    private static CoreConfigurationException Error(ConfigurationEntry entry, string code, string explanation)
        => ConfigurationDiagnostics.Error(entry.Type, code, explanation);

    private sealed class Preparation(ConfigurationEntry entry, Action<Type, string>? diagnostic,
        ConfigurationByteBudget.Reservation? ownership)
    {
        private int warningsRemaining = 8;
        private long mappedNodes;

        internal ConfigurationMappedValue ReadRecord(JsonElement record)
        {
            if (record.ValueKind != JsonValueKind.Object)
            {
                throw Invalid("A provided configuration record must be a JSON object.");
            }
            ValidateStructure(record);
            return Read(entry.Shape, record, null, false, source: true);
        }

        private void ValidateStructure(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonProperty property in value.EnumerateObject())
                {
                    if (!names.Add(property.Name))
                    {
                        throw Invalid("A supplied record contains a duplicate ordinal property name.");
                    }
                    ValidateStructure(property.Value);
                }
            }
            else if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in value.EnumerateArray())
                {
                    ValidateStructure(item);
                }
            }
        }

        internal ConfigurationMappedValue Read(ConfigurationShape shape, JsonElement json, NullabilityInfo? nullability,
            bool allowsNull, bool source)
        {
            if (ownership is not null)
            {
                if (++mappedNodes > 3 * 65_536)
                {
                    throw ConfigurationCentralLimits.Error("Configuration.Materialization", "Central intermediate mapping exceeds its bounded source/composition allowance.");
                }
                // Reserve node growth before a default expansion constructs it.
                ownership.EnsureAtLeast(checked(mappedNodes * 64));
            }
            if (json.ValueKind == JsonValueKind.Null)
            {
                if (source && !allowsNull)
                {
                    throw Invalid("A known nonnullable configuration property or collection element cannot be null.");
                }
                return new(shape, IsNull: true);
            }
            switch (shape.Kind)
            {
                case ConfigurationValueKind.Scalar:
                    return new(shape, Scalar: ConfigurationScalarValues.Read(shape.Type, json, entry.Type));
                case ConfigurationValueKind.Object:
                    if (json.ValueKind != JsonValueKind.Object)
                    {
                        throw Invalid("A known passive-object property requires a JSON object.");
                    }
                    var members = new Dictionary<string, ConfigurationMappedValue>(StringComparer.Ordinal);
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    foreach (JsonProperty supplied in json.EnumerateObject())
                    {
                        if (!seen.Add(supplied.Name))
                        {
                            throw Invalid("A configuration object contains a duplicate ordinal property name.");
                        }
                        ConfigurationProperty? property = shape.Properties.FirstOrDefault(item => item.Name == supplied.Name);
                        if (property is null)
                        {
                            if (source)
                            {
                                Warn(supplied.Name);
                            }
                            continue;
                        }
                        members.Add(property.Name, Read(property.Shape, supplied.Value, property.Nullability, property.AllowsNull, source));
                    }
                    return new(shape, Members: members);
                case ConfigurationValueKind.Sequence:
                    if (json.ValueKind != JsonValueKind.Array)
                    {
                        throw Invalid("A known sequence property requires a JSON array.");
                    }
                    NullabilityInfo? element = ElementNullability(shape, nullability);
                    bool elementAllowsNull = AllowsNull(shape.ElementShape!, element);
                    return new(shape, Items: json.EnumerateArray()
                        .Select(item => Read(shape.ElementShape!, item, element, elementAllowsNull, source)).ToArray());
                case ConfigurationValueKind.Dictionary:
                    if (json.ValueKind != JsonValueKind.Object)
                    {
                        throw Invalid("A known dictionary property requires a JSON object.");
                    }
                    NullabilityInfo? valueNullability = ElementNullability(shape, nullability);
                    var dictionary = new Dictionary<string, ConfigurationMappedValue>(StringComparer.Ordinal);
                    foreach (JsonProperty supplied in json.EnumerateObject())
                    {
                        if (dictionary.ContainsKey(supplied.Name))
                        {
                            throw Invalid("A dictionary contains a duplicate ordinal key.");
                        }
                        dictionary.Add(supplied.Name, Read(shape.ElementShape!, supplied.Value, valueNullability,
                            AllowsNull(shape.ElementShape!, valueNullability), source));
                    }
                    return new(shape, Members: dictionary);
                default:
                    throw Invalid("Unsupported configuration value shape.");
            }
        }

        internal ConfigurationMappedValue Merge(ConfigurationMappedValue lower, ConfigurationMappedValue upper)
        {
            if (upper.IsNull || upper.Shape.Kind != ConfigurationValueKind.Object)
            {
                return upper;
            }
            if (lower.IsNull)
            {
                lower = Read(upper.Shape, upper.Shape.DeclaredDefaults, null, true, false);
            }
            var members = new Dictionary<string, ConfigurationMappedValue>(lower.Members!, StringComparer.Ordinal);
            foreach ((string name, ConfigurationMappedValue value) in upper.Members!)
            {
                members[name] = members.TryGetValue(name, out ConfigurationMappedValue? previous) ? Merge(previous, value) : value;
            }
            return new(upper.Shape, Members: members);
        }

        internal ConfigurationMappedValue Complete(ConfigurationMappedValue value, NullabilityInfo? nullability, bool allowsNull)
        {
            if (value.IsNull)
            {
                if (!allowsNull)
                {
                    throw Invalid("The complete effective configuration contains a null nonnullable value.");
                }
                return value;
            }
            ConfigurationShape shape = value.Shape;
            switch (shape.Kind)
            {
                case ConfigurationValueKind.Object:
                    var members = new Dictionary<string, ConfigurationMappedValue>(StringComparer.Ordinal);
                    foreach (ConfigurationProperty property in shape.Properties)
                    {
                        if (!value.Members!.TryGetValue(property.Name, out ConfigurationMappedValue? member))
                        {
                            member = Read(property.Shape, shape.DeclaredDefaults.GetProperty(property.Name), property.Nullability, property.AllowsNull, false);
                        }
                        members.Add(property.Name, Complete(member, property.Nullability, property.AllowsNull));
                    }
                    return new(shape, Members: members);
                case ConfigurationValueKind.Sequence:
                    NullabilityInfo? element = ElementNullability(shape, nullability);
                    return new(shape, Items: value.Items!.Select(item => Complete(item, element, AllowsNull(shape.ElementShape!, element))).ToArray());
                case ConfigurationValueKind.Dictionary:
                    NullabilityInfo? itemNullability = ElementNullability(shape, nullability);
                    return new(shape, Members: value.Members!.ToDictionary(item => item.Key,
                        item => Complete(item.Value, itemNullability, AllowsNull(shape.ElementShape!, itemNullability)), StringComparer.Ordinal));
                default:
                    return value;
            }
        }

        internal IReadOnlyList<ConfigurationAssignment> Assignments(ConfigurationMappedValue value)
        {
            try
            {
                return Array.AsReadOnly(value.Shape.Properties.Select(property => new ConfigurationAssignment(
                    property.Property, Materialize(value.Members![property.Name]))).ToArray());
            }
            catch (Exception) { throw Invalid("Constructing detached replacement values failed; mapped classes and setters must be passive."); }
        }

        private static object? Materialize(ConfigurationMappedValue value)
        {
            if (value.IsNull)
            {
                return null;
            }
            ConfigurationShape shape = value.Shape;
            switch (shape.Kind)
            {
                case ConfigurationValueKind.Scalar:
                    return value.Scalar;
                case ConfigurationValueKind.Object:
                    object instance = shape.CreateInstance();
                    foreach (ConfigurationProperty property in shape.Properties)
                    {
                        property.Property.SetValue(instance, Materialize(value.Members![property.Name]));
                    }
                    return instance;
                case ConfigurationValueKind.Sequence:
                    IReadOnlyList<ConfigurationMappedValue> items = value.Items!;
                    if (shape.Type.IsArray)
                    {
                        Array array = Array.CreateInstance(shape.ElementShape!.Type, items.Count);
                        for (int index = 0; index < items.Count; index++)
                        {
                            array.SetValue(Materialize(items[index]), index);
                        }
                        return array;
                    }
                    var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(shape.ElementShape!.Type))!;
                    foreach (ConfigurationMappedValue item in items)
                    {
                        list.Add(Materialize(item));
                    }
                    return list;
                case ConfigurationValueKind.Dictionary:
                    var dictionary = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(string), shape.ElementShape!.Type))!;
                    foreach ((string key, ConfigurationMappedValue item) in value.Members!)
                    {
                        dictionary.Add(key, Materialize(item));
                    }
                    return dictionary;
                default:
                    throw new InvalidOperationException("Unsupported configuration shape.");
            }
        }

        private static NullabilityInfo? ElementNullability(ConfigurationShape shape, NullabilityInfo? information)
            => shape.Type.IsArray ? information?.ElementType
                : information?.GenericTypeArguments.ElementAtOrDefault(shape.Kind == ConfigurationValueKind.Dictionary ? 1 : 0);

        private static bool AllowsNull(ConfigurationShape shape, NullabilityInfo? information)
            => ConfigurationShape.AllowsNullValue(shape.Type, information?.WriteState ?? NullabilityState.Unknown);

        private void Warn(string name)
        {
            if (warningsRemaining-- <= 0 || diagnostic is null)
            {
                return;
            }
            string bounded = string.Concat(name.EnumerateRunes().Take(64).Select(rune => rune.ToString()));
            string safeName = JsonSerializer.Serialize(bounded);
            try
            {
                diagnostic(entry.Type, "Configuration.UnknownProperty: " + safeName);
            }
            catch (Exception) { /* Bounded observation must not change accepted data or skip validation. */ }
        }

        private CoreConfigurationException Invalid(string explanation) => Error(entry, "Configuration.Mapping", explanation);
    }
}

internal sealed record ConfigurationMappedValue(ConfigurationShape Shape, object? Scalar = null,
    IReadOnlyDictionary<string, ConfigurationMappedValue>? Members = null,
    IReadOnlyList<ConfigurationMappedValue>? Items = null, bool IsNull = false);
