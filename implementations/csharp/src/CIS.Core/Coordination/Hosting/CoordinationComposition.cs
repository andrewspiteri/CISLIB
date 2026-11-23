namespace CIS.Coordination.Hosting;

// Passive metadata is available before Build, including when native host creation later fails.
internal sealed class CoordinationComposition
{
    internal object RegistrationIdentity { get; } = new();
    internal CoordinationHostLifetime Lifetime { get; } = new();
}
