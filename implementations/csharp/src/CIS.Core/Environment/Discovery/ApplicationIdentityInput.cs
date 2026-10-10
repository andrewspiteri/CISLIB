using System.Reflection;

namespace CIS.Configuration;

internal sealed class ApplicationIdentityInput
{
    public Assembly? ApplicationAssembly { get; init; }
    public string? ApplicationName { get; init; }
    public string? BuildVersion { get; init; }
    public string? CommitId { get; init; }
    public string? BuildId { get; init; }
}
