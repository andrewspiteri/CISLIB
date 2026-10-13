# Runtime promotion evidence

This checkpoint retains development evidence for the Type Manager module promoted
into the shared CIS.Core assembly. It does not claim a completed milestone,
package release or approval of concurrent logging source.

The reviewed Core candidate SHA-256 is
`e5ac309a81db3342df379437c0d6e49fdfa12702c052e70a30c2a051ffe39b63`.
Windows built that actual Core DLL; Ubuntu replayed those exact managed bytes.
Both platforms passed 63 unit cases and 9 runtime architecture cases (72 total),
and 11 fresh runtime process cases plus 2 generator rejection cases (13 total).
The compatibility report checks the frozen reference API, unchanged compiled
consumer and rebuilt historical source against the same candidate. It hashes the
explicit Core and two approved dependency DLLs used by each replay.

The source input hashes identify the scoped tests/module and harnesses. The full
combined DLL includes concurrent logging work and is kept in ignored task artifacts;
there is no immutable source commit or release identity for this checkpoint.
Source paths and timestamps in native TRX files are execution details, not proof
that all current working files still match a previous binary.

The two negative logs run the **new** regression host against the earlier actual
Core DLL (`52e7e273b39ec45a8d9e5daf9f90e216aec07988b4c4271aae633af286c79810`).
Both fail for their expected reason: loading before complete validation, and a
dynamic assembly replacing the selected executable. They qualify those new tests;
they are not a complete mutation suite.

Web SDK Debug inventory generation passed nine cases per platform: default/native/
opposite RID, each under build, publish --no-build and publish-with-build. Opposite
RID output is inspected but not executed on the wrong operating system. These
source-import tests do not resolve the clean NuGet package RID restore defect.

## Review disposition

The initial runtime review found D1–D10; the follow-up confirms no regression of
those fixes. They address contextual diagnostics, input/task semantics, relative
paths, selected-asset attribution, path normalization, reference-attribute metadata
and BOM handling. The follow-up findings were addressed as follows:

- F1: validate every selected file and existing runtime identity before loading
  any new selection. A fresh process verifies a valid earlier assembly stays
  unloaded when a later selected reference assembly is invalid.
- F2: `AssemblyIdentity` consistently means the manifest's selection;
  `ActualAssemblyIdentity` means the observed runtime/file identity, including
  host mismatch. The unpublished behaviour test was corrected.
- F3: reentrant matching keeps a cache entry already published by a nested call
  using TryAdd. Metadata resolution can invoke runtime-controlled host callbacks;
  discovery does not deliberately call application code.
- F4: dynamic assemblies are excluded from reuse/conflict inspection, and a
  dynamic bind result is rejected. A same-identity dynamic impostor fixture now
  proves the real selected executable is cataloged.
- F5: invalid manifest path text is reported against the public `options`
  parameter, retaining its inner argument exception.

The ordinary discovery host now requires its marker rather than conditionally
skipping discovery checks. Runtime replay requires empty output directories,
checks source completion and exact Core hashes, and decodes logs as UTF-8.
Compatibility uses an explicit dependency allowlist and PowerShell 7. Discovery
failures document the actual loaded assembly location, including identity reuse.
The cancellation test name no longer claims it instruments file I/O.

A closure review is pending. The package, DI examples, full mutation coverage,
explicit enumeration counters, deterministic final-publication cancellation,
performance/memory results and final six-family release bundle remain open.
