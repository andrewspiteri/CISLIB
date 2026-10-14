# Runnable Type Manager host integration

14 October 2026. Development evidence for TM-021–025, following runtime acceptance
checkpoint `53dec080`. Formal milestone reconciliation and the final consolidated
release candidate remain open.

## What ran

`windows/results.json` and `linux/results.json` each contain 66 passing checks:

- Two modes: source project references and an exact-package variant generated from
  identical sample sources by replacing the Core reference/inventory import.
- Two real hosts: a plain-.NET console and a separate ASP.NET Core application.
- Four layouts per host/mode: Debug build, Release build, Release folder publish,
  and Release publish without building.
- One successful fresh-process run plus three broken deployments per layout:
  missing manifest, malformed manifest and missing selected library.
- Two additional HTTP checks, one for each mode's Web Release build output. Each
  makes two real requests and verifies distinct scoped consumers across requests,
  same-consumer reuse within a request, and exact prepared-manager/cache identity.

The hosts discover both direct and inherited/nonpublic candidates from a library
that starts unloaded. Neither implementation class is directly referenced or
constructed by host code. Both display type/defining-assembly identities and the
completed preparation report. Web creates even its builder only after preparation,
then registers that exact manager instance. Console deliberately needs no container
or ASP.NET shared framework. Broken deployments require exit 1, the exact affected
file path and original failure detail on stderr, without a completed-preparation
or builder/container/application-start marker.

Explicit `--verify` mode loads an unrelated, matching fixture afterward. Catalog,
definitions, cached and previously uncached queries and exact lookup remain fixed.
This is not runtime plugin support. DI identity assertions do not measure internal
enumeration counts; those have separate structural and source-instrumented evidence
in the runtime-acceptance checkpoint.

## Candidate and isolation

Package SHA-256: `d3249a77c8ee4c504e87b9c3f52b3be691430bcca47a7bc70468757dc9452ea9`.
Packed Core SHA-256: `abb66e6159e3fd111f341e32c4a61cec12c2bcde738510be9229e5fe76155025`.
The same package and verifier bytes run on Windows x64 and Ubuntu 24.04 x64 with
SDK 10.0.401. Each platform builds its hosts natively; their host binaries are not
claimed identical. Source Core hashes are recorded per result and differ from the
package candidate. The source tree includes coordinated logging changes and is not
a clean release build.

The verifier uses private feeds/caches, approved dependency archives, an environment
allowlist and explicit parent-build isolation. It records copied input hashes with
a limited provenance scope, not a full transitive build attestation. Actual restore
assets must list only the private package folder/feed. Core bytes are checked in
consumer outputs. The helper is hashed in the private package cache and must be
absent from deployment and runtime dependency graphs; this is not an independent
trace proving which task-host DLL executed.

`inputs/` retains exact relevant verifier/sample/policy bytes. `checkpoint.json`
records cross-platform consistency checks. Generated package project files for
inspection are retained under `package-projects/`; regenerate their complete trees
with the verifier for execution. Developer source projects remain under
`implementations/csharp/examples/type-manager`. No generated copy containing logging
implementation source is included here.

## Solution and framework boundary

`solution/` records locked restore, formatting, Release build and the full checker
for all 26 registered projects in an isolated Windows code snapshot. The build has
zero warnings and errors. Only the Web example is explicitly permitted the implicit
ASP.NET shared framework; Core's two approved runtime packages are unchanged.

`boundaries/` records 11 checks: five registered projects pass, added ASP.NET
references in Core/Console fail, Web without its policy entry fails, and attempts
to widen shipping/test-support policy fail even when the reference claims to be
implicit. A mutant removing only the example-role condition accepts the forbidden
shipping reference, proving the control detects that guard. The harness records
project/policy/configuration hashes, restores mutated bytes and rechecks them.

Boundary verification requires PowerShell 7.2+ and disposable ordinary copies;
hardlinked inputs are forbidden by that precondition, not detected by link-count
inspection. Reparse-point checks are accidental-misuse protection, not a security
boundary for malicious paths. A non-example policy's extra-framework key is ignored
and cannot widen permissions; rejecting the inert key itself remains optional.

## Reviews, reproduction and limits

The subsequent canonical `eng/verify.ps1` Developer run is retained separately
under `developer-check/`. Restore, framework boundaries, formatting, Release build,
contract checks, packaging and package boundaries passed. The unit suite reported
3,256 results: 3,194 passed and 62 failed, all in logging flush/file-timer test
classes. All 68 Type Manager unit results passed. The runner stopped before
integration and architecture; this is a failed combined run, not a full-suite pass.
The logging thread received the failures for investigation. Its cause is not
established here. The summary's relative references retain their original layout;
only the summary, source inventory, unit report and execution/console logs are
copied here, so this directory is not a standalone complete verification bundle.

`reviews/` retains the read-only findings and closure reviews. Corrections include
removing the verifier's global opt-in override, strengthening HTTP lifetime evidence,
exact failure diagnostics, private dependency-source checks and meaningful role-gate
controls. The final dangling-link correction follows the reviewer's exact
recommendation and is followed by a complete 11-check rerun.

See the runnable [example instructions](../../../../implementations/csharp/examples/type-manager/README.md)
and [Configuration boundary](../../../../docs/modules/type-management/configuration-boundary.md).
The process verifier is `implementations/csharp/eng/verify-type-manager-examples.py`.
It requires the exact package, the approved dependency archive cache and a new
output root. The boundary verifier accepts its disposable source tree and a separate
empty output root; it must not target canonical source or hardlinked copies.

Early attempts found a full disk and an assumed output path that ignored native
RID inference. Superseded generated outputs were removed; final runs ask MSBuild
for the actual target path. A later boundary rerun encountered a transient system
SDK workload-manifest inconsistency. Default SDK evaluation subsequently succeeded
and the unchanged-environment rerun passed, without disabling workload resolution
or modifying the SDK. Failed/incomplete runs are not counted as passing evidence.

This checkpoint does not qualify Visual Studio/PublishUrl, change unsupported
deployment profiles, implement Configuration or Startup Pipeline, complete the
whole mutation family, approve publication or replace final release-bundle replay.
