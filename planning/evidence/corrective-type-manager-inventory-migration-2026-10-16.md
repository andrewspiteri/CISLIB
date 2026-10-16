# Native Type Manager SDK inventory matrix

16 October 2026, based on `9478e063`. Corrective Batch B changes development verification only. Shipping source, public behavior and dependencies remain unchanged. No Python execution or Claude transfer.

The existing checker now supplies type-manager-inventory, replacing the standalone Python matrix. It preserves neutral/Windows/Linux managed variants, a private content-keyed fixture feed/cache, native assets for both RIDs, reference/runtime selection, satellite resources, ordinary DLL content and actual SDK inventory generation. Each invocation checks all nine default/native/cross-RID build/publish-no-build/publish-build layouts: exact five-member membership, duplicate rejection, schema/host identity, ordinal order, selected managed asset hashes, deployed exclusion fixtures and fresh native-process behavior. Previous manifests are removed before the generating command. Optional Console/Web SDK, Debug/Release configuration and alternate target inputs remain supported.

Windows Console/Release and Web/Debug each pass all nine cases; Ubuntu independently builds those same two variants and passes all nine each. That is **18 positive cases per OS**, with all case identities, copied source inputs, native fixture hashes and verifier bytes independently reconciled. Each OS also executes one deliberate build-failure control: all three RID builds fail for CONTROL_INVENTORY_FAILURE, complete remains false and the command returns one. These are not passing positive layouts. Shared bounded process support permits recording an explicitly allowed failed build while its default remains fail-closed.

Initial Windows Console/Release attempt windows-console-release fails before any logical case because .NET File.Delete reports a missing parent directory where the former Python missing_ok continued. The port now tolerates only that missing-directory condition; the fresh windows-console-release-final run passes all nine. The failed attempt remains recorded and earns no credit. Tool build and formatting pass without warnings/errors.

Native evidence remains under `.artifacts/corrective/tm-inventory`. Positive reports:
- windows-console-release-final/results.json: SHA-256 `f5fa84161dbc85e1aef89e9cfb22847b98b41ecff50e61434b9c68c787f79018`.
- linux-console-release/results.json: `1d73284ee0d8438b636416987ea11795ae134fd232d001adf1db4a73adafb7c8`.
- windows-web-debug/results.json: `c5b681c26d68818812c81afe4343cb156556215494331c3ed3440947184ab60a`.
- linux-web-debug/results.json: `5040cd39ea17e4cecd4d4e0d7923613c5305d8485f8e3c63747a72d07c4854af`.

The compiled verifier SHA-256 is `190e37b7bceaaabbc92f1826858152fdb39f3dd55b2d95acdbeda1f60d6cf722`. Existing retained native fixture inputs are reused as test assets: Windows `838591885c5396fb0588a3aedc2f3e841d3afc67570868b1bce615264005a44c`, Linux `2420322efa34abb54e37f27ec39060cf90e3bb135fae5f91c644aac1216d8f1d`. They are not shipping candidate Core. Case/source/tool and intentional-rejection audits, original commands/output and each local fixture-package hash remain alongside the reports.

After call-site/documentation reconciliation, `tests/fixtures/TypeManager/verify-inventory-matrix.py` is retired (13,463 bytes; SHA-256 `9bae415b1879ca3636c4a146ffbbdbef8bdb1eba4fc41aa8cec90881e7ba6f13`). Git retains the original. Five maintained Python engineering utilities remain: package, examples, mutation preparation/replay and measurement. No maintained fixture Python remains; historical copies stay preserved.

Graph: schema 2, 10,884 nodes/53,186 edges, zero diagnostics, build `fdcbd1f86534fb008e84da4b383f7eaa8fbe36881098588634844d84d7515ab3`, compiler/semantic extractors 6/3. The original WriteTypeInventory root is fresh/untruncated at depth one (four nodes/four traversals, limit ten); the final exact inventory-verifier root has three nodes/three traversals with the same bounds, no proposed edges. This is bounded navigation evidence.

The earlier 6,434-case full Windows Developer checkpoint predates these utility changes. These matrix runs cover Console/Release and Web/Debug on both native OSes, not the remaining two SDK/configuration combinations, Visual Studio PublishUrl/profile copying, a shared exact Core package or full release qualification. The original logging roadmap remains paused.
