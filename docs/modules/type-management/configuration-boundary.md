---
title: Consuming a completed Type Manager catalog
type: document
status: unknown
cis:
  stable_id: cislib.document.modules.type.management.configuration.boundary
---

# Consuming a completed Type Manager catalog

Type Manager prepares one fixed catalog. Ordinary consumers query that completed
instance with the implemented CIS.TypeManagement/CIS.Configuration contracts:

```csharp
IReadOnlyList<Type> candidates = manager.GetImplementations<IConfigurationObject>();
foreach (Type candidate in candidates)
{
    Console.WriteLine(candidate.FullName);
    Console.WriteLine(candidate.AssemblyQualifiedName);
}
```

Queries include concrete closed nonpublic classes and classes without usable
constructors. Type Manager never constructs them, invokes application initializers
or attributes, or automatically registers them in DI. Runtime-controlled module
initializers follow normal .NET loading. Each consuming module owns eligibility;
Configuration rejects unsuitable shapes instead of filtering its complete query.

[CISApplication](../application/core.md) prepares Type Manager, then Configuration,
then builds one provider. The canonical
[CountryData application](../../../implementations/csharp/examples/CountryData/README.md)
uses that sequence. Explicit hosts await ConfigurationRuntime.PrepareAsync and
install retained objects before dependent startup participants; see
[Configuration hosting](../configuration/hosting.md).

Configuration owns construction/retention, overlays, validation, synchronized
reads, checksums and events. Its durable keys omit assembly versions and reject
collisions. Type Manager's exact CLR identities and assembly-qualified lookup
remain unchanged and do not interpret persisted Configuration keys.

An ITypeManager singleton reuses only its catalog. It does not retain another
module's objects. Scopes receive the same prepared manager without another
preparation factory. Queries load no extra assemblies, rescan no members and do
not enlarge membership after an unrelated assembly load. Preparation failure
prevents application Build.

The existing [minimal process fixture](../../../implementations/csharp/tests/fixtures/TypeManager/Host/Program.cs)
and Contracts/Transitive/Unused libraries retain distinct unused assembly,
private-constructor discovery, passive Configuration and deployment checks. Real
package consumers retain the versioned Core SDK, native inventory, build and
folder-publication boundary. The retired console/Web tutorials and parent
examples/type-manager/README.md are no longer current guidance. Use the
[inventory build guide](../../../implementations/csharp/build/type-manager/README.md)
for the source targets and versioned SDK.
