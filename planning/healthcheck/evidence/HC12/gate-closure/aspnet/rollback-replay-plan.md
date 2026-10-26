# Independent ASP.NET rollback and ignored replay plan

The immutable reference is
`.artifacts/hcc/a1/native/reports/mutation-report.json`, SHA-256
`afcc1871cc1d4e12b74a352809b9e75c054c2fa3b9d13e61f1c166a900eb800e`.
Its selected original source is
`Healthcheck/AspNetCore/HealthcheckApplicationBuilderExtensions.cs`, LF SHA-256
`d81256db1acc93e948d72bdc7c6df713cc6f9e436b02d3b9f3b22a8d7f66efa4`.
The report retains 21 Killed, three Survived, zero Timeout/NoCoverage, 72
CompileError and five Ignored. Original statuses remain immutable.

The omission inventory is exact: **CompileError IDs 210 through 281 inclusive**
and **Ignored IDs 191, 198, 201, 282 and 289**. The 72 compile statuses span
`HandleAsync` lines 41–94: 16 statement removals, 13 block removals, 11 boolean
mutations, ten constant-condition mutations, nine strings, four logical-not
removals, three equality, three logical, two negations and one EndsWith-to-
StartsWith replacement. The five block-covered IDs span lines 20–107. Native
Safe Mode's method rollback is not independent proof that every replacement
fails to compile.

The existing C# `HealthcheckRollbackMutationVerification` provides the relevant
exact-span/native-baseline/restoration pattern, but is hard-pinned to historical
StateCoordinator automatic03, its source/report hashes and UnitTests. Its
current invocation cannot legitimately replay this ASP.NET report. The new
`HealthcheckAspNetOmissionReplayVerification` supplies a separate targeted C#
entry point. The child protocol/auditor add an explicit manual source-replay
phase; the existing parent staging support and child observer are unchanged.
Dispatcher wiring remains coordinator-owned. Native validation is pending.

A truthful targeted replay has these inputs and outcomes:

1. Require a new/empty isolated output. Pin the report and its exact selected
   source text, all copied maintained source/structural/fixture inputs, SDK,
   runtimes and tool/launcher bytes. Copy source inputs without modifying the
   checkout or old evidence. The historical source must be an explicit,
   recorded baseline override in that copy because the current adapter has now
   been simplified; it must not be presented as current-candidate bytes.
2. Build and run the unmodified historical source with the selected frozen
   detector harness before any mutation. Pin the exact successful native case
   inventory, including scheduling, rather than accepting a count alone. A
   current detector harness is a new replay context and gets no retroactive
   credit as the original automatic execution.
3. For each of the 77 exact report definitions, calculate its one-based
   start/end source offset against the pinned original text and apply only that
   replacement to a clean baseline. Retain the original slice, replacement,
   location, mutated source hash and native compiler output. Build without
   Stryker's overlapping instrumentation or whole-method rollback. A build
   failure is independently classified only when actual C# diagnostics identify
   that target replacement; timeout, restore/tool failure and unrelated build
   errors remain unresolved.
4. For every successful build, run the exact full HTTP detector filter with
   scheduling and retain native TRX, output, timeouts, individual failure
   messages/stack traces and baseline-case reconciliation. Do not turn arbitrary
   native nonzero exits, missing cases or fixture timeouts into kills. Attribute
   contract assertions individually; preserve survivors and unresolved runs.
5. The HTTP cases launch real fixture processes. Pin the newly built mutated
   Core and every actually deployed fixture/Core dependency, then collect
   actual child origin and normal/attributed-managed termination evidence.
   A direct-source replay Core has no Stryker selector/controller: it cannot
   masquerade as an instrumented child in the existing selected-mutant v4
   audit. Explicit direct-replay provenance or a separately authorized
   controller-aware reproduction must establish the exact replay bytes and
   real process lifecycle. Origin-only receipts, timeout and forced stop remain
   insufficient.
6. Restore exact historical baseline bytes after each control and verify every
   other source/structural input unchanged. Finish with a fresh restored native
   build, the exact full baseline cases passing, and a complete before/after
   source comparison. Pin any current-source restoration separately from the
   historical override.

Per-control data should retain native ID/original status, report/source/harness
pins, source location/slice/replacement, compiler diagnostics, compiled/deployed
binary pins, actual native TRX/cases, child-audit receipts, timeout/forced-stop
flags and restoration proof. Terminal outcomes must distinguish independently
uncompilable, contract-killed, survived and unresolved. A selected-ID smoke run
is explicitly partial. No all-77, full-Healthcheck or Release qualification
follows until every required actual result and disposition exists.

No replay native execution, compile-error disposition, ignored-status waiver or
strict-score adjustment is claimed by authoring this plan. The fresh automatic
run of the simplified current adapter is another independent evidence stream.
