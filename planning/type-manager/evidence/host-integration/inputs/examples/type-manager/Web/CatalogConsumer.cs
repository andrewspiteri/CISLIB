using CIS.TypeManagement;

namespace CIS.TypeManagement.Examples.Web;

// This scoped consumer receives an already completed catalog. Its constructor
// cannot initiate discovery, and discovered application objects are not registered.
public sealed class CatalogConsumer(ITypeManager manager)
{
    public Guid InstanceId { get; } = Guid.NewGuid();
    public ITypeManager Manager { get; } = manager;
}
