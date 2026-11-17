namespace CIS.Configuration;

/// <summary>A retained data object whose event uses ConfigurationEvents accessors or ConfigurationObject.</summary>
public interface IConfigurationObject
{
    /// <summary>Reports an accepted effective change after the value lock has been released.</summary>
    event EventHandler<ConfigurationChangedEventArgs>? ConfigurationChanged;
}

/// <summary>Identifies one accepted local change without retaining configuration values.</summary>
public sealed class ConfigurationChangedEventArgs : EventArgs
{
    /// <summary>Creates arguments for a positive local revision.</summary>
    public ConfigurationChangedEventArgs(Type configurationType, long revision)
    {
        ArgumentNullException.ThrowIfNull(configurationType);
        ArgumentOutOfRangeException.ThrowIfLessThan(revision, 1);
        ConfigurationType = configurationType;
        Revision = revision;
    }

    /// <summary>Gets the exact configuration CLR type.</summary>
    public Type ConfigurationType { get; }
    /// <summary>Gets the host-local accepted revision; it is not a store version.</summary>
    public long Revision { get; }
}

/// <summary>Coordinates a short synchronous read of one exact retained object.</summary>
public interface IConfigurationReader
{
    /// <summary>Reads under the object's private value gate. Copy mutable results before returning.</summary>
    /// <remarks>The callback must not mutate data, perform I/O, await, or acquire another configuration's gate.</remarks>
    TResult Read<TConfiguration, TResult>(TConfiguration configuration, Func<TConfiguration, TResult> reader)
        where TConfiguration : class, IConfigurationObject;
}
