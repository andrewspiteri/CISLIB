# Healthcheck mutation union

The user approved one complete, disjoint Unit/runtime + Pipes + ASP.NET union for
HC12. This command validates one OS lane. It does not qualify the rest of Core.

Run from `implementations/csharp`, using the repository-pinned SDK:

```text
dotnet run --project eng/CIS.DependencyChecker -c Release -- healthcheck-mutation-union --repo-root <repository> --manifest <inputs.json> --output-root <new-empty-directory>
```

Exit 0 means the complete union qualified. Exit 1 also covers a valid but pending
or unqualified result; inspect `results.json`. Malformed inputs are rejected. A
pending group produces no aggregate score. Original reports are never modified.

The input schema is `cis.healthcheck.mutation-union-inputs/v1-development`:

- `osLane`: `windows` or `ubuntu`.
- `snapshotRoot`: the immutable selected-source directory.
- `sourceManifest`: a pin for its `cis.healthcheck.selected-source-inputs/v1-development`
  manifest. All physical selected files, all current Core sources and the whole
  maintained configurations are rechecked.
- `partitions`: exactly three objects with groups `unit-runtime`, `pipes` and
  `aspnet`, the same `osLane`, a `configuration` pin, and `state` `Closed` or
  `Pending`. Pending objects require `report: null`. A closed object requires a
  `report` pin and its original absolute `nativeSourceRoot`, even when another
  group is pending.
- A complete Unit campaign may add `disable-mix-mutants: true` to its native
  configuration. With that Boolean option, its `additional-timeout` may also be
  the exact integer `300000` or `4500000` instead of the maintained `10000`. Every other
  setting must equal the maintained Unit configuration, including its complete
  source scope and test filter. This isolates mutant execution and permits a
  longer bounded host run; it grants no kill credit and does not qualify an
  aborted or incomplete host. The larger Unit budget accounts for the reviewed
  complete cohort's startup and cleanup watchdog sum; it promises no settlement.
- A complete Pipes campaign may use `disable-mix-mutants: true` together with
  exactly `additional-timeout: 2100000`. It must preserve the complete maintained
  source/cohort configuration, coverage off and every other setting. This budget
  accounts for the serialized receiving watchdog sum. The maintained budget
  preimage must remain the exact integer `10000` for Unit/Pipes and `750000` for ASP.NET; matching
  rewritten or incorrectly typed maintained/native budgets cannot self-approve.
  Unknown/cross-group values, missing/non-Boolean mixing options and additional
  scope/filter/tool changes are rejected. ASP.NET still requires exact maintained
  equality. These configuration allowances supply no host, assertion or release
  proof. The [execution repair record](../../../../../../../planning/healthcheck/evidence/HC12/execution-repair-2026-10-23/README.md)
  links the separate Unit/Pipes source budgets and actual executions. A finite
  range or reduced test filter cannot enter as a complete campaign.
- Each configured Ubuntu partition may optionally pin a direct `kernelManifest` with schema
  `cis.healthcheck.native-kernel-inventory/v1-development`. It inventories every
  original `actual-process.<PID>` trace, pins the physical dotnet executable and
  records its original native path plus the physical/native log-root mapping.
  A complete process/thread creation chain and natural exit0 may replace only
  an absent standard discovery-host exit callback. Bad or duplicate callbacks,
  incomplete discovery, and execution-host exits retain their existing checks.
  The receipt records a separate kernel discovery proof, with no loaded-image
  claim or mutation outcome credit.
