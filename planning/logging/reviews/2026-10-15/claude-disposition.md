# Claude review disposition

The user explicitly approved the prepared logging bundle on 15 October 2026. The read-only Anthropic Claude CLI received exactly 367,450 UTF-8 prompt bytes, SHA-256 `0680f4c65c46cc6f6af47fab9b3a40f01a7c7af389b3a044ccfd97f5988c5d8b`, describing 75 files. The retained input manifest identifies the reviewed version. The CLI exited 0, returned FINDINGS, and used no tools, MCP servers or delegation. This is a static review, not executed verification or release qualification.

| Finding | Disposition |
| --- | --- |
| F1 — non-string borrowed operation_id silently inherits | Fixed. Structured scopes accept valid strings or null; Guid/integer/other types throw during capture and reject the borrowed event once. Null/blank mean absent, matching the typed context; no duplicate attribute is emitted. C# cases exercise invalid Guid/integer beneath an outer identity and intentional null inheritance. |
| F2 — rendering can replace an existing output fault | Fixed. FaultForRendering leaves an already-faulted output untouched. C# regressions preserve the originating code and UTC label after a later rendering-fault attempt. |
| F3 — live metadata category becomes WriteFailed | Fixed. The worker retains the fixed safe CoreLogInitializationException category for write-path metadata publication failures. Both candidate categories are exercised through the actual queue worker. |
| F4 — budget check and work selection use separate gate sections | Fixed. Both now use the same gate section. The clock sample stays outside the gate; cancellation is based on observed monotonic time and does not promise atomic wall-clock cancellation. Existing withheld-dispatch/original-stop-budget regressions remain active. |
| F5 — reference catalog omits RenderTooLarge | Fixed. Both nonshipping reference declarations and the catalog contain the emitted category. The internal writer size guard is explicitly defensive; public option validation already enforces the size relationship. |
| F6 — NotRequested has no current public producer | Clarified as reserved in the unpublished contract. Current public flush/stop observations use Pending, Succeeded or Failed. |

The reviewed source was then corrected locally. There is no claim that Claude reviewed these subsequent edits or returned a final PASS. Executed verification and exact corrected input identities are recorded in README.md and evidence/input-audit.json.
