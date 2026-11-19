---
title: CountryData unit testing examples
type: delivery-plan
status: Active
change_id: CIS-0015
---

# Scope

Update the maintained CountryData example with readable, IDE-discoverable MSTest
unit examples in the dedicated examples/CountryData.UnitTests project, registered
under /examples/ in CisLib.slnx. Reference the compiled CountryData project and
reuse public CIS.CommonTesting controls. Production
application behavior and the live provider dataset are unchanged.

The implementation includes tests, the small test-owned reader and
lifetime collaborators, project/lock references, example/CommonTesting guidance,
scoped formatting, focused native verification and review.

## Examples and expectations

- Initial valid publication, name/ISO aliases and missing identifiers without lookup I/O.
- Configured manual refresh registration, explicit trigger and complete replacement.
- Logical REST failure or invalid dataset retains the last good snapshot.
- Initial failure prevents a refresh registration; unexpected faults retain their cause.
- A held scripted refresh remains owned through catalog disposal.
- Direct lookup-step unit examples cover normalization, expected failures and compensation.
- Existing HTTP, workflow engine, scheduling, health-policy, mapper/codec and package
  fixtures retain their separate integration responsibilities.

Use ScriptedRestClient, ManualHeartbeat, ManualTimeProvider,
RecordingHealthMonitorFactory, RecordingLogger and DisabledMetrics.Instance.
Advance virtual time explicitly and trigger manual callbacks explicitly. No real
host, sockets, sleeps, live provider or optional collector is activated by unit tests.
The application reference brings its existing dependency closure. The test project
uses the same centrally pinned MSTest packages and runtime as the shared suites;
do not add package versions, friend access, copied application source or a new harness.

## Approved finite allocation

Shared total: 7,200 active seconds (2 hours), 4 logical attempts, including 300
restoration seconds. All agents and already-performed intake, source, docs,
formatting, restore/build/tests and review share this containing allocation.
Retain the physical 6,442,450,944-byte ceiling and 67,108,864-byte reserve for
.artifacts, .cis/local and implementations/csharp. No historical cleanup or
second pool/worktree. Existing completed owners are not reopened.

Andrew Spiteri explicitly approved this allocation on 19 November 2026. Native
execution begins only after the fresh CIS owner has its recorded finite allowance.
Push, merge and release are outside this task.

## Project placement correction

The initial implementation placed these examples in the shared Core unit suite.
The user corrected that choice: the tests must have their own project under
examples in the solution. WORK-002 performs this location/project correction and
removes the extra shared-suite reference. WORK-001 and its observations remain
historical; it is not reopened.

The original 7,200 seconds, four attempts and all storage/restoration limits are
unchanged. Leave WORK-001 at 5,700 seconds/three attempts, retaining its earlier
conservative 5,396,809 ms cost, one used attempt and protected 300-second
restoration reserve. Assign WORK-002 1,500 ordinary seconds/one attempt.
This is redistribution of unused capacity; all earlier costs remain charged.
The historical scope and constraints remain intact for WORK-001. The added
placement scope applies the user's specific override to the original project
location choice for WORK-002 only.
