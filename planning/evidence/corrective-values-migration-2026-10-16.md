# Native value verification migration

16 October 2026, based on `f0adb63e`. Corrective Batch B continues by retiring the scalar, collection and qualification Python drivers. Shipping Core, dependencies, public API and unchanged conformance vectors are untouched. Review is local; no Claude transfer is involved.

Existing C# integration classes execute the same 152 physical cases: 47 original values, 12 normalization rows, 22 collections and 71 qualification rows. A new catalog case fixes their complete, unique membership. All 153 value cases plus the previous 73 configuration/environment cases form the 226-case regression selection.

The parent now independently checks valid typed kinds and factory invocation; exact Core-versus-adapter failure attribution, exception category and parameter; qualification factory invocation and safe native exceptions; and JSON collection node/depth counts bounded by 1024/8. Actual PID, OS/runtime, fixture/Core/input hashes before/after each child, parent environment/CWD preservation, no lifecycle API, collector-sentinel exclusion and no filesystem outputs are retained. Commands, actual PIDs and original reports are in native TRX output.

The configuration-specific process helper is renamed `FixtureProcessChecks` and reused for these logging fixture cases. Its process checks are unchanged. The earlier configuration receipt's symbol name is a historical navigation observation.

The three retired scripts are recoverable from the base revision. They contain no separate corruption-control matrices; existing C# comparator/error/ownership controls remain in the unit selection. Retirement source hashes are recorded below and in `.artifacts/corrective/value-readers/retirement-audit.json`.

This leaves **67 nonhistorical implementation Python files**: 58 other DiscoveryHost readers/helpers, six engineering utilities and three Type Manager fixture utilities. One additional maintained Python selector remains under docs/modules/logging/examples and belongs with file-recovery migration. Five historical implementation Python files and 106 frozen planning/evidence Python files remain preserved. Other native migration, supported IDE acceptance, complete Ubuntu Developer/coverage/mutation/compatibility, examples and release qualification remain open. The original roadmap remains paused while authorized corrective work continues.

## Execution

Windows focused integration `2bae1da6813648c6a4cfada6e2ceaa02` and Ubuntu focused integration `88091d86ac114a64ba463e168fb77dc9` each passed 226 cases, zero failures/skips. Windows unit selection `b195728b97064e8bb9f506b7a189cd5f` and Ubuntu unit selection `3392f81383af4f378121202574f0ba84` each passed 399 cases, zero failures/skips. Ubuntu uses scoped current source copies with isolated outputs; this is not a shared exact-package or full-lane qualification.

The optional initial Debug architecture selection `5701d81c9912447b9d4cc12dedb4bcf1` passed 18 of 19 cases and correctly rejected one compiler-consumer report bound to the previous Release Core bytes. No architecture pass is credited from this attempt. A normal Developer checkpoint regenerates matching Release consumer artifacts before architecture execution; its final result is recorded below.

Original SHA-256 values:

- `verify-log-values.py`: `a6a2c3c8060af828cb36281b6fe2a642826cfc17690d18e7e14b68a0d63b754a`.
- `verify-log-collections.py`: `9ff1f2097cafa81689802bd03e708fbcaa773d4cd7484bb72e512b431d2fdb01`.
- `verify-log-qualification.py`: `ee17ce2e6b3cd8381f414d9dee77171e0ed8d7abbdb4e24c96e38f92ab6e715c`.

## Final Windows checkpoint

Run `0c64015288274be7b5c4dffc419af8c4`, **08:05:39–08:18:11 UTC**, passed **3,659 unit, 1,646 integration, 619 architecture and 24 business cases: 5,948 total**, zero failures/skips, exit 0 and zero validation issues. All normal Developer stages passed, including formatting, build, matching compiler consumers, dependency/package boundaries, discovery reconciliation and ledger/scaffold checks. The previously rejected consumer precondition is resolved in this checkpoint. Mutation and compatibility were NotRun; no complete corrected release or full Ubuntu Developer pass is claimed.

Retained checkpoint: `.artifacts/corrective/value-readers/developer-0c64015288274be7b5c4dffc419af8c4`. Summary SHA-256: `8a28e7e540386740c706532b5b675a80eb5eb7f3a09ad45d5ab06fad3fa7c96c`. Source-inventory SHA-256: `c653d938498f159f37336edf55c1610a42210770b1a0cea11f867bcd500eeae6`. The source is the base revision plus this migration and preserved unrelated edits, not a clean release candidate.

Native integration case audit: 226 per lane, only the two declared platform-specific path names differ, zero unexpected differences. The value portion has 153 identical case identities. The four value catalogs execute 152 children: 142 real Core construction scenarios and ten explicitly identified adapter rejections.

Final schema-2 graph refresh: **10,833 nodes, 51,636 edges, zero warnings/errors**, build `4223a4557904e2cced52e4c5cabba1eb9e3c328a494b8b99a12b6e916eeccdeb`. Value-process and shared fixture-helper classes resolve uniquely. The depth-one value-process related query is fresh/untruncated, four nodes/four traversals, proposed edges excluded; compiler/semantic extractors remain 6/3. This is bounded navigation, not complete semantic or coverage evidence.
