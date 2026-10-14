using CIS.TypeManagement.Fixtures.Contracts;

namespace CIS.TypeManagement.Fixtures.Transitive;

public abstract class BaseSettings : IDiscoveryMarker;

public abstract class HandlerBase<T> : IHandler<T>;
