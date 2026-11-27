---
title: "CIS-0030 delivery plan"
type: delivery-plan
status: Approved
change_id: CIS-0030
authority: derived-outcome-authorization
---

# Delivery plan

[Shared outcome and limits](proposal.md). Canonical task state is `plan.json`.
Derived under `cislib:CIS-0030:outcome:1bd0878814134335b822d2827a100e02` by Codex; no separate human review of these task views is claimed.

| Task | Work | Status | Depends on |
| --- | --- | --- | --- |
| WORK-010-BACKOFFICE | [Define textual screens, states, actions, and paths (backoffice)](agent-tasks/WORK-010-BACKOFFICE.md) | Complete |  |
| WORK-020-BACKOFFICE | [Render and approve the visual design pack (backoffice)](agent-tasks/WORK-020-BACKOFFICE.md) | Complete | WORK-010-BACKOFFICE |
| WORK-040 | [Implement security, permissions, and exposure boundaries](agent-tasks/WORK-040.md) | Decomposed | WORK-020-BACKOFFICE |
| WORK-050 | [Implement data model and persistence](agent-tasks/WORK-050.md) | Decomposed | WORK-020-BACKOFFICE, WORK-040 |
| WORK-080 | [Implement consumed contracts](agent-tasks/WORK-080.md) | Decomposed | WORK-020-BACKOFFICE, WORK-040, WORK-050 |
| WORK-090 | [Implement backend domain and application behavior](agent-tasks/WORK-090.md) | Decomposed | WORK-020-BACKOFFICE, WORK-040, WORK-050, WORK-080 |
| WORK-100-BACKOFFICE | [Implement the approved frontend experience (backoffice)](agent-tasks/WORK-100-BACKOFFICE.md) | Decomposed | WORK-020-BACKOFFICE, WORK-040, WORK-080 |
| WORK-110 | [Implement cross-module integration and handoff](agent-tasks/WORK-110.md) | Decomposed | WORK-020-BACKOFFICE, WORK-080, WORK-090, WORK-100-BACKOFFICE |
| WORK-140 | [Implement lifecycle and carry-forward behavior](agent-tasks/WORK-140.md) | Decomposed | WORK-020-BACKOFFICE, WORK-050, WORK-090 |
| WORK-150 | [Plan rollout, release, and rollback](agent-tasks/WORK-150.md) | Cancelled | WORK-020-BACKOFFICE |
| WORK-151 | [Define passive trusted capability and scheduler execution contracts](agent-tasks/WORK-151.md) | Complete | WORK-020-BACKOFFICE |
| WORK-152 | [Implement compatible CSharp TypeScript scheduler codecs clients and public substitutes](agent-tasks/WORK-152.md) | Complete | WORK-151 |
| WORK-153 | [Implement native resource action eligibility and exact operator grant enforcement](agent-tasks/WORK-153.md) | Decomposed | WORK-151, WORK-152 |
| WORK-159 | [Implement authoritative calendar evaluator and bounded replicated production](agent-tasks/WORK-159.md) | Draft | WORK-157, WORK-158 |
| WORK-160 | [Implement durable run/attempt history, report reconciliation and protected retention](agent-tasks/WORK-160.md) | Draft | WORK-157, WORK-158 |
| WORK-161 | [Integrate durable attempt entry captured settings and exact execution contracts](agent-tasks/WORK-161.md) | Draft | WORK-151, WORK-152, WORK-157, WORK-158, WORK-160 |
| WORK-162 | [Integrate managed worker rescan immutable build provenance and drain compatibility](agent-tasks/WORK-162.md) | Draft | WORK-161, WORK-158, WORK-160 |
| WORK-169 | [Integrate RM04 scheduler ingress, durable outcome delivery and original recovery](agent-tasks/WORK-169.md) | Draft | WORK-157, WORK-158, WORK-160, WORK-161, WORK-162 |
| WORK-154 | [Implement bounded trusted cohort discovery and rollout grant choices](agent-tasks/WORK-154.md) | Complete | WORK-152, WORK-171 |
| WORK-155 | [Implement bounded operator-bound original command receipts](agent-tasks/WORK-155.md) | Complete | WORK-152, WORK-171 |
| WORK-156 | [Implement protected in-memory drafts and late-response session isolation](agent-tasks/WORK-156.md) | Complete | WORK-155, WORK-171 |
| WORK-157 | [Implement qualified scheduler persistence, trusted catalog and coherent active revisions](agent-tasks/WORK-157.md) | Draft | WORK-151, WORK-152, WORK-171, WORK-154 |
| WORK-158 | [Implement atomic event fanout, manual admission and protected receipts](agent-tasks/WORK-158.md) | Draft | WORK-157, WORK-172 |
| WORK-163 | [Deliver human-first application capabilities and metadata editing with original recovery](agent-tasks/WORK-163.md) | Decomposed | WORK-151, WORK-152, WORK-171, WORK-155, WORK-156, WORK-157 |
| WORK-164 | [Deliver finite Configuration authoring, exact immutable publication and release](agent-tasks/WORK-164.md) | Decomposed | WORK-171, WORK-154, WORK-155, WORK-156, WORK-163 |
| WORK-171 | [Qualify existing Metadata and Configuration operator action grants](agent-tasks/WORK-171.md) | Complete | WORK-151, WORK-152 |
| WORK-172 | [Qualify future Scheduler Task action and recovery grants](agent-tasks/WORK-172.md) | Draft | WORK-151, WORK-152, WORK-157 |
| WORK-173 | [Deliver human-readable current inspection and truthful application-feature availability](agent-tasks/WORK-173.md) | Complete | WORK-151, WORK-152, WORK-171, WORK-155, WORK-156 |
| WORK-174 | [Deliver application metadata review confirmation and original-key recovery](agent-tasks/WORK-174.md) | Complete | WORK-151, WORK-152, WORK-171, WORK-155, WORK-156, WORK-173 |
| WORK-175 | [Deliver future trusted application feature catalog and versioned participation inspection](agent-tasks/WORK-175.md) | Draft | WORK-151, WORK-152, WORK-171, WORK-155, WORK-156, WORK-157, WORK-173 |
| WORK-176 | [Deliver exact authorized Configuration binding editor context](agent-tasks/WORK-176.md) | Complete | WORK-151, WORK-152, WORK-171, WORK-154 |
| WORK-177 | [Deliver finite Configuration authoring, exact immutable publication and release](agent-tasks/WORK-177.md) | Decomposed | WORK-171, WORK-154, WORK-155, WORK-156, WORK-173, WORK-176 |
| WORK-166 | [Deliver scoped event/run/attempt history and worker-provenance details](agent-tasks/WORK-166.md) | Draft | WORK-160, WORK-161, WORK-162, WORK-173, WORK-175, WORK-172, WORK-156 |
| WORK-178 | [Deliver bounded unpublished Configuration candidate preview](agent-tasks/WORK-178.md) | Complete | WORK-151, WORK-152, WORK-171, WORK-154, WORK-176 |
| WORK-179 | [Deliver finite Configuration authoring, exact immutable publication and release](agent-tasks/WORK-179.md) | Complete | WORK-171, WORK-154, WORK-155, WORK-156, WORK-173, WORK-176, WORK-174, WORK-178 |
| WORK-167 | [Deliver whole-map assignment canary promotion and rollback journeys](agent-tasks/WORK-167.md) | Cancelled | WORK-171, WORK-154, WORK-155, WORK-156, WORK-179 |
| WORK-180 | [Deliver exact binding release history and scoped adoption reads](agent-tasks/WORK-180.md) | Complete | WORK-171, WORK-154, WORK-176, WORK-178 |
| WORK-181 | [Deliver complete reviewed map canary promotion and retained-release rollback](agent-tasks/WORK-181.md) | Cancelled | WORK-171, WORK-154, WORK-155, WORK-156, WORK-179, WORK-180 |
| WORK-168 | [Deliver bounded truthful desired applied and historical adoption views](agent-tasks/WORK-168.md) | Complete | WORK-180, WORK-152, WORK-171 |
| WORK-182 | [Review R08 JSON-first application UX and readable tab routes](agent-tasks/WORK-182.md) | Complete | WORK-173, WORK-179, WORK-180 |
| WORK-183 | [Deliver native predeployment Configuration creation and bounded assignment origins](agent-tasks/WORK-183.md) | Complete | WORK-171, WORK-176, WORK-180 |
| WORK-184 | [Deliver R08 readable navigation and JSON-first Configuration workflows](agent-tasks/WORK-184.md) | Complete | WORK-173, WORK-179, WORK-180 |
| WORK-185 | [Deliver expanded current and retained past instance presentation](agent-tasks/WORK-185.md) | Complete | WORK-173 |
| WORK-186 | [Correct portal and isolated review session lifetime](agent-tasks/WORK-186.md) | Complete | WORK-156 |
| WORK-187 | [Repair application resizing description and metadata return navigation](agent-tasks/WORK-187.md) | Complete | WORK-184, WORK-185 |
| WORK-188 | [Repair Configuration editing Shared copy and modification dates](agent-tasks/WORK-188.md) | Complete | WORK-183, WORK-184 |
| WORK-189 | [Design one-page Configuration versions and deployment](agent-tasks/WORK-189.md) | Complete | WORK-182 |
| WORK-190 | [Implement approved Configuration version saving deployment and rollback](agent-tasks/WORK-190.md) | Complete | WORK-189 |
| WORK-191 | [Constrain Configuration diff to the actual deployed version](agent-tasks/WORK-191.md) | Complete | WORK-189 |
| WORK-192 | [Diagnose and correct Windows Node native serving failures](agent-tasks/WORK-192.md) | Complete |  |
| WORK-194 | [Reconcile Configuration obligations and close completed CIS records](agent-tasks/WORK-194.md) | Complete | WORK-192, WORK-193 |
| WORK-193 | [Complete Configuration browser state and Instance status boundaries](agent-tasks/WORK-193.md) | Complete | WORK-189, WORK-180 |
| WORK-195 | [Apply the approved bounded Node runtime correction](agent-tasks/WORK-195.md) | Complete |  |
| WORK-165 | [Deliver real whole-ScheduledTask editing, preview and operational commands](agent-tasks/WORK-165.md) | Draft | WORK-157, WORK-158, WORK-159, WORK-161, WORK-162, WORK-179, WORK-190, WORK-168, WORK-172, WORK-155, WORK-156 |
| WORK-170 | [Complete maintained scheduler consumer composition and portable portal delivery](agent-tasks/WORK-170.md) | Draft | WORK-169, WORK-173, WORK-175, WORK-174, WORK-179, WORK-165, WORK-166, WORK-190, WORK-168, WORK-162 |
| WORK-196 | [Bound Configuration row action reader lifetime](agent-tasks/WORK-196.md) | Complete |  |
| WORK-197 | [Invalidate protected Configuration across tabs and session changes](agent-tasks/WORK-197.md) | Complete |  |
| WORK-198 | [Refresh open Configuration editor context without losing drafts](agent-tasks/WORK-198.md) | Complete |  |
| WORK-199 | [Prepare portal build and headed browser in existing service CI](agent-tasks/WORK-199.md) | Complete |  |
| WORK-200 | [Scope peer invalidation to the retired session](agent-tasks/WORK-200.md) | Complete |  |
| WORK-201 | [Suspend native Deploy modality during session verification](agent-tasks/WORK-201.md) | Complete |  |
| WORK-202 | [Retire exact pooled Management broker topology after native drain](agent-tasks/WORK-202.md) | Complete |  |
| WORK-203 | [Align legacy service and browser checks with approved Configuration versions](agent-tasks/WORK-203.md) | Complete |  |
