namespace CIS.Configuration;

// Local validated state for the later public snapshot builder; no publication or I/O.
internal sealed class ValidatedConfiguration(ConfigurationResolution selected, InitializationWorkingDirectory directory, string? filePath)
{
    public ConfigurationResolution Selected { get; } = selected;
    public string WorkingDirectory { get; } = directory.Value;
    public string? FilePath { get; } = filePath;
}
