using CIS.TypeManagement.Fixtures.Transitive;

namespace CIS.TypeManagement.Fixtures.Unused;

internal sealed class DiscoveredSettings : BaseSettings
{
    private DiscoveredSettings() => throw new InvalidOperationException("Discovery must not construct instances.");
}

public sealed class StringHandler : HandlerBase<string>;

public sealed class GenericHandler<T> : HandlerBase<T>;
