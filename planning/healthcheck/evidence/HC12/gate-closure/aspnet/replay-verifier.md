# Authored independent omission verifier

The new internal entry point is
`HealthcheckAspNetOmissionReplayVerification.RunAsync(string[] args)` in
`implementations/csharp/eng/CIS.DependencyChecker/HealthcheckQualification/Mutation/AspNet/`.
The coordinator owns dispatcher wiring and native execution. No build, test or
replay execution is claimed by this authoring note.

Proposed dispatch: `healthcheck-aspnet-omission-replay`.
Required options: `--repo-root`, `--report`, `--output-root`.
Optional: `--ids <distinct comma-separated native IDs>`, `--dotnet <full pinned
host path>`, `--stryker-core <exact 4.16.0 Stryker.Core.dll>`, and
`--profile historical77|gate-closure-a1`.
The last option locates common pinned provenance; no Stryker process runs during
these direct source replacements. Its default is the 4.16.0 global NuGet cache
under `NUGET_PACKAGES` or the user's `.nuget/packages` directory.

The omitted/default profile is `historical77`, retaining the exact old hcc/a1
report/source, 101-status inventory and all 77 omission IDs. The explicit
`gate-closure-a1` profile instead pins the current a1 source vectors:

- Report `.artifacts/hcg/a1/native/reports/mutation-report.json`, SHA-256
  `5a109f16c2c355a571d0efb38fd59a297f2077e3f9a03d1eefb53fd074f7a750`.
- Full report source, SHA-256
  `b4611cfccb7aafb377b7fbb663b81dbc6c58cb59af66a73b44fa85a1fd096d0e`.
- Exactly 94 selected definitions: 16 raw Killed, two Survived, zero
  Timeout/NoCoverage, 72 CompileError and four Ignored. Schema version is `2`.
- Exactly 76 omissions: compiler IDs 205–276 and ignored IDs 191, 198, 201, 277.

Neither profile accepts another report hash, source hash, status inventory or
ID set. There is no ID-translation credit. `--ids` is an explicitly partial
selection within the chosen exact profile. Current-profile summaries identify
the profile, use report-source baseline/restoration labels and report the
required full selection of 76. Historical default summaries retain their
historical labels and `all77Observed`; both expose `allRequiredObserved`.
Original raw Killed labels are provenance, not imported native qualification.
Profile implementation and its native validation remain distinct; no current
76-control execution is claimed here.

The verifier copies maintained inputs, records the current
canonical source, builds/runs the current full HTTP baseline (at least 64 cases,
including the required in-process HEAD case and scheduling), then explicitly
overlays the entire historical report source and builds/runs that same baseline.
Each single replacement is independently built and inspected; successful builds
run all baseline cases with scheduling. It retains source patches, actual
compiler diagnostics, binary pins, native process requests/output/TRX and actual
child propagation audits. Source inventories are checked before and after
replacement/build, during child audit, after each restoration, and against the
untouched input checkout. Historical and current baseline cases/binary hashes
must each match a fresh final restoration.

The appended nullable `Session.SourceReplay` record contains control kind,
report SHA/original ID, exact historical/canonical/current source pins, source
offset/slice/replacement, and actual built Core SHA/MVID. Prepare requires
`--phase source-replay --source-replay-control <record.json>` together. Audit
also requires explicit `--phase source-replay`; the default automatic audit
rejects the manual record. A passing canonical baseline includes that actual
mode-confusion negative control.

The source-replay audit verifies the exact report definition and resulting
single-source hash. Every child must be uninstrumented with controller null,
active ID -1 and the pinned Core hash/MVID. Normal child and actual parent exits,
runtime/dependency hashes and parent/owner/Core/executable staging checks remain
required. The common config, Stryker selector-source and native host/runtime
pins remain intact. Manual success is `SourceReplayChildPropagation`, never
`QualifiedChildPropagation`. Manual managed crashes, timeout, forced stop,
missing lifecycle receipts or instrumentation cannot qualify. No changes to
`HealthcheckMutationChildSupport` or `HealthcheckMutationChildObserver` are used.

Independent compile errors require actual `error CSxxxx` diagnostics on the
target source and no unrelated/infrastructure diagnostic. Contract-kill status
requires the complete baseline case inventory, passing scheduling, recognized
HTTP contract assertions, no infrastructure/timeout, and a positive actual
manual child audit. Cleanup-only fixture-disposal assertions are not accepted
as contract kills. Everything else stays survived or unresolved. The native
test session limit is 900 seconds with a 960-second outer process bound; fixture
limits remain 15 seconds. An outer process deadline produces unresolved
evidence, not kill credit.

Use a short new output root for Windows. Child staging deliberately retains
parent PID, unique owner ID, exact Core hash and executable directory; the
historical failed deep-path attempt is not repaired or relabelled. A smoke
`--ids` run cannot claim all 77/76 of its selected profile, fresh automatic closure, whole Healthcheck or
Release qualification. Existing reports and their original strict score are
never rewritten.
