# Instance status — focused desktop preview

Status: Andrew Spiteri approved this exact layout. Implementation, the production build and the focused real browser/provider check passed; see [current verification](instance-status-verification.md). The preview images below remain synthetic design evidence.
These are synthetic proposed pixels, not a live instance, production change or
passing browser/provider evidence. Earlier approved assets remain unchanged.

Rename **Configuration adoption** to **Instance status**. The proposed table has
exactly three columns: **Instance**, **Configuration version in use**, and
**Status**. Its caption is **Last reported Configuration per instance**. Readable
instance names and authorized saved notes/dates lead; native IDs, generation and
historical diagnostics remain in the existing panel's collapsed Technical
details.

| Preview | Review purpose |
| --- | --- |
| [Instance status table](assets/instance-status-table-preview.png) | Known Up to date, Needs restart and Failed observations; neutral stale Unknown; explicit Version details unavailable when version metadata is restricted. |
| [Existing details panel](assets/instance-status-panel-preview.png) | Select the Needs restart row without opening another screen. Show its reported older accepted version, the deployed version, the independent report time and collapsed Technical details. |

The version cell means the last reported accepted/in-use version, not the desired
assignment generation. The Needs restart row reports the older **European
defaults** version while **Country cache limits** is deployed. The Failed example
has no current accepted version reported, so the cell says **Not reported**;
historical last-successful data must not fill it. A stale report is neutral
**Unknown**. Notification failure alone does not establish Failed. Saved dates
and report dates remain distinct, with explicit synthetic Malta/CET labels.

Existing AdoptionRead-only operators retain the observation view. A version
metadata denial says **Version details unavailable** without requiring new
content privileges or revealing a GUID in the table. This preview proposes no
fleet percentage, new route, new authority, editing control or additional screen.

Renderer: [render-instance-status-preview.mjs](assets/render-instance-status-preview.mjs).
Root will execute it with the existing pinned Sharp runtime and inspect the two
1600×1000 outputs before presenting them. The generated
`assets/instance-status-preview-manifest.json` records exact renderer/font/input
and PNG identities. The renderer reuses only the digest-checked existing Inter
font; it does not execute the older renderer or copy an approved PNG. No new
package or hosted publication is included.

The standing screenshot gate applies to this table/panel layout. No user
approval or native execution is claimed by this source package.

The controlled render passed on Node v24.15.0 / Sharp0.35.5 / libvips8.18.7. Both PNGs are1600x1000, nonblank, and visually inspected by root and an independent reviewer with no required finding. Font cleanup was verified. Exact inputs, dimensions, frame identities, renderer and review identities are retained in [the review checkpoint](instance-status-review-checkpoint.json) and unchanged generated manifest. The current global design gate is PausedForReview; only review work continues. No product or live-fixture edit is implied by these synthetic screens.


Andrew's exact reply was Approve this layout. [The decision](instance-status-user-approval.json) binds the unchanged two PNGs and renderer d870705d39fff72b5b0107b1fec271c8e23f1bb51c6d9c9c0d5848ee0af63ba7. The earlier pending-status manifest/checkpoint remains historical, not a current pause. The global gate is now Approved for this revision; no implementation or native-test pass follows from screenshot approval alone.

