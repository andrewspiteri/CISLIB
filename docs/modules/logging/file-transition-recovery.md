---
title: 'Interrupted file transitions'
type: document
status: unknown
cis:
  stable_id: cislib.document.modules.logging.file.transition.recovery
---

# Interrupted file transitions

Rotation and partial recovery publish schema-2 intent before any retirement. It names the exact old/new inventories, original active bytes and planned retirement IDs/names/lengths/hashes. Restart validates all surviving members before resuming only that exact retirement/rename/publication/fresh-active sequence. The oldest planned prefix may be partly or wholly absent; a missing survivor or changed retirement still fails. Legacy schema-1 intent retains its already-retired-prefix rule. A full retention group can recover by retiring its oldest owned member and preserving the complete partial active as damaged.

Each metadata kind uses one private candidate slot; a failed creating attempt removes only its own candidate. Failed cleanup leaves a blocking slot, rather than generating unbounded files. Occupied/unrelated candidates are preserved. Committed recovery may complete exact recorded retirement; pre-commit rollback still preserves existing members. Unproven pre-intent gaps and ambiguous states fail without guessing.

The [current review addendum](review-contract-2026-10-15.md) defines resumable states, safe categories and capacity/lifecycle rules. This cooperative protocol does not promise power-loss durability, hostile-race resistance or exactly-once delivery.
