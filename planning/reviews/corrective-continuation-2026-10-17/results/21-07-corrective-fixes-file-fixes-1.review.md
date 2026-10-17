# Static review: 07-corrective-fixes-file-fixes-1

This is a read-only review of the supplied text only. I executed nothing. It covers the eight assigned files plus the bounded context. It is not a whole-change PASS and not a release qualification.

## Confirmed findings

### F-A (Medium): two selector controls are rejected for a missing file, not their intended reason

**File:** `implementations/csharp/tests/CIS.Core.IntegrationTests/JsonlInputSelectorTests.cs`, lines 54–55, with the catch at lines 83–85.

- **`traversal`** sets `segments[0].fileName = "../outside"`. Nothing is ever created at `<temp-parent>/outside`.
- **`foreign-name`** replaces the group in the damaged member's name with `ffff…`. The test never creates that file.
- **The catch** accepts any `IOException`, which includes `FileNotFoundException`. It also accepts `InvalidDataException`, `UnauthorizedAccessException`, `ArgumentException`, `InvalidOperationException` and `JsonException`. No reason is asserted.

**Concrete failure:** suppose `JsonlInputSelector` lost its path-traversal check or its same-group name check. Both controls would still pass, because opening or inspecting the absent member throws. The `missing-member` behavior masks the intended rejection.

This is the same defect class that the corrective fix closed for `inventory-bound`. That control now proves the oversized document is still valid JSON. These two controls still do not isolate their reasons.

**Fix:**
- For `traversal`, point the name at an existing in-group member through traversal, for example `"../" + Path.GetFileName(root.Path) + "/" + Path.GetFileName(damaged)`.
- For `foreign-name`, copy the damaged bytes to the foreign-named path before rewriting the inventory.
- Optionally assert a stable rejection reason, as the six other readers now do.

### F-B (Low): the policy guide still says the integration consumers cover deletion failures

**File:** `docs/modules/logging/file-policy-failures.md`, line 11 compared with new line 13.

- **Line 11 (unchanged)** says the "five integration consumers cover JSONL/Human required deletion failures".
- **Line 13 (new)** says the three native `delete-jsonl-directory`, `delete-human-directory` and `delete-jsonl-damaged-directory` modes "fail before publishing intent or invoking File.Delete". It calls them a preflight boundary, "not an attempted deletion".

The five integration consumers appear to be those three delete modes plus the two Human guards. That mapping is my inference; the fixture catalog is not supplied. If it holds, line 11 still overstates the scope that line 13 corrects. Only the `LoggingReviewProcessTests` rotation-denied/recovery-denied consumers exercise actual deletion denial.

**Fix:** reword line 11 to say "unreadable retirement-member preflight", or move it explicitly under the historical label.

### F-C (Low): the raw acquisition step skips the environment pin and identity checks

**File:** `FileInventoryProcessTests.cs`, around lines 53–54, in `StartupRejectsUnsafeInventoryBeforeModifyingExistingContent`.

The raw `ProcessRunner.RunAsync` call passes no environment override. Every other probe clears `DOTNET_SYSTEM_IO_DISABLEFILELOCKING`. The call also checks only `status`, with no fixture or Core SHA-256 and no pid check.

**Concrete failures:**
- If a parent environment sets `DOTNET_SYSTEM_IO_DISABLEFILELOCKING=1`, the Ubuntu child reports `FileOwnershipUnavailable`. The test then fails spuriously.
- That step would not detect a substituted fixture or Core.

This matters for the "shared child/parent identity checks" claim, if this consumer is counted in it.

**Fix:** pass `{ ["DOTNET_SYSTEM_IO_DISABLEFILELOCKING"] = null }` and validate the identity fields.

## Fixes checked where this bundle overlaps

I traced each corruption's control path statically. In each case, every earlier assertion holds and the intended `Require` throws first, with the expected message:

- **Acquisition:** `status`, `Core hash`, `length` and `safe error`.
- **Inventory:** `status`, `runtime`, `Core hash`, `failed inventory facts` and `safe error`.
- **Retention:** all ten controls. This includes `group`, which reaches "same group" after a well-formed GUID, and `retained-preimage` against segment 5.
- **Rotation:** all five controls.
- **Inventory holder:** it now applies `ValidateAcquired` and checks the created flag, zero length and segment 1/1.
- **Selector `inventory-bound`:** the padded document is still valid JSON and over the size bound by one byte.

The transition and boundary reason strings could not be verified, because their `*Expectations.Validate` sources are not supplied.

## Limits and missing context

These files or facts were not supplied, so I could not check them:
- `FileTransitionExpectations`, `JsonlBoundaryExpectations`, `HumanRenderingExpectations` and `FixtureProcessChecks`.
- The DiscoveryFixture modes and the `JsonlInputSelector` and `FileGroupTransition` sources.
- The case-inventory and required-case bindings, including whether `symlink-member` is bound on Windows.
- The migration record and the raw reports.

The bundle says the baseline-deleted readers are complete, but no deleted Python reader text appears in it. Parity between the Python readers and their native replacements is therefore not assessed.

The doc's statements about `FileGroupTransition.Begin` behavior, the "twenty corrective consumers" and its counts are unverified.