# RM-02 storage execution hold

**Resolved:** Andrew Spiteri approved this exact alternative with the answer
“Approve 6 GiB and exact fixture cleanup.” CIS recorded the amended outcome while
preserving the original decision in its authorization history. The original
6 GiB ceiling now has an applied RM-02 finite allocation.

Both approved roots were removed on 20 November 2026 after idle checks across
Windows, Ubuntu and Docker WSL and a retained 5,580-file SHA-256 manifest.
Manifest SHA-256: `0ce5eaad473b473ad80010a944d45b681752d171d8a85534914f08b8cc697f9a`.
Actual reclaimed bytes: **4,206,421,532**. Receipts remain in ignored
`.cis/local/transactions-intake/fixture-cleanup-{manifest,receipt}.json` and
`fixture-idle-check.json`; no other historical material was removed.
The cleanup command's timestamp parsing guard produced a nonterminating
PowerShell conversion error. Independent inspection of the retained UTC receipts
confirmed completion 75.783 seconds after the idle check, within its 180-second
window. Absolute-path, ancestry/reparse and exact inventory checks passed.
Subsequent invocation scripts use terminating error handling.

The proposal below preserves the concrete basis for that decision.

Andrew Spiteri approved the RM-02 profile, five-task plan and shared 24 active
hours / 28 attempts, including 2,700 restoration seconds, on 20 November 2026.
The approval also raised the physical storage ceiling from 6 to 8 GiB. CIS
recorded that outcome decision, but allocation was rejected without applying it.

The installed CIS build fixes the original byte limit across every retained
outcome sharing a storage reference. Its outcome amendment path supports only an
unshared ceiling. No supported shared-ceiling amendment command was found.
Changing the reference, editing the ledger or reopening completed tasks would
not implement the approved decision legitimately. No such workaround was used.

## Proposed bounded alternative

Retain the operational **6 GiB (6,442,450,944-byte)** shared ceiling and its
**64 MiB (67,108,864-byte)** restoration reserve. Keep the entire approved
**1 GiB Docker allowance inside that ceiling**. All time, attempt, restoration,
dependency, product-scope and ownership limits remain unchanged.

Authorize cleanup of only these two generated temporary fixture roots:

| Absolute root | Observed bytes | Files | GUID child directories |
| --- | ---: | ---: | ---: |
| `C:/miscwork/portfolio/cislib/implementations/csharp/tests/CIS.Core.UnitTests/bin/Debug/net10.0/cis-type-manager-fresh` | 406,215,058 | 570 | 152 |
| `C:/miscwork/portfolio/cislib/implementations/csharp/tests/CIS.Core.UnitTests/bin/Release/net10.0/cis-type-manager-fresh` | 3,800,206,474 | 5,010 | 1,336 |

Total observed reclaimable size: **4,206,421,532 bytes (about 3.92 GiB)**.
Both roots contain only DLLs and generated JSON assembly inventories; neither
contains reparse points. The producer is
`implementations/csharp/tests/CIS.Core.UnitTests/TypeManagerFreshAssemblyTests.cs`.
Its initialization creates a fresh GUID directory, copies the test assembly as
`Host.dll`, emits fixture assemblies and writes the selection inventory. Its
existing comment explicitly permits removing `cis-type-manager-fresh` after all
test hosts using the files have exited. These are accumulated native test inputs,
not test-result reports. No exact references to these roots were found in the
tracked change/planning records or the work-accounting records searched.

Before deletion, resolve and verify both absolute roots remain beneath the
specified unit-test output directory, recheck for reparse points, retain a
file manifest with hashes and measured sizes outside these roots, and verify
that no Windows or Linux test host is using them. A live or uncertain test host,
unexpected content, protected claim or changed ownership holds that target.
Do not stop another user's test process to make cleanup possible.

Delete only the verified targets using native PowerShell literal-path operations.
Preserve source, user launch settings, current test binaries outside those roots,
TRX/results, logs, candidate packages, CIS history, approvals, accounting and
recovery records. Do not prune Docker or delete any other historical material.
Record actual reclaimed bytes; cleanup grants no time or attempts and does not
make an old test result current.

After the specific storage/cleanup decision, amend the canonical outcome through
CIS, allocate its five tasks using the unchanged shared storage reference, and
inspect eligibility before native implementation work. Recount physical usage
and the Docker charge before expansion. The expected post-cleanup footprint is
about 1.82 GiB before the Docker allowance, leaving ample space within 6 GiB.

This alternative avoids changing CIS tooling. It requires a specific new user
decision because it replaces the approved 8 GiB ceiling and widens the earlier
cleanup boundary beyond newly created RM-02 scratch. Implementation has not
started; no database, Docker image/container/volume or product native test has
been created or run for RM-02.
