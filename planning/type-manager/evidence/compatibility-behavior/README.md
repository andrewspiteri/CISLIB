# Development behavior and schema compatibility

14 October 2026. This checkpoint qualifies TM-029 against the frozen runtime
candidate `d70fcfdc41b6d15829227f1cee60130ddad3f7f82cc11c7a2fcbf8d5f700fb09`.
It is not a published-version compatibility promise or a package release gate.

The original six-file `v1-development` API reference and compiled consumer are
unchanged. The new `v1-behavior-development` baseline contains 22 authored expected
snapshots, raw JSON inputs (including duplicate properties, BOM and invalid UTF-8),
consumer source and compiled consumer/extra-assembly bytes. Its consumer was built
against the original API reference, not the runtime candidate. Each native lane
rebuilds it against that reference and requires byte-for-byte equality with the
frozen DLL. Its explicit assembly metadata, disabled generated source/default
Win32 manifest, deterministic compilation and mapped paths avoid OS line endings
and Git revision metadata changing that binary. Expectations were
written from the intended contract, not recorded automatically from a passing DLL.
Once committed, this baseline must not be regenerated to make a candidate pass.

The verifier requires independently selected SHA-256 values for the candidate and
new baseline manifest; it pins the original manifest hash internally. It checks
the exact baseline file set, all listed hashes, and the candidate's completed
producer record. The TypeManagement C# file set and hashes must match that record
before private control assemblies can be built. This is accidental-mismatch
protection using a trusted producer record, not transitive build attestation.
Three additional Windows guard checks reject a wrong candidate pin, wrong baseline
pin and missing module source record before any compatibility case executes.

The 86 checks per OS comprise:

- Three original API/unchanged-binary/rebuilt-source checks and one build of the
  new consumer source against the original API reference to the exact frozen bytes.
- Twenty-two cases each through the frozen behavior consumer, candidate-source
  rebuild and unmodified standalone TypeManagement control (66 checks).
- Three standalone-module positive API/binary/source checks before their matching
  public-property-rename negatives (six checks).
- Ten targeted behavior regressions across nine source mutants, including both
  reflected and thrown numeric failure values.

Cases cover schema version rejection and additive fields, strict UTF-8 and duplicate
properties, default/relative paths, missing/corrupt/mismatched assets, synchronous
argument checks, cancellation winning over malformed input, candidate filtering,
assignability, exact lookup, query identity and protected collections. An extra
assembly is available through the consumer's dependency graph but initially
unloaded: an unselected lookup must leave it unloaded, then an explicit load proves
it was bindable and cannot extend catalog membership. A separate selected case
requires that assembly to load during preparation and appear in the report.

Mutants change failure values, accept schema 2, exclude nonpublic implementations,
return mutable query arrays, use `Type.GetType` for lookup, use linguistic ordering,
resolve relative paths from the current directory, accept duplicate properties,
and allow path traversal. Each must produce its specified incorrect observable
value; an arbitrary crash or mismatch does not pass. The traversal control supplies
a real DLL outside the manifest directory and disables all three relevant guards,
including Windows trailing-dot rejection. A separate renamed-public-property
control must fail ApiCompat, unchanged binary execution and source compilation with
the expected member diagnostic. Passing historical output is retained separately.

Every consumer process uses .NET 10.0.12; builds use SDK 10.0.401. ApiCompat 10.0.401
uses .NET 8.0.31, with a private Linux runtime installation. Ubuntu replays the exact
frozen Windows managed consumer/Core bytes under `/mnt/c`; source and mutant control
builds are native per OS. Builds have private caches and cleared package sources.
The process working directory differs from the application base directory. Reports
retain candidate, baseline, dependency, verifier, runtime-policy and tool hashes,
case names and observed-output hashes. This checkpoint does not copy the full tool
or candidate deployment and therefore is not the final portable release bundle.

`review/` retains the read-only findings and follow-ups. The final reviews returned
PASS. `provenance/` retains producer configuration/build logs and guard results;
`inputs/` records the named verifier, policy and module sources, not the full
transitive build-input closure. Native logs and every behavior snapshot are retained.
`superseded/` preserves failed intermediate reports: the initial traversal control
left a Windows guard active, an earlier Linux run was invalidated by a changed
uncommitted baseline, and two reproducibility attempts failed on generated source
or default manifest differences. None is counted as final passing qualification.

The coordinated combined Developer run `ce9cc2bf66cd49fdac044aed2e5c70dc` also
passed all 14 stages: 3,259 unit, 1,276 integration and 463 architecture results,
with no failures/skips and zero build warnings/errors. `developer/` retains its
summary, source inventory, logs and native family evidence. Its Core hash is
`7ad5e3e5ae8d829429ae65503d93c213c9743ef493b2ea552a763f3df1e298ac`;
it includes coordinated uncommitted logging work and is a different artifact from
the frozen compatibility candidate. Generated contract/build files remain in the
local Developer output. This is not the complete six-family release gate.

The repository graph is refreshed separately. Its extraction remains partial;
freshness and graph diagnostics are navigation evidence, not runtime verification.

Reproduce from the repository root, supplying the independently selected hashes
from `checkpoint.json` and an empty output directory:

```text
python implementations/csharp/eng/verify-type-manager-behavior.py
  --repo-root . --candidate-core <frozen-CIS.Core.dll>
  --candidate-report <completed-runtime-producer-results.json>
  --expected-core-sha256 <recorded-Core-SHA256>
  --expected-baseline-sha256 <recorded-behavior-manifest-SHA256>
  --api-tool <Microsoft.DotNet.ApiCompat.Tool.dll>
  --output-root <empty-output-directory>
```

Use `python3` and `--dotnet <native-SDK-dotnet>` on Ubuntu; `--api-dotnet` can select
the separate native .NET 8 host. The displayed command is split for readability;
use the appropriate shell continuation syntax or one line.

Limits: the new probe fixes both cultures to en-US. The separate query-ordering
checkpoint covers en-US/tr-TR/sv-SE. It checks all five failure enum values, but
operational discovery/unsupported-deployment and nonempty loader-exception coverage
remain in the runtime acceptance evidence. Cancellation precedence does not prove
that no file API was called. These targeted mutants do not complete TM-027's full
mutation family. Final exact-package replay, performance and the six-family gate
remain separate roadmap work. No production Type Manager source changes here.
