# Configuration versions

R09 brings saved versions, their JSON and deployment into one desktop Configuration page. Titles use immutable version notes; dates use the browser's local timezone. No sequential version number or readable human author is invented. Exact IDs, native actor references, schema and preconditions remain in collapsed Technical details.

A saved version is one complete immutable Shared/Application publication pair. The viewer labels the actually readable JSON layer; it does not merge or imply accepted runtime values. Editing an older version retains its exact Shared base, never the latest Common publication. Protected or unavailable JSON does not become an empty object.

**Edit as new version** opens local Application JSON and a version note. **Format JSON** preserves value/string tokens, member order, large numbers and escapes while changing whitespace. Drafts remain in memory within 256 KiB UTF-8. **Save version** freezes the visible intent, obtains native structural/impact admission and sequentially invokes publication and coherent release with separate original keys retained before each effect. Saving does not deploy. A partial publication is not a deployable saved version.

Existing saved JSON opens in the editor with the same readable formatting as its viewer. Subsequent typing stays exactly as entered; refresh and resizing do not reformat or replace a local draft. New Configuration input retains its original bytes through its first Save.

**Create first version** also works for an existing catalog binding whose saved
history and assignment are empty. A provisioned Shared source does not make that
binding deployed. The editor starts a deliberately new Application JSON object
from the exact empty assignment map, without copying or guessing Shared content.
Its first saved version explicitly has no Shared publication; saving still does
not deploy it. Existing saved or protected JSON is not replaced by this blank
initialization.

Incomplete Save retains the draft and original references. Recovery directly looks up the same keys, never republishes or creates a replacement key. Continuing requires the exact captured Save intent and known publication; an unrelated same-resource original cannot complete it. Commitment and current cleanup remain separate. An older completion never clears newer edits. A later explicit edit can dismiss only the exact clean completed Save references; unknown and incomplete observations remain unresolved.

**New configuration** uses one Name, JSON and version-note Save. Existing fixed generic-object setup precedes the same publication/release sequence. Each effect retains its own original. There is no normal Continue-to-publication screen. The parent retains input until the child admits the exact application/environment/schema/source/session; then the duplicate parent draft and seed are cleared. Abnormal setup/recovery stays on the same page for an explicit continuation. Receipt storage contains no JSON, ETag, credentials or reconstructed command.

**Deployed** requires a fresh actual uniform desired map and an exact saved-version match. Unassigned, unavailable and target-specific maps remain distinct; stale history cannot produce a current green claim. **Deploy** on any saved version, including an older one, opens one confirmation for the exact application, environment and all instances. The final click performs native whole-map preview and one assignment with its reviewed ETag. Any existing target-specific pins are disclosed and removed rather than silently retained under an all-instance claim. There is no Canary or separate rollback wizard. Desired deployment and actual consumer adoption remain separate; **Instance status** reads bounded native observations inline.

A subsequent explicit Deploy can acknowledge only its exact previously displayed clean committed deployment reference before retaining a new key. Unknown, foreign, incomplete and cleanup-unconfirmed originals remain blocking; failed storage acknowledgment stops dispatch. This permits ordinary Deploy followed by an older-version Deploy without turning recovery into a required success-path step.

The collapsed **Differences from deployed version** compares the selected saved version with the fresh uniform default release. There is no arbitrary picker or invented baseline. Map/session/selection changes reset the comparison; no deployment or divergent targets make it unavailable. The left column is deployed JSON, the right selected JSON. Output is complete or explicitly refused beyond 512 lines. Shared-base/reference differences are shown so unchanged Application JSON cannot imply an unchanged whole version.

The disclosure starts closed and closes again when selecting another version, entering Edit or completing Save. An explicitly opened comparison remains open while Deploy refreshes its current baseline.

Configuration canvases use the available desktop width, with long JSON/table content contained inside its control. Conflicting legacy textarea rules are consolidated. Canonical Back/breadcrumb links keep exact selectors and list context. An accepted current-document discard clears local draft/intent once while retaining receipts, established facts and actual pending callback custody; cancel keeps edits.

This guide is source documentation, not passing runtime proof. Focused model/coordinator tests and the existing native Management/browser fixture qualify WORK-190; the parent records actual build, browser, SQL/Audit and cleanup outcomes. Historical R08 proof is retained separately. No Scheduler runtime, new framework, package or backend version counter is introduced.

The [automated testing guide](../../changes/CIS-0030/configuration-testing.md)
maps supported source, permission, session, Save/recovery, Deploy/rollback and
instance-status combinations to ordinary unit and real-host tests. It records
current accepted results separately from interrupted or historical runs.
