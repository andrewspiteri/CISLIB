# Consuming a completed Type Manager catalog

The host prepares one `ITypeManager` before building its container. A consumer
receives that completed instance and queries actual runtime types:

```csharp
// IDiscoveryExample belongs only to the runnable example projects.
IReadOnlyList<Type> candidates = manager.GetImplementations<IDiscoveryExample>();
foreach (Type candidate in candidates)
{
    Console.WriteLine(candidate.FullName);
    Console.WriteLine(candidate.Assembly.FullName);
    Console.WriteLine(candidate.AssemblyQualifiedName);
}
```

The [console and Web examples](../../../implementations/csharp/examples/type-manager/README.md)
compile this query with a dedicated sample marker and display type and defining
assembly names. They discover both direct and
inherited implementations from an initially unloaded library. Queries include
concrete, closed nonpublic classes and classes without usable constructors. The
consumer must decide which candidates are suitable for its own purpose. The Type
Manager never constructs them, invokes application initializers or attributes, or
automatically registers them in DI. Runtime-controlled module initializers remain
subject to normal .NET loading semantics.

When Configuration has its own published marker, it can make the corresponding
`GetImplementations<IConfigurationObject>()` query. This is an illustration of a
future consumer call, not a declaration of that interface or a dependency from
Type Manager to Configuration. No such public marker or event contract is added by
these samples.

Configuration will own POCO eligibility, construction, retaining its own object
instances, storage, common/service-specific merging, overrides, validation,
locking/checksums and change notifications. Its persisted key and version policy
also belong there. Exact assembly-qualified lookup in Type Manager neither strips
assembly versions nor interprets persisted configuration keys or class-name suffixes.

Registering an existing `ITypeManager` as a singleton only reuses the catalog. It
does not implement singleton retention for discovered configuration objects. DI
scopes must receive the prepared catalog, not start another preparation through a
registration factory. Querying does not load additional assemblies, rescan catalog
members or enlarge membership after a later unrelated assembly load.

The host can use existing bootstrap values to choose preparation inputs. Type
Manager does not initialize or mutate `EnvironmentOptions`, infer the application
name, orchestrate `IStartable`, or supply a Startup Pipeline. Preparation failures
must stop host startup before container construction; an immutable report is
available only after successful preparation.
