# Type Manager package delivery checkpoint

This is development evidence for TM-032, not publication approval or the final
six-family verification bundle. The optional `CIS.Core` MSBuild SDK, runtime DLL,
inventory targets and helper are assets of one package. The SDK imports RID
selection before the .NET SDK's first-restore inference and injects an exact
matching runtime reference. It never invokes a nested restore.

The producer is the isolated source copy at
`.artifacts/type-manager/package-delivery-source/`. It includes the coordinated
logging working tree as it existed when copied. Logging implementation is not
part of this change or its external review. The package smoke tests inspect and
execute the exact copied Core DLL, recorded by hash in each platform report.

Reproduce the producer pipeline from the isolated `implementations/csharp` directory:

```powershell
dotnet build CisLib.slnx -c Release -p:RestoreLockedMode=true -m:1
dotnet pack src/CIS.Core/CIS.Core.csproj -c Release --no-build --no-restore -o ../../.artifacts/csharp/packages
```

The retained producer build and pack logs record the actual results.
Architecture checks cover Type Manager and dependency boundaries;
`architecture.trx` was parsed by the repository's strict `Read-NativeTrx` reader.
The 34 cases include an unchanged package positive control, altered/missing
package assets and build-helper leakage.
The package boundary check compares all static build assets with source, the
helper with its Release binary, and the generated SDK version with the nuspec.

Windows and Ubuntu each pass 35 consumer checks with the same package and verifier
hashes. The final read-only review returned PASS. The package hash is
`d3249a77c8ee4c504e87b9c3f52b3be691430bcca47a7bc70468757dc9452ea9`;
Core and helper hashes are recorded separately in both platform reports.

The final Ubuntu run exited 0 and its complete JSON stdout is retained as
`linux-results.json`. Its `/tmp` output directory was unavailable when collection
ran afterward, so detailed per-command logs and copied manifests from that final
run are not retained. `linux-prior/` retains the earlier workspace-based 35-check
run against the same package, with its verifier source, logs and manifests. That
earlier verifier predates forced build-manifest deletion and verifier self-hashing;
its detailed logs must not be relabelled as the final run. This retention gap
remains part of the final portable-bundle work, not an inferred release pass.

The portable `implementations/csharp/eng/verify-type-manager-package.py` uses a
private feed/cache, the exact producer package, the approved Microsoft dependency
archives and the real native/RID fixture from the earlier source inventory matrix.
It runs actual console and Web consumers, then verifies incremental no-restore,
default folder publish without build, both RID selections, parallel solution
builds, offline cached restore, central versions and configuration rejection.
The Web inventory excludes Microsoft dependencies supplied by its shared framework.
Native execution occurs only for the lane's native RID; cross-RID outputs are
inspected without execution on the wrong OS.

The earlier second-restore experiment is superseded, not adopted. Visual Studio
MSBuild task hosting, its separate PublishUrl copy stage, unsupported deployment
profiles, and complete release qualification are outside this evidence. The
consumer suite requires the pinned .NET SDK and prepared RID fixture package;
this directory alone is not a self-contained portable verification kit.
