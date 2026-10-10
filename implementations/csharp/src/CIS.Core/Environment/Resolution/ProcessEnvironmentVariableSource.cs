namespace CIS.Configuration;

internal sealed class ProcessEnvironmentVariableSource : IEnvironmentVariableSource
{
    public static ProcessEnvironmentVariableSource Instance { get; } = new();

    private ProcessEnvironmentVariableSource()
    {
    }

    public string? GetValue(string name) => System.Environment.GetEnvironmentVariable(name);
}