- The explicit `cis.healthcheck.native-kernel-inventory/v2-process-projection`
  variant adds exactly one `projection` object to the v1 inventory. Its exact
  fields are `sourceRoot`, `nativeRoot`, `results`, `sink`, `capture`,
  `sourceManifest` and `origins`. The five document fields are physical file
  pins, not supplied validation flags. The reader binds their original roles,
  validates every compressed primary member and both full raw-stream passes,
  and independently regenerates every derived process byte and selected-origin
  index row. Only the fixed reviewed, completed producer profiles are accepted;
  incomplete, forced, mixed, repinned or extra inputs fail closed. Invalid v2
  input never falls back to v1. The typed result can supply the same standard
  discovery-only natural-exit proof as v1, retaining its verified process-origin
  provenance. It adds no execution-host normality, kill credit, loaded-image or
  whole-IPC claim. The actual closed pilot and rejection controls are recorded
  in the [Capture72 evidence](../../../../../../../planning/healthcheck/evidence/HC12/execution-repair-2026-10-23/capture72/README.md).
  This projected v2 format remains Unit-only because its reviewed producer
  profiles are Unit captures. Pipes/ASP.NET accept the original direct v1 trace
  inventory; no projected transport producer is approved by this routing change.
- A separately reviewed completed direct assertion before a later host abort may
  supply credit only through the maintained source-bound catalog. The initial
  entry is exact original Unit18 definition296 and its pinned synchronous
  enum-validation test. The reader checks its report/source/body/GUID/selector,
  lifecycle and result chronology, exact assertion payload, completion statistics
  and parent exit. A GUID cannot be both returned and still active. The receipt
  exposes `completedBeforeAbortKilledIds` separately and composes its credit once
  with normal-host categories. Host normality, abort/incomplete/global issues,
  raw statuses, denominator and the 90% threshold remain unchanged. This is not
  a general crash or timeout allowance, and it accepts no supplied approval flag.
- `proposals`: pins for proposed disposition evidence. They are retained as
  **unapplied proposals**. This reader does not accept approval booleans or manual
  adjustment files.
- Optional `dispositions`: pins for strict
  `cis.healthcheck.source-bound-disposition/v1` records. An exact, source-reviewed
  C# catalog entry must bind the review ID, kind, OS lane, original report hash,
  full definition tuple, production/contract closure and independently authored
  review evidence. Every physical pin is rechecked. Supported kinds are
  `Equivalent` and `ReviewedNoncritical`; unimplemented rerun/termination proof
  modes are rejected. The historical catalog retains Ubuntu ASP.NET24b200,
  a setup-error wording change, and seven separately reviewed continuation-policy
  survivors230/241/245/255/260/267/274. These noncritical decisions disclose real
  scheduling/progress differences and retain every denominator entry with zero
  numerator. An adopted-contract violation reopens the corresponding decision.
  A separate current ASP93 catalog binds its completed original report
  c532d6558aae3c9231f9bd7dee57c32c1682900107c84d6ee8b2365772ff9c16
  and all eight current Survived tuples200/230/241/245/255/260/267/274 to
  fresh primary and independently authored peer reviews plus the current eight-file
  production/options/contract/fixture closure. These ReviewedNoncritical entries
  retain all eight denominator contributions and supply zero numerator credit.
  They are not equivalent and do not waive supported context/cancellation behavior.
  Historical24b entries remain unchanged; neither report's record applies to the
  other report, even where source identities match. No full-union qualification
  or native-integrity waiver follows from catalog binding alone.
  The additional Unit/Pipes equivalent catalogs bind exact original report,
  source and caller/contract premises. Their earlier accepted exclusions do not
  transfer automatically to a fresh report, changed source or another OS lane.
  Pending unions pin and validate these records but apply none. Put each
  immutable record/evidence collection in a directory separate from output.
