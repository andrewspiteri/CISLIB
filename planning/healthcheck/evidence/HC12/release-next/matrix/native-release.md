# Native Release v2: passed execution, retained release refusal

Windows `release-windows-02` and native Ubuntu 24.04 x64 `release-linux-02`
completed all 17 execution stages successfully. Both then returned **exit 2 from
final validation**, with the same 26 outstanding issues. Neither is a qualified
release. The original first-attempt compatibility failures and their different
inputs remain retained; these corrected runs do not replace those receipts.

| Native run | Run ID | Started UTC | Finished UTC |
| --- | --- | --- | --- |
| Windows v2 | `f2960d12e82b4206a95fb565cec2ae55` | 2026-10-21 13:03:29.2859503 | 2026-10-21 13:39:46.2958503 |
| Ubuntu v2 | `7b4374c4baea4185b653e338f8708f31` | 2026-10-21 13:04:21.11133 | 2026-10-21 13:30:50.777986 |

The successful stages are locked restore, project boundaries, format, build,
startup contract, compatibility samples, log-value consumers, publication ledger,
pack, package boundaries, architecture inspection, unit, integration, architecture,
compatibility, business and host. Each stage records `Passed` and exit 0. Both
architecture inspections cover 32 projects, 38 target graphs and two packaged
assemblies. The mutation family remains `NotRun`; this profile does not dispatch
the full mutation matrix.

## Native project-backed family counts

These counters come from each actual family TRX, without summing separate
contract/example observations or package-consumer rows into MSTest totals.

| Family | Windows passed / total | Ubuntu passed / total |
| --- | ---: | ---: |
| Unit | 4,561 / 4,561 | 4,561 / 4,561 |
| Integration | 2,265 / 2,265 | 2,267 / 2,267 |
| Architecture | 1,132 / 1,132 | 1,132 / 1,132 |
| Compatibility | 248 / 248 | 248 / 248 |
| Business | 29 / 29 | 29 / 29 |
| Five-family total | **8,235 / 8,235** | **8,237 / 8,237** |

Both lanes have zero failed and zero not-executed TRX cases. The summaries retain
their existing `foundation-only` family scope labels; successful project-backed
execution does not close unexecuted policy/release matrices. The full 248-case
compatibility family includes the scheduling control and the unchanged 127
Healthcheck cases. It is distinct from the earlier 247-case focused filter that
omitted scheduling.

The two-case integration count difference is outside Healthcheck. Each lane runs
its corresponding `CFG-LOG-FILE-PATH-*` row. Ubuntu additionally discovers and
passes `JsonlInputSelectorTests.OriginalSelectorCorruptionsAreRejectedAfterPositive`
with `symlink-member`, and
`FileRotationPolicyProcessTests.RealConsumersExposeTerminalPolicyFailureAndHealthyOutput`
with `write-jsonl-os-limit`. The former source filters the row when Windows link
privilege is unavailable; the latter explicitly adds it on Linux. The Windows
symlink control is therefore absent from discovery, rather than a skipped test.
Zero TRX skips must not imply that control executed on Windows.

## Final validator refusals

Each validation report contains exactly:

| Code | Rows | Meaning |
| --- | ---: | --- |
| `MissingFamilyExecution` | 1 | Full mutation family was not executed by the profile. |
| `MissingReleaseGate` | 8 | `runtime`, `package-suites`, `coverage`, `mutation`, `history`, `examples`, `lanes`, `bundle`. |
| `MissingRequiredCase` | 17 | Nine unique planned cases; eight appear at both family and global reconciliation levels. |

The nine unique planned IDs are `MUT-FULL-CORE`, `ARCH-PUBLIC-TYPES`,
`ARCH-IMMUTABLE-CONFIG`, `COMPAT-API`, `COMPAT-SOURCE`, `COMPAT-BINARY`,
`COMPAT-CONFIG-WIRE-BEHAVIOR`, `BC-009` and `BC-010`. `MUT-FULL-CORE` appears only
globally because its family did not execute; the other eight appear twice.
There are no failed-stage or failed-native-case issues in these v2 reports.
Summary and validation issue arrays agree exactly on each lane.

These are the existing global single-package Core obligations documented in
[the gate inventory](gate-inventory.json). They are preserved refusals, not
Healthcheck runtime failures, and running this authorized profile does not resume
the paused logging roadmap or authorize removing its gates. Failed broad
Healthcheck automatic mutation, compiler/ignored review and child-origin closure
remain additional concrete work, as recorded in
[the unit-runtime comparison](unit-runtime-native.md).

## Base commit and frozen harness overlay

Both summaries record base commit
`8fc2f9ccb78ba948f9794cfb4c0ea37cfd2956eb`. The architecture producer explicitly
states that repository HEAD alone does not attest working-tree source identity.
These v2 runs use that base plus the frozen harness overlay; they are not clean
replays of the unmodified commit.

The frozen overlay manifest is
`.artifacts/healthcheck/release-next/v2-inputs-01/source-inputs.json`, file SHA-256
`1ce7051f2e0f85b383d00c61b3d286ae474b2033ac857a0b05a9044fbba085e3`,
with selected-input digest
`e5b2307a2795edadd63d1d68cc025b38ca82dcea204e1677e2e736df3d8ded39`.
It contains 1,326 selected paths. Both native Git-based inventories match all
1,275 non-cache entries, including all 981 non-cache C#-implementation entries.
The other 51 overlay entries are generated `__pycache__/*.pyc` files absent from
the Git-based native inventory: repository `.gitignore` ignores them, and
`SourceInventory.CaptureAsync` uses cached/untracked paths with
`--exclude-standard`. This is a difference in inventory scope, not 51 missing
maintained source files. No cache-file execution credit is inferred.

