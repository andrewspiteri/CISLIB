using System.Reflection;
using System.Text.Json;
using CIS.TypeManagement;
using Microsoft.Extensions.DependencyInjection;

namespace CIS.Configuration;

internal sealed class ConfigurationCatalog
{
    private ConfigurationCatalog(ConfigurationEntry[] entries, ConfigurationSettings settings)
    {
        Entries = Array.AsReadOnly(entries);
        Reader = new ConfigurationReader(entries);
        Settings = settings;
    }

    internal IReadOnlyList<ConfigurationEntry> Entries
    {
        get;
    }
    internal IConfigurationReader Reader
    {
        get;
    }
    internal ConfigurationSettings Settings
    {
        get;
    }

    internal static ConfigurationCatalog Create(ITypeManager manager, ConfigurationOptions options)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ConfigurationSettings settings = ConfigurationSettings.Capture(options);
        return Create(manager.GetImplementations<IConfigurationObject>(), settings);
    }

    internal static ConfigurationCatalog Create(IReadOnlyList<Type> types, ConfigurationOptions options)
    {
        ArgumentNullException.ThrowIfNull(types);
        return Create(types, ConfigurationSettings.Capture(options));
    }

    private static ConfigurationCatalog Create(IReadOnlyList<Type> types, ConfigurationSettings settings)
    {
        var factories = settings.Factories;
        var validators = settings.Validators;
        var owner = new object();
        var identities = new HashSet<string>(StringComparer.Ordinal);
        var discovered = new HashSet<Type>();
        var definitions = new List<(Type Type, string Identity)>();
        foreach (Type type in types)
        {
            if (type is null)
            {
                throw ConfigurationDiagnostics.Error(typeof(ConfigurationCatalog), "Configuration.Shape", "The discovered configuration list contains a null type.");
            }
            if (!discovered.Add(type))
            {
                continue;
            }
            if (!type.IsClass || type.IsAbstract || type.ContainsGenericParameters || !typeof(IConfigurationObject).IsAssignableFrom(type))
            {
                throw ConfigurationDiagnostics.Error(type, "Configuration.Shape", "A configuration must be a concrete closed class implementing IConfigurationObject.");
            }
            string identity = ConfigurationIdentity.Create(type);
            if (!identities.Add(identity))
            {
                throw ConfigurationDiagnostics.Error(type, "Configuration.Identity", "Discovered types collide under the versionless storage identity.");
            }
            definitions.Add((type, identity));
        }

        if (settings.Enrollments.Keys.Any(type => !discovered.Contains(type)))
        {
            throw ConfigurationDiagnostics.Error(typeof(ConfigurationOptions), "Configuration.Enrollment", "Every centrally enrolled exact type must belong to the prepared discovery catalog.");
        }

        var entries = new List<ConfigurationEntry>();
        foreach ((Type type, string identity) in definitions)
        {
            bool custom = factories.TryGetValue(type, out Func<IConfigurationObject>? selected);
            Func<IConfigurationObject> factory;
            if (custom)
            {
                factory = selected!;
            }
            else
            {
                ConstructorInfo? constructor = type.GetConstructor(Type.EmptyTypes);
                if (constructor is null)
                {
                    throw ConfigurationDiagnostics.Error(type, "Configuration.Construction", "Provide a public parameterless constructor or an exact-type default factory.");
                }
                factory = () => (IConfigurationObject)constructor.Invoke(null);
            }
            IConfigurationObject instance;
            try
            {
                instance = factory();
            }
            catch (Exception)
            {
                throw ConfigurationDiagnostics.Error(type, custom ? "Configuration.Factory" : "Configuration.Construction", "Constructing configuration defaults failed; constructors and factories must be passive.");
            }
            if (instance is null || instance.GetType() != type)
            {
                throw ConfigurationDiagnostics.Error(type, "Configuration.Factory", "A default factory must return a nonnull object of the exact discovered type.");
            }
            try
            {
                ConfigurationEvents.ValidateAndClaim(instance, owner);
            }
            catch (CoreConfigurationException error) when (error.Issues.Any(issue => issue.Code == "Configuration.Ownership"))
            {
                throw ConfigurationDiagnostics.Error(type, "Configuration.Ownership", "A default factory returned an object owned by another preparation; return a fresh exact-type root.");
            }
            catch (Exception)
            {
                throw ConfigurationDiagnostics.Error(type, "Configuration.EventWiring", "Validating ConfigurationChanged accessors failed; use passive ConfigurationEvents forwarding accessors.");
            }
            settings.Enrollments.TryGetValue(type, out ConfigurationEnrollment? enrollment);
            ConfigurationShape shape = ConfigurationShape.Create(type, factory, instance,
                enrollment is null ? null : settings.ByteBudget);
            JsonElement defaults = shape.DeclaredDefaults;
            validators.TryGetValue(type, out Action<IConfigurationObject>? validator);
            entries.Add(new(type, instance, identity, factory, shape, defaults, validator)
            {
                Enrollment = enrollment,
                SnapshotTarget = enrollment is null ? null : settings.SnapshotTarget,
                SessionEpoch = enrollment is null ? 0 : 1,
                ByteBudget = enrollment is null ? null : settings.ByteBudget
            });
        }
        return new(entries.ToArray(), settings);
    }

    internal void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Type[] reserved = Entries.Select(entry => entry.Type).Append(typeof(IConfigurationReader)).ToArray();
        foreach (ServiceDescriptor descriptor in services)
        {
            if (reserved.Contains(descriptor.ServiceType))
            {
                throw ConfigurationDiagnostics.Error(descriptor.ServiceType, "Configuration.Registration", "A retained configuration type or its synchronized reader is already registered, including keyed registrations.");
            }
        }
        foreach (ConfigurationEntry entry in Entries)
        {
            services.AddSingleton(entry.Type, entry.Instance);
        }
        services.AddSingleton(Reader);
    }
}

