# Git-root alias inventory correction

17 October 2026, based on `19f4b9c9`. Fixes **INVENTORY-ALIAS** from the [approved findings](../../reviews/corrective-approved-2026-10-17/findings.md).

The checker now uses Git's empty repository-relative prefix to recognize the work-tree root. It no longer compares the caller's spelling with Git's canonical absolute path. Nested directories still use copy enumeration; invalid repositories/indexes still fail. No new path-resolution framework or dependency is introduced.

The new C# regression creates its own Git root and sibling alias, both with spaces. It stages two files, adds nonignored/ignored untracked files, compares complete path/hash records through both root spellings and verifies unchanged Git index bytes. Windows uses an owned junction; Ubuntu uses an owned symbolic link. Cleanup deletes the link without recursion before disposing the owned fixture directory.

Before the fix, the Ubuntu regression failed because the alias included `ignored.txt`. [Native pre-fix TRX](ubuntu-before-fix.trx). After the fix, **all seven SourceInventoryTests pass on each native lane**, zero failures/skips, including copy/nested roots, corrupt-index rejection, actual checker command JSON and fresh PowerShell bridge bootstrap. Windows `7f67d8351e204779ae2cc2ac99b8cb0b`; final Ubuntu `b8cd13e345414d65979b289d6de396fc`. [Windows](windows-inventory.json), [Ubuntu](ubuntu-inventory.json).

The required inventory group now binds all seven methods with a total minimum of seven on both lanes. The [C# audit](RetainInventoryAliasEvidence.cs) checks the intended red failure, complete native results, required binding count and matching final code/test/inventory byte hashes in both workspaces. [Audit](audit.json). Formatting passes for the two C# files.

This focused tool correction follows the earlier 6,460-case Windows Developer checkpoint; it is not a repeated full checkpoint, complete Ubuntu qualification or release gate. Ambient Git environment isolation and unsupported link/sparse-input policies remain the previously recorded bounded questions. The original roadmap goal remains paused; the next confirmed correction is REJECTION-REASONS.
