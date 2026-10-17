# Coverage input and control corrections

17 October 2026, based on 7ce99c28. Addresses continuation 18-F1–F4 and 17-F6.

The existing coverage entry now reuses the repository source inventory to pin shared conformance/contracts, build/tool configuration and additions/deletions during a run. It also pins the consumed successful Developer summary, hash-checked inspection/package, startup summary/reference assembly, and the actual Release summarizer DLL. The summarizer executes those pinned bytes directly; it is no longer rebuilt after the final hash check. Existing no-build test/shipping/copy-local pins remain. A nonempty focused-inspection override is rejected before report creation. Focused ownership and allowlist readers now honor the same override. Checkpoint override rejection remains unchanged.

The duplicate-point and invalid-branch-count C# controls require their intended reasons. Coverage exclusions refer to the enumerated declarations without a stale hard-coded count. Focused-test guidance says at most one selector, identifies the dedicated compatibility runtime prerequisite accurately, and describes both inspection readers.

Both native lanes pass 29 ShippingCoverageTests and 44 ArchitectureOwnershipTests, zero failures/skips. The ownership selection explicitly supplied the same existing lane inspection path; it does not qualify a new inspection or package. Ubuntu initially exposed ANSI/wrapped error formatting in the new guard assertion; its stable diagnostic prefix is now used and the complete selection passes.

These tests verify parser/override behavior and the existing ownership consumers. A full fresh instrumented coverage collection was not run for this batch. The expanded orchestration pins still need that end-to-end run; no coverage percentage or threshold qualification is claimed. The original roadmap remains paused.
