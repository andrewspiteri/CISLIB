# Isolated package restore experiment

An early NuGet props import cannot influence the first restore's RID graph.
An isolated build-only package experiment now passes on both Windows and Ubuntu:
a guarded target before ResolvePackageAssets invokes the SDK's Restore target with
the inferred RuntimeIdentifier as a global property. The SDK still owns dependency
and RID selection; no runtime dependency/RID parser was added.

Both native runs passed a clean build, publish --no-build and host execution with
four expected project/transitive assemblies. Late host enablement was explicitly
rejected. The package was generated into a private empty feed/cache workspace and
is named CIS.TypeManager.BuildProof; it is not the CIS.Core release package.

This is a promising experiment, not adopted canonical integration. The additional
restore's incremental/offline/no-restore, concurrent graph and native-package/RID
behaviour need qualification and review. The actual packed Core consumer and
one-flag setup documentation remain unfinished. The prototype script is retained
here for reproducibility; canonical build targets are unchanged by this proof.
