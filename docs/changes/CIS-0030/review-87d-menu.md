# Configuration row-menu owner correction

The review of87d18ba6 identified that every closed row menu called
`usePortalRead(null)`. A null URL avoids HTTP work but still registers its
protected-state owner. Two parent owners plus fifteen row owners exceeded the
existing sixteen-owner bound within the normal supported page size.

`ConfigurationRowActions` now retains only the ordinary trigger and focus
handling. Its protected reader is inside `ConfigurationOpenRowActions`, mounted
only for the selected open menu. Existing list selection admits one open menu;
Escape, switching rows, filtering, page/target changes and session changes
unmount that reader through the existing hook cleanup. The owner bound, native
authority, late-callback ownership, actions, layout and public protocol are
unchanged. No additional general reader or lifetime framework was introduced.

The new named native case
`ActualConfigurationListRetiresRowReadersAcrossTwentyNativeEntriesAndRepeatedMenus`
extends the existing fixture's trusted catalog to twenty real application
Configuration rows. Its additional grants allow only scoped definition/action
discovery. OperatorB and ordinary A/B negatives remain unchanged.

The browser continuation requires all twenty actual summaries before opening a
menu, zero closed-menu action reads, twenty mouse-open/Escape-close cycles,
twenty direct keyboard menu switches, filter retirement/restoration, and one final reopening. Every
action reply must match the exact application/definition/schema/binding. It
checks focus, at most one visible menu, exact41read requests and zero mutations.
The C# parent independently compares native catalog/source/binding/definition,
publication/release/map, Configuration originals and Audit fingerprints.

The ordinary pure test checks that sixteen registered owners still exhaust the
coordinator and an unregistered owner releases a slot. It does not replace the
component/browser regression.

The current native retry passed 1/1 with no skips. The full affected portal
pure selection passed 212/212, the production portal and scoped C# builds passed,
and final scoped formatting/source identity checks passed. Independent source
and actual-result review found no further required change. See the
[retained verification](review-87d-verification.md) for exact phases and limits.
Earlier failures and accepted source candidates remain separate.

The first native attempt rendered all twenty rows, then timed out during the
second pass because an open dropdown physically covered the next row's mouse
target. The retained failure screenshot shows that ordinary popup overlap.
The corrected second pass uses actual Enter activation on each row trigger;
the first pass still exercises mouse opening and Escape closing. Exact41native
reads, both complete sets of twenty IDs, focus, single-menu visibility, filtering
and zero-write/fingerprint assertions remain unchanged. This corrects test
interaction, not product behavior; the failed run remains failed.