Each native full-source inventory contains 51,950 path/SHA-256 pairs. Comparing
those pairs between platforms yields zero missing paths and zero changed hashes.
Their JSON file hashes differ; the data entries agree exactly. The summary's
source-inventory pin, architecture-inspection pin and discovery-reconciliation pin
all rehash correctly on both lanes.

The overlay includes the three compatibility dependency-origin fixes, seven HTTP
detector cases/cache-header assertions and two unit cancellation-resource cases.
The exact v2 HTTP test-source hash is
`0405ee4a239f0206664d83bde001b27555b2d2f1c27958879d72e5a52ac54993`;
the cancellation-resource source hash is
`10c029f57404539b2115625076316dba5486488254d3fb8bea2be471741ae778`.
The later v3/v4 helper/HTTP-timeout freeze and its native proofs have separate
inputs and results. No later detector or propagation outcome is credited to
these v2 profiles.

## Actual native packages are separate candidates

The single package remains `CIS.Core.0.1.0-dev.1.nupkg`. Its unchanged version text
does not establish byte equality. The architecture/package stages checked each
native package's Core/helper entries against that lane's actual Release outputs.

| Origin | Package SHA-256 | Packaged Core SHA-256 |
| --- | --- | --- |
| Sealed clean Windows producer used by the separate package consumers | `37961c7cd75a7c7e5af47087496f087c5f50ade228c9616be0cfc251e49bcdea` | `039007b8920929bb62e096590789325f874ec5cac969f2d5a95f29a31967e980` |
| Windows Release v2 | `4d2e1c62cc76b263100b3f600d85bfaec18953976be20cc7c2e5a754fdb99fc5` | `d63e22271a4b1c80918cc20b3a368fd0afd2a6cde72d4071b7e7c0c7d862ebdf` |
| Ubuntu Release v2 | `b48704586dc7270ae29d055ccde68762209e7e960de87387bb052a57ca93280e` | `6d76659386ab71a363ed2c5b800a6c152c430573190090f2c5d3c45ddd17ff23` |

These hashes all differ. The v2 project-backed suites and package-boundary checks
must retain their own native binary origins; they cannot be relabeled execution
of the sealed `37961...`/`039007...` package. Conversely, the separately retained
25-row package-consumer observations on each OS exercise the exact sealed
producer bytes and do not automatically qualify either v2 native package.

The sealed producer provenance is
`.artifacts/healthcheck/release-next/candidate-8fc-windows-01/provenance.json`,
SHA-256 `040c2857c61444cc516383155a5863317167019bbc423c9c9a951eb88a8e6bf0`.
Both native inventories match 153 of its 154 selected producer inputs. The sole
raw difference is `NuGet.config`: producer
`0adcf51637dc2e738d70fd3fe5264e34ba67fdd95a709270587fbf8b574826e5`,
v2 native `0192b30549bf113c82874e5f34b0099e8f7b2c6fcb98d0660f3ce81fc5f1ce00`.
The actual producer/v2 decoded configuration texts match after CRLF-to-LF
normalization. That observation preserves the distinct raw input identities; it
does not make the resulting packages or Core DLLs byte-identical.

## Primary artifact pins

The following paths are relative to
`.artifacts/healthcheck/release-next/<native-run>/`. Hashes identify the actual
retained file bytes. Each native summary also pins its five family TRX files.

| Artifact | Windows `release-windows-02` SHA-256 | Ubuntu `release-linux-02` SHA-256 |
| --- | --- | --- |
| `summary.json` | `e1c93f5b5a976dbd80407a83b5b0fe42557bd7b8c06bdab295cdb0b6094c2388` | `ba24410c3e6cefbfc7f9839ff8887500aef46a3602e5e9d92f497fd2ee1c9b8b` |
| `validation.json` | `b5c0b3d869878c1f7d722e1dbc37164ede0825252c035ff97a75a960265edae4` | `7f66566082415e4d52f187e49957100a20161039692af256f4c4ef9f28a895f4` |
| `source-inputs.json` | `ec3a0b10f7bed38fc4b76ac24f2488973c872b3e16c8ae5891470c5e47ba691d` | `014bb39389a0f4e653c0399b9920e93782823804bc47805e77728c7234ab602f` |
| `discovery-reconciliation.json` | `9aa1033f9b3e1b59d50cb9d9d4a112ece89523dbfeb2fe28742e9b58d9304ae7` | `6a9531665fcdedafe00db8840b521a3b3e2ce3981af225f4d044a7dc923596cf` |
| `architecture/<runId>/inspection.json` | `b60edb4d9b7fd4f2b927e726ae2e8d034351ddeeaa9cbd06c3b52a5f6f38c467` | `ed31f6fd1022208ec7c9ede8403b83185ae55ea70eeef51739b8691d347effa8` |

The audited TRX paths are `families/{unit,integration,architecture,compatibility,business}/report.trx`.
Each rehashes to its summary reference. Native case arrays were not rewritten or
merged. The inspector's package/Core hashes above are the retained native
producer observations; the original archives, binaries, logs and raw native
reports remain coordinator-owned evidence.

The unchanged final validator refuses release qualification. Full shipping
coverage/mutation, exact-package cumulative suites and examples, required
external integrations, retained kit/licence and publication-history obligations
remain open according to their existing policies. The actual publication ledger
is empty; these runs create neither a released Healthcheck baseline nor a
historical replay. No publication is authorized or claimed.
