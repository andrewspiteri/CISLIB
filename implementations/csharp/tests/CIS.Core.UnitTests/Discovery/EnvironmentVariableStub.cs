using CIS.Configuration;

namespace CIS.Tests.UnitTests.Discovery;

internal sealed class EnvironmentVariableStub : IEnvironmentVariableSource
{
    public Dictionary<string, string?> Values { get; } = new(StringComparer.Ordinal);
    public List<string> Reads { get; } = [];
    public string? FailingName { get; init; }
    public Exception? Failure { get; init; }

    public string? GetValue(string name)
    {
        Reads.Add(name);
        if (name == FailingName)
        {
            throw Failure ?? new IOException("Variable access unavailable");
        }

        return Values.GetValueOrDefault(name);
    }
}
