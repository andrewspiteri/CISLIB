# Unit194 first MixedPID diagnosis, closed metadata only

The single read-only metadata entry completed with exit 0. The exact original fact-reader condition is unchanged: this derivative adds only path, line, extent, byte-offset and expected/actual PID context to its existing exception. Original campaign flags and failed diagnostic receipt `fa60056d13ae5c370ff45f36755df6a7fb929716e3b27f97ec67b430047900c3` remain unchanged. There was no budget, native audit, capture review, subject or campaign entry.

The complete original receiving anchor is `unit194-closed-receiving01/manifest01.json` SHA `6699299f5bd721515f38b404623ac0a540baacc8216545247965ea99d0492729`, with Windows receipt `f575d9cb1c9a88960db2c156af7308839c4c4e553599c2c8068cf855f3384380` (64,347 files, 6,088,950,221 bytes, no extras). All selected original bodies were checked before and after this entry. The nine declared logical parents are eight Runner paths and one TestDiscoverer path; they were derived from that inventory.

Eight parent scans completed their fact-reader stage. Only `Runner 6-log.txt` rejected. This is not a statement that the other parents have complete normal capture. The exact exception is at logical line 72,767, byte 215,124,772, active extent index 1 / byte 125: expected PID 2784012, actual PID 2879622. The preceding retained row at logical line 72,766 / active byte 0 is:

```text
2879622, 1, 2026/10/26, 01:55:27.083, 408515326213338, vstest.console.dll, Version: 18.7.0 Current process architecture: X64
```

The row lacks the `TpTrace` prefix required by the existing header grammar. Its raw bytes were neither changed nor reconstructed. The following complete prefix belongs to PID 2879622, so the reader's unchanged MixedPID guard rejects it. The old backup is 215,124,647 bytes / SHA `12cac95c6630b4c794dfcf9a264c3b1e812a917a5c45417f61c663b90c77d2cb`; the active file is 128,548,137 bytes / SHA `f5b0ff3480ebdae3ab2f247d074261b8879ad2a755e34758024d9b647477aa8c`.

The two independently retained kernel bodies show sequential console processes with the same original `/diag:.../Runner 6-log.txt;tracelevel=Verbose`, same console path and `/parentprocessid:2780469`, but distinct ports. PID 2784012 successfully execs at 1792975910.270345 and is killed by SIGABRT at 1792979726.064305. PID 2879622 successfully execs at 1792979726.195572 and exits 1 at 1792981097.108826. The interval between the old terminal and new exec is 0.131267 seconds. Kernel pins are `168e3a532e12d1c3153d468efdf706d11dfc737f079764ee6ccf71d312225aba` and `39daa8d54a7b42b7838f0449b1205daac84d5194f60fdaa39651b7ae21f864b8`. This establishes those selected process records and temporal order; it does not explain why the header prefix is absent, establish full role closure, or make either abnormal terminal normal.

There is a second independent source restriction: even a complete second Runner header would fail the current fact reader's final rule, which permits shared parent paths only for the exact two-family discoverer. The maintained native controller parser also routes a PID change through strict sequential-discovery closure. Any proposed Runner support therefore needs an explicit source-reviewed epoch route, not a prefixed copy or a generic fallback.

Exact local closure pins:

| Artifact | SHA256 |
| --- | --- |
| `Diagnose.cs` | `c72059b1cc695409b253be9ba50306957a58d84045e285426fc5751aa6925489` |
| Frozen original fact-reader preimage | `ed839ac0818762b9fa81b5947d227c52b4975bb8f07a91e282ea5ca0efdd78e3` |
| Context-only fact reader | `fc0f025db0a5813af1cb8527e6baea801225e1a66e189b63543fd29c6beaa126` |
| DLL / PDB | `4c42c72387b739139f953a3c6641a69d1f74753d0880b452af66a7038b7a9367` / `1a2f3a227b761a46f8529027cf643cec244896b04d3f94d24977dc41f1fcf6cb` |
| Seven-document compiled-source binding | `4cf54c536dfc05b71f73b906a5d893c81ab73ec5fa101fa2f55e7970d6eeb7c0` |
| `actual01/receipt.json` | `fa833d5c1ea28b11850346bac75359c9529d27d85eb22e79f55a49cded2b0cee` |
| `actual01/Runner_6-log.txt.json` | `93da1a4e3efd46ab8a6240ff15f89ebb1786917895cf5c878637c992a4492134` |

Strict SDK10 build01 and entry01 both closed 0. The unchanged extent reader is `2bf3ff801cf8981855ac99782a0a401c21f28f20e2e753fd8369da119c2fdaad`; the unchanged exec parser is `347ffd2d20ffc0f962fea4517083683b067be361ff769250124bc8c576213570`. This root is now frozen. Qualification and applied mutation credit are false / zero.
