---
title: CIS-0015 verification
type: verification-record
status: Draft
change_id: CIS-0015
---

# Verification

| Task ID | Check | Command or artifact | Result | Notes |
| --- | --- | --- | --- | --- |
| WORK-001 | CountryData unit examples | Ordinary dotnet test filter CIS.Tests.UnitTests.CountryData | Passed: 23; failed: 0; skipped: 0 | Actual compiled application; no application host/network. |
| WORK-001 | Existing CommonTesting controls | Ordinary dotnet test filter CIS.Tests.UnitTests.CommonTesting | Passed: 13; failed: 0; skipped: 0 | Exercises the helpers under the updated test reference graph. |
| WORK-001 | Native build and formatting | Release Unit build; changed-file whitespace formatter and verify | Passed | Zero build warnings/errors; only three new C# files included in formatting. |

## Delivered examples and boundaries

The user requested CountryData testing examples starting with unit tests that
reuse CommonTesting, then explicitly approved one shared two-hour/four-attempt
allocation including five restoration minutes. Earlier owners remain complete.

The existing Unit project now references the actual CountryData project. Its
public catalog and individual lookup steps are exercised without copying business
source or adding friend access. The small setup uses ScriptedRestClient,
ManualHeartbeat, ManualTimeProvider, RecordingHealthMonitorFactory,
RecordingLogger and DisabledMetrics.Instance. Only the application's initialized
settings reader and stopping lifetime need small test-owned implementations.

Fourteen catalog cases demonstrate aliases/no lookup I/O, captured refresh
registration with explicit dispatch, complete dataset replacement, five expected
failure modes retaining the same last-good record, initial failure, original
unexpected exception/log identity and joined held work before monitor withdrawal.
Nine lookup-step cases demonstrate trimming, stable failure codes, cached selection,
prepared result metadata and compensation of a response draft. A queued sentinel
ensures accidental logical REST reads are admitted/counted by the fake.

The example README lists expected results, the ordinary filtered test command and
the progression to existing HTTP/workflow/package integration tests. CommonTesting's
README links directly to the actual unit examples. No scheduler, health-policy,
mapper/codec/retry, actual HTTP delivery or hosted shutdown qualification is inferred
from these manual controls. No production behavior or live country dataset changes.

## Native and source observations

Ordinary Windows restore/build/test receipts are retained under ignored
`.artifacts/countrydata-unit/CIS-0015`: examples/countrydata.trx records 23 passing
cases and common-regression/common.trx records 13. The Unit Release build has zero
warnings/errors. The documented dotnet test path was used with no-build/no-restore
after its real prerequisite build. No failed native invocation or skipped case exists.

The one new project reference brings the example's already-approved optional adapter
closure into the test graph. Every resolved NuGet ID/version pair was checked against
baseline repository lock files; none is new to the repository. Only the Unit lock
graph changes. Four unchanged dependency locks received generated line-ending churn;
their semantic diff was verified empty before restoring those exact files. SDK,
runtime, production references, packages, source mapping and friend lists are unchanged.

Independent preflight/source review checked the public boundaries, cleanup, expected
fake semantics and direct lookup-step assertions. It identified no remaining blocker.
Native execution used this fresh approved owner; no prior result was rerun to clear
a CIS status, and no managed incremental-currentness or broader release claim is made.

Strict documentation validation passes 219/219 documents with zero warnings/errors.
The approved physical roots observed 5,911,838,092 bytes, below 6,442,450,944 with
463,503,988 bytes remaining beyond the protected 67,108,864-byte reserve.

Conservative shared effort is 5,396,809 ms (about 1 h 30 min): 5,280,000 ms of
source/intake/all-agent/review/controller/local-closeout estimates plus 116,809 ms
of measured native commands. One logical delivery attempt is charged out of four.
The 7,200,000 ms allowance retains its 300,000 ms restoration reserve and
1,503,191 ms beyond it. Source estimates/manual attempt charges remain separate
from CIS's automatic counters; no managed native attempt was opened. The local
resources-final.json preserves component costs and physical observations.

The user's untracked launch settings retain SHA-256
ea7e7219222183193f0361ae674998f1edc43a3789f118748c59f166a4d29784.
No push, merge, release, new runtime/test framework, production code, provider
dataset or historical cleanup is included. The change remains on
codex/countrydata-unit-examples for review.

WORK-001 is Complete with no pending native invocation or recovery hold. Its
source, documentation and focused observations are retained for local review.
| WORK-001 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1686; failed=241; possibleTokenSavings=591; ledgerDigest=sha256:55a6a05420fedae943048f2777dfc651ad360ae499ffe008cb977c862bc167db |
