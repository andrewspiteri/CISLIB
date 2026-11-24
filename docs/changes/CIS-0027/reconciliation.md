# RM-08 reconciliation against current main

Status: proposed design and scope, not an implementation or approval.

The source is the preserved [70,206-byte RM-08 attachment](../../../planning/configuration/publication/sources/CISLIB_RM08_Central_Configuration_Publication_and_Distribution_Specification_v1.0.md),
SHA256 `592f6af2e49d982fa7bfbe4581fac8f66eeb9939afd7c4c6fa9008e3e1e2db2f`.
The actual baseline is `abfbcafaa6ef2f3c3c18304244e4dafc1dd5c852`, merged RM-07
PR #17 including its final cancellation and native-fetch corrections. The
attachment's references to main `494e8941` and branch `38031b41` describe older
source. Preserve the original document and resolve those differences here.

| Requirement boundary | Current implementation | Necessary RM-08 change |
| --- | --- | --- |
| RM-07 live authority prerequisite | Mandatory registration/control, 5-second checks, at most 15-second freshness, readiness-only admission, reconciliation and canceled-startup cutoff are implemented | Reuse completed CIS-0024–26. Do not redo their work or claim pre-Build configuration already exists. |
| Configuration identity and roots | `Configuration/Objects/ConfigurationIdentity.cs` already omits assembly version; catalog retains one concrete root and once-captured defaults | Preserve exact persisted keys; explicitly enroll a one-to-one portable ID/schema/local CLR binding and fixed host scope. Discovery alone grants no central exposure. |
| Source consistency | `ConfigurationStore.cs` and refresh coordinator read common/application independently | Optional paired-snapshot capability; mandatory for central entries, with legacy independent reads retained only for noncentral stores. |
| Actual acceptance | `ConfigurationValuePipeline.Apply` returns bool and exits on equal checksum | Commit immutable provenance under the same value gate, including unchanged acceptance; provide exact applied/staged/rejected/faulted outcomes without changing the old public pipeline contract. |
| Update modes | Existing pipeline performs hot apply; no central desired/applied state | Local enrollment chooses HotReload or RestartRequired; one bounded pending candidate, supersession, restart baseline and terminal setter-fault reporting. |
| Constructor-visible values | `CISApplicationComposition.PrepareAsync` loads Configuration before registering REST/final services; registration is later DI-created Startup work | Explicit authenticated bootstrap resource/session preparation before Configuration and Build; same session handed to the single final provider. |
| Existing REST and credential ownership | Public standalone HttpRestClient and security-bound transport exist; hosted borrowed transport requires exact final IMeterFactory | Use those implementations. Retire joined standalone bootstrap clients and bind runtime reads to normal final-provider clients. Do not install an unmetered bootstrap transport as a hosted borrowed transport. |
| Certificate renewal | Optional `CertificatePemRuntime` owns and renews into one readonly transport | A small supported preparation/adoption seam must retarget actual renewal/ownership safely; no invented generic credential transfer or temporary provider. |
| Control versus config refresh | IManagedStateReconciler runs under the short control budget; initial false reconciliation makes StartAsync fail | Consume the initial same-session config epoch before first Management startup. Retained initial/session/reconnect epochs trigger existing serialized config work; healthy controls do not perform or restart a 256-definition sweep. |
| Accepted data versus latest attempt | Existing refresh IsValid describes the latest reconciliation; old values can remain intact | Track intact accepted state separately. Failed polling, identified rejection and pending restart do not automatically invalidate accepted readiness; Disabled/Faulted does. No default config TTL or desired==applied admission policy. |
| Authoritative backend | Typed backend, qualified primary/namespace/epoch/hold and schema v2 already exist | Same backend/transaction participant graph, explicit compatible next migration and truthful configuration capabilities; no second authority. |
| Manifest before consumers | Application rows are currently allocated on first trusted registration | Trusted manifest provisioning allocates/reuses application/environment resources and stable empty source/map ETags before consumers bootstrap. |
| Provisioning actor | Existing --migrate is schema-only, without native mutation actor/result/Audit | Migration seeds only empty stable root state. A fixed authenticated action applies the locally loaded manifest digest under root ETag/key/reason. Ordinary startup validates persisted state rather than silently applying edits. |
| Publication and release | No immutable configuration content/reference tables | Exact payloadJson UTF-8 and SHA-256, immutable publications, explicit absent release references, reference-safe creation and complete encoded distributability checks. |
| Assignment/cohort resolution | No persisted cohort binding or assignment map | Trusted issuer/subject plus application/environment mapping captured per registration; one whole-map CAS/generation transaction, explicit promote/rollback intent and deliberate override removal. |
| Original mutation recovery | Existing metadata pattern already joins RM-02/RM-03/RM-06, but its result and audit DTOs are metadata-specific | Four fixed configuration operation families and typed recovery; identical execute/replay/lookup scopes, original key/precondition/reason fingerprints, known commit before success and payload-free Audit details. |
| Wire and clients | Canonical OpenAPI/schema/vectors and 13 current methods; problems hard-code metadata results | Add separate configuration result/problem and substitutable client port. Keep IManagementClient/custom implementations unchanged. |
| HTTP/transport limits | Ordinary request default 64 KiB; total composition 32 client/operation pairs including request fallback | Configuration-only 1-MiB profile. One configuration alias plus fallback per config client: two full Management profiles plus two config profiles remain exactly 32. CountryData runtime composition remains within 18 pairs. |
| TS decoding and effects | Request method union omits POST; decoding is synchronous; all non-GET calls imply mutation; all 3xx are refused | Add POST, compatible awaited decoding for native WebCrypto, explicit non-mutating validation/preview POST, and narrowly opted-in 304 handling. Preserve legacy defaults, GET-only retry policy, redirects and actual cleanup. |
| Adoption | Existing reporter owns observation/inventory, uncertainty and lifecycle | Separate complete configuration-status stream on that same owner; independent sequence/receipt freshness and authorized current-assignment projection. No payloads/checksums or invented process identity. |
| Retention | Existing cleanup handles telemetry only | Content quota and reference-safe source/release cleanup on the same owner, independent of operation-result/telemetry retention. Preserve assigned/pinned content; fail visibly when pinned-full. |
| Tutorial | CountrySettings is explicitly restart-scoped; CountryCatalog captures it in its constructor | Opt-in RestartRequired enrollment of that root for all required operator scenarios. Prove HotReload in current native/public fixtures, without inventing new CountryData behavior or changing unmanaged defaults. |

The proposal excludes a portal, deployment automation, a new DI/REST/auth/config
framework, secret resolver, broker or watcher, TypeScript/Kotlin Configuration
runtime, alternate database provider, automatic promotion/restart, multi-root or
fleet atomicity, broad mutation/performance campaign and package release.