internal sealed class ConfigurationEntry(Type type, IConfigurationObject instance, string identity,
    Func<IConfigurationObject> factory, ConfigurationShape shape, JsonElement defaults, Action<IConfigurationObject>? validator)
{
    internal Type Type { get; } = type;
    internal IConfigurationObject Instance { get; } = instance;
    internal string Identity { get; } = identity;
    internal Func<IConfigurationObject> Factory { get; } = factory;
    internal object ValueGate { get; } = new();
    internal ConfigurationShape Shape { get; } = shape;
    internal JsonElement Defaults { get; } = defaults.Clone();
    internal Action<IConfigurationObject>? Validator { get; } = validator;
    internal string? AcceptedChecksum
    {
        get; set;
    }
    internal long Revision
    {
        get; set;
    }
    internal bool Disabled
    {
        get; set;
    }
    internal ConfigurationEnrollment? Enrollment
    {
        get; init;
    }
    internal ConfigurationSnapshotTarget? SnapshotTarget
    {
        get; set;
    }
    internal long SessionEpoch
    {
        get; set;
    }
    internal ConfigurationSnapshotAdmission? SnapshotAdmission
    {
        get; set;
    }
    internal long HighestGeneration
    {
        get; set;
    }
    internal ConfigurationSnapshotProvenance? ObservedProvenance
    {
        get; set;
    }
    internal ConfigurationDesiredIdentity? ObservedDesired
    {
        get; set;
    }
    internal ConfigurationAcceptanceStatus? Acceptance
    {
        get; set;
    }
    internal ConfigurationByteBudget? ByteBudget
    {
        get; init;
    }
}

internal sealed class ConfigurationReader(IEnumerable<ConfigurationEntry> entries) : IConfigurationReader
{
    private readonly Dictionary<IConfigurationObject, ConfigurationEntry> retained = entries
        .ToDictionary<ConfigurationEntry, IConfigurationObject, ConfigurationEntry>(
            entry => entry.Instance, entry => entry, ReferenceEqualityComparer.Instance);

    public TResult Read<TConfiguration, TResult>(TConfiguration configuration, Func<TConfiguration, TResult> reader)
        where TConfiguration : class, IConfigurationObject
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(reader);
        if (!retained.TryGetValue(configuration, out ConfigurationEntry? entry))
        {
            throw ConfigurationDiagnostics.Error(configuration.GetType(), "Configuration.Reader", "Read requires the exact retained configuration instance belonging to this preparation.");
        }
        lock (entry.ValueGate)
        {
            return reader(configuration);
        }
    }
}
