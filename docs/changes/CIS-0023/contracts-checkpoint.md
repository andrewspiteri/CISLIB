---
title: RM-06 contracts and capture checkpoint
type: document
status: Draft
change_id: CIS-0023
---

# WORK-001 contracts and capture checkpoint

The portable v1/capture/public-port boundary is frozen for parallel publication
and Security implementation. This checkpoint covers AC01–03 and the public
surface portion of AC12, not SQL, broker, host or public-package acceptance.

- Native Windows targeted unit selection: **56 passed, 0 failed, 0 skipped**.
- Independent portable schema/wire/byte-identity selection: **42 passed, 0 failed,
  0 skipped** (20 structural cases, 16 raw-wire cases, four identity pairs and
  two catalog/evaluator checks).
- Affected Core/CommonTesting/unit and architecture builds: **0 warnings/errors**.
- Pinned SDK scoped whitespace format and `--verify-no-changes`: passed for the
  13 changed C# files; no restore or unrelated formatting.
- Independent wire/profile review and source-eligibility/ownership review found
  no remaining actionable issue after the recorded corrections.

The first 52-case unit run passed 51 and failed the exact-size encoding boundary.
The corrected bounded encoder honors framework growth hints, checks actual final
payload size and admits additional encoding buffers nonblockingly against the
same existing Messaging budget before allocation. Tests cover small and large
ASCII/Unicode exact limits, refusal/release and original Write faults. Capture
close joins an application codec that has actually not returned.

The first multi-node build returned failure without an error diagnostic; retained
serial builds succeeded. Sandboxed workspace formatting failed on its named pipe;
the ordinary pinned SDK formatter and verification succeeded with the permitted
sandbox override. An attempted folder-mode invocation was rejected because it
cannot combine `--folder` with `--no-restore`; it changed no product files. All
observed failures/costs remain in the ignored native journal.

Actual results and logs are retained under `.artifacts/audit/CIS-0023/WORK-001/`:
`contract-unit-corrected-tests` and `contract-architecture-tests`. Source/runtime
checks used SDK10.0.401 and the selected .NET10.0.12 product runtime. The tool's
MSBuild workspace host is a tooling process, not an Audit runtime proof.

The exact supplied source and user-owned CountryData launch settings hashes remain
unchanged. Native SQL atomicity, actual transport retries/reconstruction, the
Security adapter and public consumer/host evidence remain in WORK-002–004.