- Optional `assertionReruns`: pins for strict
  `cis.healthcheck.finite-assertion-rerun/v1-development` records. The separate
  finite reader supports Ubuntu Unit18 definition1896 and its original
  finite22b selected-source run. It checks the original definition,
  complete same-file definition inventory, relaxed timeout/configuration delta,
  exact two-case initial/fresh cohort, normal native diagnostics and the exact
  Retired-versus-Reserved direct assertion. Actual backup/source/test bodies,
  original copy inventory and chronology, recovered capture-role command and a
  separately timestamped current native-body observation are also required.
  Missing role/current-observation evidence remains a proof deficit. Historical
  loaded-module or immediate post-fresh binary sampling is not inferred. A
  complete proof can add one `AssertionKill` with unchanged denominator; the
  original Timeout and every unrelated/global issue remain visible. Pending
  unions validate physical inputs, apply none and emit no aggregate. Finite
  compile errors/ignored definitions never become a fourth partition. The
  alternate-JIT lifetime campaigns and other reruns require separate evidence
  contracts and cannot enter by changing a supplied ID or approval flag.
  A second exact profile uses schema
  `cis.healthcheck.finite-cancellation-rerun/v1-development` for Unit18
  definition1904 and the original Ubuntu finite29 selected-source run. It binds
  both declared direct assertion GUIDs, the complete330-definition file,
  three-case initial/fresh cohort, original pre-fresh capture, ten actual
  post-fresh bodies, full kernel/log inventories, and distinct launcher/monitor
  roles. An incomplete capture remains a proof deficit. The first actual strict
  replay rejects a stale pre-fresh test-output Core PDB; that mismatch and the
  failed replay remain retained. The independently reviewed correction accepts
  only that exact run's old/new PDB hashes, verifies five actual PE/portable-PDB
  content-ID and SHA256-checksum bindings, keeps the old symbols explicitly
  untrusted, and requires all nine other corresponding roles unchanged. It also
  fixes the original process/TRX/source-check artifact hashes. No equality of
  all eleven bodies or historical loaded symbols is claimed. Neither profile
  can make the original aborted host normal or waive unrelated/global
  integrity failures.

A file pin is `{ "path": "...", "sha256": "<lowercase SHA-256>", "bytes": 123 }`.
Relative paths resolve against the repository. Immutable imported evidence can
have absolute local paths. Report file keys must still match the declared
original native source root, without basename matching or path traversal.

When all groups close, each partition additionally requires:

- Pins `strykerCore` (the exact selected Stryker 4.16.0 implementation),
  `nativeExit` (the original exit-code file), and `objectModel` (actual retained
  VSTest ObjectModel metadata; read without loading executable code).
- A `logsManifest` pin. Its schema is
  `cis.healthcheck.native-log-inventory/v1-development`, with `root` and `files`
  containing relative file pins. Every physical file under that root must be
  inventoried, including controller/host `.txt` diagnostics. Inventories and
  hashes are checked before and after validation.
- Optional direct-v1 `kernelManifest` for standard Ubuntu discovery when VSTest omits its
  asynchronous host-exit callback. The schema is
  `cis.healthcheck.native-kernel-inventory/v1-development`, with `root`,
  `nativeLogRoot`, `dotnet` (`file` pin and `nativePath`) and `files` containing
  every top-level `actual-process.<pid>` trace pin. The reader checks the exact
  executable arguments, diagnostic paths, requested assembly, process creation,
  controller thread ancestry and complete natural exits from the original
  traces. This supplies separately reported OS-exit evidence for standard
  discovery only. It cannot override a bad callback, missing protocol completion
  or an execution abort, and adds no mutation credit.
- A `restorationManifest` pin with schema
  `cis.healthcheck.mutation-restoration/v1-development`, `actualBackup` and
  `preFreshObservation` and `nativeCaptureInventory` pins, `actualBackupOrigin`
  (its native absolute path), `binaries` pairs (`role`, `original`, `restored`),
  `sources` entries (`sourcePath`, `file`), and `baselines` entries (`family`,
  `initial`, `fresh`, `initialExit`, `freshExit`). Source/test Core bodies must equal the actual retained
  `.stryker-unchanged` backup. The original observation must precede the fresh
  TRX start; initial/fresh GUID and method sets must equal the complete original
  report cohort. Native capture inventory entries use the original `sha256sum`
  format and must include the backup, pre-fresh marker and role-specific native
  source/test Core paths. Core metadata is checked without loading code. A
  passing subset cannot substitute for the original test cohort.
