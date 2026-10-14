# Package review disposition

The read-only review identified actionable issues in package guards and evidence.
The initial review and focused exact-version review are retained beside this file.
The following distinguishes corrections from claims checked against the pinned SDK.

1. Dependency evaluation now passes `BuildProjectReferences=false` when resolving
   references. It must not build the new private helper as a checker side effect.
2. The ordinary-package guard applies only to executable hosts. A fixture library
   now uses an ordinary Core PackageReference; the fresh parallel solution build
   enables the flag globally and includes that library.
3. Cross-RID restore replaces a project's assets graph. The verifier now expects
   NETSDK1047 from a subsequent native build without restore, then restores native
   assets explicitly. Concurrent different-RID operations on the same project and
   intermediate directory are unsupported; parallel distinct projects are tested.
4. The original copied Directory.Build.props already enabled lock files, contrary
   to one review claim. The revised independent consumer props explicitly enable
   them; the offline test asserts their presence, forces a locked restore without
   ignoring failed sources, and verifies unchanged lock-file hashes.
5. The five tested settings (PublishSingleFile, PublishTrimmed, PublishAot,
   SelfContained and disabled CopyLocalLockFileAssemblies) now go through real
   build and publish-without-build commands. This exposed a real ordering gap: runtime-pack resolution could fail
   before the CIS diagnostic. Validation now also runs before BuildOnlySettings,
   the first step of the pinned SDK's no-build publish path. Rejected publication
   must not produce files in its output directory.
6. Fresh-cache parallel restore/build and subsequent parallel no-restore build
   both inspect and execute both host outputs. These are parallel solution builds,
   not claims about MSBuild static-graph execution.
7. Consumers now use minimal independent build properties, assert the pinned SDK
   and its native RID, and hash-check the helper used from the fresh package cache.
8. The suggested DestinationSubPath change is rejected. In SDK 10.0.401,
   Microsoft.NET.Publish.targets lines 608–610 construct ResolvedFileToPublish's
   RelativePath from DestinationSubDirectory + Filename + Extension, exactly as
   the inventory does. Line 650 explicitly notes unresolved DestinationSubPath
   concerns. Changing only the inventory would diverge from the actual copy path.
9. Missing/replaced early hooks and unqualified SDK versions get CISTM009. The
   matching central plain version is normalized into an exact range; its focused
   review returned PASS. Static-graph restore is outside the qualified matrix;
   build validation remains the guard when restore does not execute validation
   targets. Package corruption controls now include an unchanged positive control
   and compare the package Core DLL with the actual Release assembly. A valid PE
   with an appended byte must fail that specific comparison.

The follow-up evidence review prompted manifest deletion before individual and
parallel regeneration checks; the separate incremental check still requires
unchanged bytes and modification times. It also prompted the valid-PE Core-byte
negative control and narrower wording for the five tested profile settings.

All execution claims remain tied to their individual retained reports. An earlier
passing matrix does not qualify a later package merely because the version string
is unchanged; package and assembly SHA-256 values identify the candidate.
