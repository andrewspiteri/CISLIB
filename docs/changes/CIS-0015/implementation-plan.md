---
title: CountryData unit testing examples
type: delivery-plan
status: Active
change_id: CIS-0015
---

# Scope

Update the maintained CountryData example with readable, IDE-discoverable MSTest
unit examples in the existing CIS.Core.UnitTests project. Reference the compiled
CountryData project and reuse public CIS.CommonTesting controls. Production
application behavior and the live provider dataset are unchanged.

One coherent implementation task includes tests, the small test-owned reader and
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
The application reference brings its existing dependency closure; do not add
packages, a test project, friend access, copied application source or a new harness.

## Approved finite allocation

One task: 7,200 active seconds (2 hours), 4 logical attempts, including 300
restoration seconds. All agents and already-performed intake, source, docs,
formatting, restore/build/tests and review share this containing allocation.
Retain the physical 6,442,450,944-byte ceiling and 67,108,864-byte reserve for
.artifacts, .cis/local and implementations/csharp. No historical cleanup or
second pool/worktree. Existing completed owners are not reopened.

Andrew Spiteri explicitly approved this allocation on 19 November 2026. Native
execution begins only after the fresh CIS owner has its recorded finite allowance.
Push, merge and release are outside this task.
