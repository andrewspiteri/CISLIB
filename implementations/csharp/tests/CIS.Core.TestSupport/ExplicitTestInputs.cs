namespace CIS.TestSupport;

public sealed record TestMetadata(string ApplicationName, string BuildVersion, string EnvironmentName, string HostName, string RuntimeVersion);

/// <summary>Copies a supplied identity sequence; exhaustion never falls back to random or machine data.</summary>
public sealed class ExplicitTestInputs(TestMetadata metadata, IEnumerable<Guid> instanceIds)
{
    private readonly Queue<Guid> ids = new(instanceIds);
    private readonly object gate = new();

    public TestMetadata Metadata { get; } = metadata;

    public Guid NextInstanceId()
    {
        lock (gate)
        {
            if (!ids.TryDequeue(out Guid value)) { throw new InvalidOperationException("The explicit instance ID sequence is exhausted."); }
            return value;
        }
    }
}
