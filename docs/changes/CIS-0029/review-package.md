---
title: "RM09 R03 application detail tabs review"
type: design-review-package
status: Draft
change_id: CIS-0029
---

# RM09 R03: application detail tabs

Andrew Spiteri accepted the R02 application list and instance direction, then
requested tabs on the selected application's detail screen. **R03 was approved by Andrew Spiteri, who also requested a Scheduled jobs tab next.** The accepted
application list remains the reference; it is not being redesigned.

The desktop detail screen keeps application identity and environment above three
tabs: **Instances** (default), **Configuration**, and **Capabilities**. Switching
tabs keeps the exact selected application. Back restores the list filters/page
and originating focus. No additional tab is inferred from "etc".

- **Instances** preserves the accepted per-instance reported version, presence
  and Last seen data. Versions are registration reports; different instances may
  report different versions.
- **Configuration** lists definition metadata for the selected application and
  environment: configuration ID, schema version, host scope and source/binding
  references. It exposes no configuration payload/default values and provides
  no publication, editing or assignment flow.
- **Capabilities** lists the Management API operations and advisory access hints
  for the current caller. It does not treat these as reported instance modules,
  Jobs or runtime inventory. Missing hints say **Not evaluated**; actual requests
  still require native authorization.

All content remains synthetic. Mobile, production login, metadata editing,
configuration mutations, result recovery and other broader RM09 workflows remain
deferred. The source specification stays unchanged.

## Current API boundaries

Instance `applicationVersion` and application `getCapabilities` are delivered
fields/APIs. A complete application-name/environment search still needs P2.
A complete selected-application Configuration list additionally needs a bounded
application/environment definition query: current `getDefinitions` is globally
paged. Searching only a loaded page or arbitrarily associating common definitions
would not deliver the intended list. Human session and native resource authority
remain production prerequisites.

See [wireframes](wireframes.md), [design brief](design-brief.md) and
[contract reconciliation](reconciliation.md) for behavior, exact facts and these
prerequisites. The [design record](design.md) binds the exact renderer and PNG
hashes. Its `authority: human-reviewed` metadata names the required approval
mechanism; it does not itself grant approval.

## Decision history and boundary

R01 was revised following the desktop-only request. R02 is preserved at local
Git commit `6176b895d423ddfbf76936733432191bd2287782`; its renderer was
`5369b0a8c5cba6c8df8ead9678f6e7f50b7629bf35876fbe5601fbc628cfb496`.
The user's "this part is ok" accepts that base direction, alongside the explicit
request to change detail navigation. The native requested-revision record keeps
that partial acceptance; no blanket approval of an unmodified full manifest is
inferred. No earlier screenshot is claimed as CIS-verified approved reuse.

Approval requested next is limited to the three detail tabs and their read-only
list presentation. It does not approve future configuration write workflows or
additional tabs. The unchanged application list need not be reconsidered. After
this UI decision, normal CIS reconciliation of the current scope, direction,
outcome, plan and owner records must precede production work. Earlier full-scope
plans remain draft future material.

## Rendered review index

CIS gate: **Approved**. R03 tab design: **approved by Andrew Spiteri**. The requested Scheduled jobs addition is not part of this approval.
Renderer SHA256: `071d759ed388a2852f15ce82ff9a07ceb3e072991df4e1c50774be8c33ae24b4`.
Native render-result SHA256: `8450342bbf6487b7bf961ac8b469f8374d4c3ad92f52714d317b21f29a00dab4`.

New primary review: Instances, Configuration and Capabilities detail tabs.
All three application-list PNGs match the accepted R02 reference byte for byte.

| Screen | State | Desktop size | Preview |
| --- | --- | --- | --- |
| rm09-applications | no-matches | 1600 × 1000 | [Open PNG](assets/rm09-applications--no-matches--desktop.png) |
| rm09-applications | primary | 1600 × 1000 | [Open PNG](assets/rm09-applications--primary--desktop.png) |
| rm09-applications | unavailable | 1600 × 1000 | [Open PNG](assets/rm09-applications--unavailable--desktop.png) |
| rm09-capabilities | primary | 1600 × 1000 | [Open PNG](assets/rm09-capabilities--primary--desktop.png) |
| rm09-configuration-list | primary | 1600 × 1000 | [Open PNG](assets/rm09-configuration-list--primary--desktop.png) |
| rm09-instances | empty | 1600 × 1000 | [Open PNG](assets/rm09-instances--empty--desktop.png) |
| rm09-instances | primary | 1600 × 1000 | [Open PNG](assets/rm09-instances--primary--desktop.png) |
| rm09-instances | stale | 1600 × 1000 | [Open PNG](assets/rm09-instances--stale--desktop.png) |

Eight synthetic desktop frames are review evidence. Static/visual checks and
the independent model reader found no material presentation issue; they do not
establish working browser tabs, authorization or live service integration.