- Original pre-fresh markers may retain the legacy plain UTC or rich
  `observedBeforeFreshUtc` spelling. The complete79 controllers instead write
  `observedUtc` with one of two exact source-proven five-field shapes: Unit uses
  `actualNativeCoreDllRestorationVerified`, `explicitControllerPdbCorrection`
  and `historicalLoadedSymbolClaim`; Pipes/ASP.NET use
  `nativeCoreDllRestorationVerified`, `explicitControllerSymbolRepair` and
  `historicalLoadedSymbolsClaim`. Each also includes integer `nativeExit` 0/2.
  Both timestamp spellings together, altered fixed fields/types and extra fields
  reject. These booleans identify the writer format; actual body/MVID,
  original backup, source and strict marker-before-fresh checks still establish
  restoration. The original marker's physical hash must bind its exact native
  `<run>/before-fresh-marker.json` origin, derived from the native Core root.
  Never rewrite an original marker to make a receiving format match.
- A controller capture may require a post-closure address index derived from
  its original backup metadata and phase-specific `roles.json` observations.
  Label that index and any scalar exit projection from original `process.json`
  as derived now. Their creation time supplies no pre-fresh proof. Preserve the
  original metadata/body/process pins, use distinct pre-repair and after-fresh
  Core copies, and reject conflicting origin hashes. An explicit PDB correction
  is separate from native DLL restoration. The v1 inventory reader checks
  origin/hash membership; it does not prove when an address index was created.
- Pipes/ASP.NET require `childSession`, a pin for actual original child receipts.
  The maintained child auditor runs again against their original paths and
  retained stage bodies. An imported success flag cannot replace this audit.
  Run where those paths exist; missing paths fail closed.

Raw statuses and native detection percentages remain separate from qualified
kills. Credit requires an exact reported GUID and selector, a complete normal
host with actual OS exit 0, and either a direct assertion or a reviewed semantic
failure from the fixed C# catalogs. Unit18 mutant312's changed guard rejects the
contract's permitted32-element measurement map. Fifty-eight reviewed Unit18
definitions remove available or mandatory JSON fields from a real service
snapshot:58 HTTP definition observations and two separately reviewed pipe GUID
observations for3255/3288. Their required-field observations bind the exact
receiving GUID, original report, full definition/killedBy tuple, embedded
production/test source, exact full native stack hash and receiving/framework
frames. Other missing-key exceptions are uncredited. These checks do not admit
arbitrary Core exceptions or fabricated snapshot inputs. Direct assertions and
reviewed semantic failures remain separately reported. Raw Timeouts add no
numerator credit without a separately validated assertion rerun.

The exact original Unit18 selector1904 aborted session has a separately pinned
diagnostic-ownership review. Its46 pre-completion results,43 failed killedBy
associations, one late failed result and one late active case remain retained.
The reviewed malformed tail can become an owned unqualified issue; it never
adds a normal host or kill. Unknown, ambiguous or different malformed tails
remain global integrity failures. A separate valid assertion rerun is required
to resolve that definition's owned failures.
Compile errors and native ignored blocks remain visible, with no invented
denominator exclusions. Unresolved survivors, uncovered definitions, runtime
errors and integrity failures prevent qualification.

Direct MSTest collection assertions use the older `CollectionAssert.AreEqual`
and `CollectionAssert.AreEquivalent` failure messages. These require the exact
declared receiving method, a first assertion-origin frame in its class/source
file with a positive PDB line, and the final MSTest adapter execution frame.
Exception wrappers, unrelated origins, cleanup, initialization and deadline
failures cannot qualify through these messages. Physical input/output paths
reject symbolic links and directory junctions, including existing ancestors.

Receiving tests use explicitly synthetic diagnostics to test rejection rules.
Those tests are not native mutation or release evidence. The HC12 evidence log
records actual runs and outstanding inputs.
