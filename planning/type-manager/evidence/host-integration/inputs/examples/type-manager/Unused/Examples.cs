namespace CIS.TypeManagement.Examples.Unused;

// No host references these classes directly. Discovery includes inherited and
// nonpublic implementations without requiring usable constructors.
internal sealed class InheritedExample : DiscoveryExampleBase
{
    private InheritedExample() => throw new InvalidOperationException("Discovery must not construct this example.");
}

public sealed class DirectExample : IDiscoveryExample
{
    private DirectExample() => throw new InvalidOperationException("Discovery must not construct this example.");
}
