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
- `proposals`: pins for proposed disposition evidence. They are retained as
  **unapplied proposals**. This reader does not accept approval booleans or manual
  adjustment files.
- Optional `dispositions`: pins for strict
  `cis.healthcheck.source-bound-disposition/v1` records. An exact, source-reviewed
  C# catalog entry must bind the review ID, kind, OS lane, original report hash,
  full definition tuple, production/contract closure and independently authored
  review evidence. Every physical pin is rechecked. Supported kinds are
  `Equivalent` and `ReviewedNoncritical`; unimplemented rerun/termination proof
  modes are rejected. The maintained catalog contains Ubuntu ASP.NET24b200,
  a setup-error wording change, and seven separately reviewed continuation-policy
  survivors230/241/245/255/260/267/274. These noncritical decisions disclose real
  scheduling/progress differences and retain every denominator entry with zero
  numerator. An adopted-contract violation reopens the corresponding decision.
  There is no equivalent entry.
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
