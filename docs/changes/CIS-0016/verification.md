---
title: CIS-0016 intake and verification boundary
type: verification-record
status: Draft
change_id: CIS-0016
---

# Intake observations

The request starts RM-01 from the supplied specification. CIS registered this
dossier against merged baseline b53b4eaef6b3374e7387e9bbb065b57533248445 on
codex/application-identity-security. Source, contract reconciliation, P01–P16,
seven task definitions, dependency candidates and one shared proposed allocation
are prepared for review. No implementation authority is claimed by these drafts.

| Check | Observation |
| --- | --- |
| Source preservation | Original and repository copy match SHA-256 d4966a3ad3d1a870d91422a35cc8467cf780d01a9f91cbc3313e5b3fe812ca36; source-directory attributes preserve bytes. |
| Baseline drift | Since source-reviewed31739a24, only CountryData unit-example/guide/CIS work changed; actual runtime seams were inspected. |
| Existing contracts | Environment identity, configuration reader/snapshots, facade order, native REST/leases, shutdown, Metrics capacities and public testing/package conventions reviewed. |
| Independent intake | Read-only Core review6active minutes plus2final-plan review, transport review6.5, provider/dependency review10; no native execution or credential/infrastructure operation by agents. |
| Optional dependencies | Exact direct pins and27 mapping candidates supported by official metadata/cached nuspecs; no generated restore graph or package-consumer pass claimed. |
| User work | Only pre-existing untracked CountryData launch settings; SHA-256 ea7e7219222183193f0361ae674998f1edc43a3789f118748c59f166a4d29784 preserved. |
| Implementation/native tests | Not started; fresh profile, dependency and finite allocation approval required. |

The proposed source/all-agent intake estimate is4,170,000ms: root45conservative
active minutes plus24.5agent minutes, including planning, research, CIS
administration, documentation and local intake closeout. Waiting is excluded.
This effort is to be charged to the proposed containing allocation if approved;
it is not free work, new implementation attempts or a reopened historical owner.
No native restore/build/test/pack was launched during intake.

Final plan review found all25acceptance scenarios assigned and the30hour/32attempt/
3600restoration-second distribution consistent. It identified the Entra outbound
integration handoff to REST; the plan now freezes shared signatures in WORK-001
and explicitly gates WORK-003 completion on actual WORK-002 enforcement.
Strict documentation validation passes224/224documents with zero warnings/errors.
Git whitespace checks cover all authored files. The byte-preserved attachment is
excluded from that formatting check because its original trailing blank lines
must remain unchanged; its staged blob is compared with the preserved file.

# Required acceptance gaps

AC-02 is unverified: no authorized AKS OIDC/federation, projected workload
assertion, Entra application/API app-role relationship or receiving deployment
has been supplied. WORK-003 owns implementation and the reproducible guide;
tenantless SDK/JWT validation does not establish this deployment proof.

AC-24 is unverified: no authorized Kubernetes namespace, cert-manager issuer,
issuance controls or mounted Secret deployment is supplied. WORK-005 owns the
recipe and actual local adoption behavior; file replacement cannot establish
cert-manager issuance/renewal. Record actual deployment evidence only under
available operational authority. RM-01 cannot be marked complete while these
acceptance requirements remain unverified.

Native authorization, JWT, TLS, expiry, revocation, reload/drain, listener, facade,
local setup, metrics and package-consumer observations will be recorded with
their owning implementation tasks after approval. Broader platform/release,
coverage/mutation/performance and publication obligations remain distinct.
