---
title: "cislib Technical Intent Questionnaire"
type: specification
status: Draft
scope: Workspace
owner: Product owner and technical authority
last_reviewed: null
review_cadence: on approved requirement or high-level technical direction change
cis:
  stable_id: cislib:spec:technical-intent-questionnaire
  technical_questionnaire_schema: 2
  brd_version: semantic-v1:sha256:6190ff89fbcb18f0e009e516f8cd1bafa6855e5891713c66dbc9eed57e3bc5ce
  project_mode: existing-project
---

# cislib Technical Intent Questionnaire

This governed pre-intent record captures high-level technology and architecture direction. Explicit facts in accepted, current BRD and Technical Intent sources resolve matching fields with source references. During setup, selected supplied direction can populate working fields pending final baseline adoption; outside setup, unadopted directions remain suggestions. CIS also records deterministic repository evidence for existing implementations. Optional capabilities absent from a supplied scope are marked Not requested with its accepted or pending-baseline provenance; this records neither N/A nor an exclusion. Unresolved choices and source conflicts remain visible; derived answers may be overridden explicitly.

## Decision progress

- Resolved: 13 of 16
- Derived from source or repository evidence: 13
- Answered by a human: 0
- Optional capabilities not requested: 0
- State: Draft — answer every question before technical intent generation

## Questions
### TI-Q-001 — Product surfaces

Which product surfaces are in scope?

- Why this is needed: The architecture must distinguish user-facing, machine-facing, and operational entry points before components are selected.
- Common options: Public web; Authenticated customer web; Backoffice web; iOS; Android; Desktop; API; Background workers; External integrations
- Advisory starting direction: Start with only the surfaces required by the Active BRD; classify each frontend as public, customer, or backoffice and record mobile or machine-facing surfaces separately.
- Status: Derived
- Answered by: Not recorded
- Answered at: Not recorded
- Resolution source: accepted-document
- Confidence: source-explicit
- Evidence: docs/specs/technical-intent-spec.md:50 — Frontend technology (sha256:3f8b6e04a743a1b4bc067f8730dc1ea9b3b538da92ac22b0745b4f942f9c156c); docs/specs/technical-intent-spec.md:52 — Frontend technology (sha256:3f8b6e04a743a1b4bc067f8730dc1ea9b3b538da92ac22b0745b4f942f9c156c); docs/specs/business-requirements.md:38 — Scope (sha256:7e0e542c011a0fa65c23ce63e305f4a62cd4c4c607d73c05b3a0bc46f2fcf042)
- Assessment record: {"SupportingBasis":{"BRD:Scope:1":"sha256:e70d46816a42a372b1d95c2f923b3d85825216021d4e7e6c6f182dbf5ed08c64","BRD:unmapped:Business outcomes:0":"sha256:63f06b10d37e84d466481fa587bdb9285edd597c5efcf41b5cbc189008dceee3","BRD:unmapped:Canonical registration:0":"sha256:26027a377571f4b99f4595c7fc8a91cced04dacd4f04e6bfbc1b54ce48cf4ad9","BRD:unmapped:Functional requirements:1":"sha256:cbc46b87f50780d62831f4680c2942455d58d98de6ef10d840bfad06c3913a3d","BRD:unmapped:Functional requirements:3":"sha256:7f7984db9040b8819b7deb8ecd19c6fecdbf10931f9b47035ab908dcb4692892","BRD:unmapped:Functional requirements:5":"sha256:89428a5f0e3c2b87c8bef5b7c0ce5f21cc120d3ab446c18fc3e7eda89c64504d","BRD:unmapped:Open questions:0":"sha256:950ebf522c1271f11155a3c349a1188af4c26fbdff01b5d607cb121e1e9c1dec","BRD:unmapped:Stakeholders and actors:0":"sha256:4660f0a5b083676cceac105f14ca48f61178e358042b7c9a61ef24cbf2f07561","BRD:unmapped:Stakeholders and actors:4":"sha256:72bdf5d075aff8ac31d1694ec34df8fe41568095626a576e1c6c1ad6ce737cad","BRD:unmapped:Success measures:1":"sha256:d829372214a932a83be4a13b7b23beab9c46997216a895f190cb1525eef54dcd","BRD:unmapped:Traceability:1":"sha256:418faf927fe56611e48197e2de8f82df3731c0c7ad88c1b5552278f21180985c","BRD:unmapped:Traceability:4":"sha256:f656d3ff988a225e19a90670466df23d06fb528991e268dbd10bb82405e6cc16","BRD:unmapped:Traceability:8":"sha256:2831ac927fc003681fcadb517dbdf6eea8891c2710e010ccbca60d6e35bc49dc"},"Status":"Current","Reason":null,"History":[],"SourceFactAuthority":{"Basis":{"docs/specs/technical-intent-spec.md":"sha256:985e7420038e9fa61129f14a2ba6f2834ac2d4ca87dc0a9e817cb808d4924700","docs/specs/business-requirements.md":"sha256:e62e658f1bd571a51503aab04c1d6a4e39063b457a51e172e53e4a0e313aa368"},"AssessmentStatus":"Current","AssessmentReason":null}}

#### Recorded answer

No user-facing UI

The product has no frontend application or browser/mobile/desktop presentation technology. HTTP routes are health-reporting contracts inside an application host. The line above records the settled source direction. Accepted-document questionnaire derivation requires the actual approved technical baseline; the canonical no-visual-UI applicability record remains separate and governed by supported questionnaire/adoption controls.

The product has no visual frontend. Machine-facing Healthcheck HTTP and local-pipe reporting, the matching probe executable, consumer examples and IDE/native developer workflows do not create a web or desktop application product. [S02, S06, S07]

### TI-Q-002 — Frontend technology

What frontend technology should each user-facing surface use?

- Why this is needed: Framework, rendering model, accessibility tooling, and supported platforms shape component ownership and verification.
- Common options: Existing repository framework; React / Next.js; Angular; Vue / Nuxt; SwiftUI; Jetpack Compose; .NET MAUI; No frontend
- Advisory starting direction: Reuse a detected framework when one exists. Otherwise choose one maintained framework per surface and avoid a second UI stack without a distinct platform need.
- Status: Derived
- Answered by: Not recorded
- Answered at: Not recorded
- Resolution source: accepted-document
- Confidence: source-explicit
- Evidence: docs/specs/technical-intent-spec.md:46 — Product surfaces (sha256:3f8b6e04a743a1b4bc067f8730dc1ea9b3b538da92ac22b0745b4f942f9c156c); docs/specs/technical-intent-spec.md:52 — Frontend technology (sha256:3f8b6e04a743a1b4bc067f8730dc1ea9b3b538da92ac22b0745b4f942f9c156c); docs/specs/business-requirements.md:38 — Scope (sha256:7e0e542c011a0fa65c23ce63e305f4a62cd4c4c607d73c05b3a0bc46f2fcf042); docs/specs/business-requirements.md:106 — Traceability (sha256:7e0e542c011a0fa65c23ce63e305f4a62cd4c4c607d73c05b3a0bc46f2fcf042)
- Assessment record: {"SupportingBasis":{"BRD:Scope:1":"sha256:e70d46816a42a372b1d95c2f923b3d85825216021d4e7e6c6f182dbf5ed08c64","BRD:Traceability:3":"sha256:2fc0570903f07ed723aefc54a491c169b4241812ccb2e187229e1461b46b01ce","BRD:unmapped:Business outcomes:0":"sha256:63f06b10d37e84d466481fa587bdb9285edd597c5efcf41b5cbc189008dceee3","BRD:unmapped:Canonical registration:0":"sha256:26027a377571f4b99f4595c7fc8a91cced04dacd4f04e6bfbc1b54ce48cf4ad9","BRD:unmapped:Functional requirements:1":"sha256:cbc46b87f50780d62831f4680c2942455d58d98de6ef10d840bfad06c3913a3d","BRD:unmapped:Functional requirements:3":"sha256:7f7984db9040b8819b7deb8ecd19c6fecdbf10931f9b47035ab908dcb4692892","BRD:unmapped:Functional requirements:5":"sha256:89428a5f0e3c2b87c8bef5b7c0ce5f21cc120d3ab446c18fc3e7eda89c64504d","BRD:unmapped:Open questions:0":"sha256:950ebf522c1271f11155a3c349a1188af4c26fbdff01b5d607cb121e1e9c1dec","BRD:unmapped:Stakeholders and actors:0":"sha256:4660f0a5b083676cceac105f14ca48f61178e358042b7c9a61ef24cbf2f07561","BRD:unmapped:Stakeholders and actors:4":"sha256:72bdf5d075aff8ac31d1694ec34df8fe41568095626a576e1c6c1ad6ce737cad","BRD:unmapped:Success measures:1":"sha256:d829372214a932a83be4a13b7b23beab9c46997216a895f190cb1525eef54dcd","BRD:unmapped:Traceability:1":"sha256:418faf927fe56611e48197e2de8f82df3731c0c7ad88c1b5552278f21180985c","BRD:unmapped:Traceability:4":"sha256:f656d3ff988a225e19a90670466df23d06fb528991e268dbd10bb82405e6cc16","BRD:unmapped:Traceability:8":"sha256:2831ac927fc003681fcadb517dbdf6eea8891c2710e010ccbca60d6e35bc49dc"},"Status":"Current","Reason":null,"History":[],"SourceFactAuthority":{"Basis":{"docs/specs/technical-intent-spec.md":"sha256:c1d448de721e89276ffd31cc8e0b50fcc5086d37fb734d4cc6773661561bcea3","docs/specs/business-requirements.md":"sha256:6c0f51d12ba4adbec6860ceec3408499cd23d03490eddbdb4432c1454fdc5527"},"AssessmentStatus":"Current","AssessmentReason":null}}

#### Recorded answer

The primary surface is the .NET in-process library API. Supporting machine-facing surfaces are host-integrated Healthcheck HTTP reporting, local pipe protocol and matching non-core probe executable. Type Manager supplies build-time inventory generation and runtime catalog/query APIs. Console/file logging are output capabilities used by a host, not standalone application frontends.

The product has no frontend application or browser/mobile/desktop presentation technology. HTTP routes are health-reporting contracts inside an application host. The line above records the settled source direction. Accepted-document questionnaire derivation requires the actual approved technical baseline; the canonical no-visual-UI applicability record remains separate and governed by supported questionnaire/adoption controls.

The product has no visual frontend. Machine-facing Healthcheck HTTP and local-pipe reporting, the matching probe executable, consumer examples and IDE/native developer workflows do not create a web or desktop application product. [S02, S06, S07]

| S04 | `planning/logging/sources/core-library-implementation-guide-v1.md`, `planning/logging/priority-spool/brd-v1.2.md`, current `docs/modules/logging/` guides: detailed Logging source and selected frontend behaviour; historical names/package assumptions are interpreted through S01. |

### TI-Q-003 — Backend technology

What language, runtime, and application framework should own server-side behavior?

- Why this is needed: This establishes the application boundary, package ecosystem, deployment unit, and compiler-backed architecture rules.
- Common options: .NET / ASP.NET Core; TypeScript / Node.js; Java / Spring Boot; Kotlin / Ktor; Python / FastAPI; Go; No backend
- Advisory starting direction: Prefer the detected backend runtime unless an evidenced migration is intended: cislib/countrylist: csharp, aspnet-core, backend-api-producer; cislib/cis-core: csharp, aspnet-core, backend-web; cislib/healthcheckhttphost: csharp, aspnet-core, backend-web; cislib/consumer: csharp, aspnet-core, backend-web.
- Status: Derived
- Answered by: Not recorded
- Answered at: Not recorded
- Resolution source: accepted-document
- Confidence: source-explicit
- Evidence: docs/specs/technical-intent-spec.md:46 — Product surfaces (sha256:3f8b6e04a743a1b4bc067f8730dc1ea9b3b538da92ac22b0745b4f942f9c156c); docs/specs/technical-intent-spec.md:56 — Backend technology and topology (sha256:3f8b6e04a743a1b4bc067f8730dc1ea9b3b538da92ac22b0745b4f942f9c156c); docs/specs/technical-intent-spec.md:69 — Runtime architecture and trust boundaries (sha256:3f8b6e04a743a1b4bc067f8730dc1ea9b3b538da92ac22b0745b4f942f9c156c); docs/specs/technical-intent-spec.md:113 — Operations, migration, and recovery intent (sha256:3f8b6e04a743a1b4bc067f8730dc1ea9b3b538da92ac22b0745b4f942f9c156c); docs/specs/business-requirements.md:23 — Executive summary (sha256:7e0e542c011a0fa65c23ce63e305f4a62cd4c4c607d73c05b3a0bc46f2fcf042); docs/specs/business-requirements.md:36 — Scope (sha256:7e0e542c011a0fa65c23ce63e305f4a62cd4c4c607d73c05b3a0bc46f2fcf042); docs/specs/business-requirements.md:57 — Business capabilities and processes (sha256:7e0e542c011a0fa65c23ce63e305f4a62cd4c4c607d73c05b3a0bc46f2fcf042); docs/specs/business-requirements.md:76 — Quality, regulatory, and operational requirements (sha256:7e0e542c011a0fa65c23ce63e305f4a62cd4c4c607d73c05b3a0bc46f2fcf042); docs/specs/business-requirements.md:79 — Quality, regulatory, and operational requirements (sha256:7e0e542c011a0fa65c23ce63e305f4a62cd4c4c607d73c05b3a0bc46f2fcf042); docs/specs/business-requirements.md:85 — Constraints and assumptions (sha256:7e0e542c011a0fa65c23ce63e305f4a62cd4c4c607d73c05b3a0bc46f2fcf042); docs/specs/business-requirements.md:87 — Constraints and assumptions (sha256:7e0e542c011a0fa65c23ce63e305f4a62cd4c4c607d73c05b3a0bc46f2fcf042)
- Assessment record: {"SupportingBasis":{"BRD:Business capabilities and processes:1":"sha256:3826e297b75b82eb4b4aa23122c2e9340c51b0a72556f3efb8d11a9911a99ec2","BRD:Constraints and assumptions:0":"sha256:1b6487cd2655d7c2bfcdd9dfd49cfff6170e66945b808ca530cb380ad3cabb03","BRD:Constraints and assumptions:1":"sha256:d36f3f8506b6a2d61d89fbe5aadc9a1f391a0e098a0521080a69365135f92576","BRD:Executive summary:0":"sha256:633454819f1bbba147ac8aaa479e1166301cf6246edfd28dbca6258ea7df0b5d","BRD:Quality, regulatory, and operational requirements:0":"sha256:f92d77877115d6e2d2a28e815b1107afca880289ab9272ea0d85512bff21f936","BRD:Quality, regulatory, and operational requirements:3":"sha256:a4ae05c407c91ca6361b12c49af1a15f97949f8073b77ccc006eac9e3a59b9ac","BRD:Scope:0":"sha256:60b07e4d08d635a2449d464752ccea2e66918874d14d8a359482596006a1f448","BRD:unmapped:Business outcomes:0":"sha256:63f06b10d37e84d466481fa587bdb9285edd597c5efcf41b5cbc189008dceee3","BRD:unmapped:Canonical registration:0":"sha256:26027a377571f4b99f4595c7fc8a91cced04dacd4f04e6bfbc1b54ce48cf4ad9","BRD:unmapped:Functional requirements:1":"sha256:cbc46b87f50780d62831f4680c2942455d58d98de6ef10d840bfad06c3913a3d","BRD:unmapped:Functional requirements:3":"sha256:7f7984db9040b8819b7deb8ecd19c6fecdbf10931f9b47035ab908dcb4692892","BRD:unmapped:Functional requirements:5":"sha256:89428a5f0e3c2b87c8bef5b7c0ce5f21cc120d3ab446c18fc3e7eda89c64504d","BRD:unmapped:Open questions:0":"sha256:950ebf522c1271f11155a3c349a1188af4c26fbdff01b5d607cb121e1e9c1dec","BRD:unmapped:Stakeholders and actors:0":"sha256:4660f0a5b083676cceac105f14ca48f61178e358042b7c9a61ef24cbf2f07561","BRD:unmapped:Stakeholders and actors:4":"sha256:72bdf5d075aff8ac31d1694ec34df8fe41568095626a576e1c6c1ad6ce737cad","BRD:unmapped:Success measures:1":"sha256:d829372214a932a83be4a13b7b23beab9c46997216a895f190cb1525eef54dcd","BRD:unmapped:Traceability:1":"sha256:418faf927fe56611e48197e2de8f82df3731c0c7ad88c1b5552278f21180985c","BRD:unmapped:Traceability:4":"sha256:f656d3ff988a225e19a90670466df23d06fb528991e268dbd10bb82405e6cc16","BRD:unmapped:Traceability:8":"sha256:2831ac927fc003681fcadb517dbdf6eea8891c2710e010ccbca60d6e35bc49dc"},"Status":"Current","Reason":null,"History":[],"SourceFactAuthority":{"Basis":{"docs/specs/technical-intent-spec.md":"sha256:0cedfa7247ec491760e4c8640bd4f1c3974d46454ff9394828d11acfe5187366","docs/specs/business-requirements.md":"sha256:8b581630d9c44b296ce8e5e5c9ecb85cab4f795f7ff7b567c9b4b34e6f83e081"},"AssessmentStatus":"Current","AssessmentReason":null}}

#### Recorded answer

The primary surface is the .NET in-process library API. Supporting machine-facing surfaces are host-integrated Healthcheck HTTP reporting, local pipe protocol and matching non-core probe executable. Type Manager supplies build-time inventory generation and runtime catalog/query APIs. Console/file logging are output capabilities used by a host, not standalone application frontends.

Use existing C# `net10.0`, pinned SDK `10.0.401`, selected .NET/ASP.NET runtime `10.0.12`, nullable analysis and warnings as errors. Maintain one repository, one `CIS.Core` package/assembly and module namespaces `CIS.Logging`, `CIS.Logging.MicrosoftLogger`, `CIS.TypeManagement` and `CIS.Healthcheck`. The product is a modular reusable library; there is no new server process, distributed-service topology, application database or UI deployable.

Applications own startup/shutdown, option inputs, external resources, listeners, deployment and background work. Logging exposes bounded capture/queue behaviour and independent backend output paths. The consumer processes queued occurrences and writers own only their documented resources. Preserve callback/provider/scope ownership and recoverable-failure containment; caller expressions and fatal process failures retain their documented limits.

Initial platform lanes remain Windows 11 x64 and Ubuntu 24.04 x64. Type Manager's supported layouts remain framework-dependent untrimmed .NET 10 conventional build/folder publish on Windows/Linux x64. No AOT, trimming, single-file, self-contained, arbitrary load-context or Visual Studio/MSBuild.exe hosting claim is added. Exact package and platform qualification remain open where not actually recorded for the current revision.

CISLIB supplies reusable application foundations through one C# `CIS.Core` package and assembly: environment values and Logging, Type Manager, and Healthcheck. Applications retain ownership of startup, shutdown, business execution, deployment and external resources. The product is independent of Change Impact Studio (CIS), collector SDKs and application orchestration. Its MIT licence and official maintainer policy remain unchanged. [S01, S02, S03]

The current implemented language is C# on .NET 10. One `CIS.Core` package/assembly contains `CIS.Logging`, `CIS.Logging.MicrosoftLogger`, `CIS.TypeManagement` and `CIS.Healthcheck`, plus the existing environment support. HTTP Healthcheck integration stays inside Core under the user-approved ASP.NET shared-framework dependency. `CIS.TypeManager.Build` is packaged build machinery, not a second application runtime or independently deployed service. [S01, S02, S03, S06]

Developers use ordinary .NET restore/build/test/pack and IDE MSTest support. Product-specific assertions and fixtures protect actual behaviour and accepted boundaries. Release work separately qualifies the required native families, platform/package profiles, conservative coverage and mutation obligations before any publication claim. [S01, S02, S07]

- Keep direct, readable C# with nullable analysis and warnings as errors. Use the retained native MSTest/VSTest workflow and ordinary IDE support. [S01, S07]

- Windows 11 x64 and Ubuntu 24.04 x64 are the selected initial qualification lanes. Supported Type Manager deployment is framework-dependent, untrimmed .NET 10 in conventional build or folder-publish layouts. Actual qualification remains to be recorded for the current revision. [S01, S05]

Use `net10.0`, SDK `10.0.401` and the selected .NET/ASP.NET qualification runtime `10.0.12`. The native ApiCompat tool separately requires .NET `8.0.31`; that tool-host requirement does not retarget the library. Keep existing namespaces, assembly identity `CIS.Core`, neutral culture, unsigned assembly and `AssemblyVersion` `1.0.0.0`. [S01–S03, S07]

The existing two approved runtime NuGets remain `Microsoft.Extensions.Logging.Abstractions` and `Microsoft.Extensions.DependencyInjection.Abstractions`, both `10.0.12`, with their recorded closure. Earlier zero-package, separate-bridge and optional-ASP.NET proposals are superseded by current repository decisions. [S01, S03]

### TI-Q-004 — Architecture style

What high-level architecture style should the product use?

- Why this is needed: The first technical intent should fix dependency direction and deployment boundaries without prematurely designing every component.
- Common options: Modular monolith; Microservices; Serverless functions; Event-driven services; Client-only application; Extend existing architecture
- Advisory starting direction: Default to a modular monolith with explicit module boundaries; split a service only for independent ownership, scaling, security, or deployment needs.
- Status: Derived
- Answered by: CIS deterministic repository analysis
- Answered at: Not recorded
- Resolution source: repository-evidence
- Confidence: medium
- Evidence: cislib:implementations; cislib:implementations/csharp/examples/type-manager/Shared/SampleVerification.cs; cislib:implementations/csharp/verification/consumers/logging-candidate/Program.cs; cislib:implementations/csharp/verification/contracts/type-manager/v1-behavior-development/source/Extra.cs; cislib:implementations/csharp/verification/contracts/type-manager/v1-behavior-development/source/Program.cs; cislib:implementations/csharp/verification/contracts/type-manager/v1-development/source/Program.cs; cislib:implementations/csharp/eng/CIS.TypeManager.Build; cislib:implementations/csharp/eng/CIS.TypeManager.Build/CIS.TypeManager.Build.csproj; cislib:implementations/csharp/examples/healthcheck/CountryList; cislib:implementations/csharp/examples/healthcheck/CountryList/CountryList.csproj; cislib:ASP.NET Core Web SDK or host markers; cislib:implementations/csharp/examples/logging/ConsoleHuman
- Assessment record: {"SupportingBasis":{"BRD:Functional requirements:6":"sha256:3660d8249b1dea45b605dd4559bab01f332dd05e27f2cab3e4083a0dd4f89284","BRD:unmapped:Business outcomes:0":"sha256:63f06b10d37e84d466481fa587bdb9285edd597c5efcf41b5cbc189008dceee3","BRD:unmapped:Canonical registration:0":"sha256:26027a377571f4b99f4595c7fc8a91cced04dacd4f04e6bfbc1b54ce48cf4ad9","BRD:unmapped:Functional requirements:1":"sha256:cbc46b87f50780d62831f4680c2942455d58d98de6ef10d840bfad06c3913a3d","BRD:unmapped:Functional requirements:3":"sha256:7f7984db9040b8819b7deb8ecd19c6fecdbf10931f9b47035ab908dcb4692892","BRD:unmapped:Functional requirements:5":"sha256:89428a5f0e3c2b87c8bef5b7c0ce5f21cc120d3ab446c18fc3e7eda89c64504d","BRD:unmapped:Open questions:0":"sha256:950ebf522c1271f11155a3c349a1188af4c26fbdff01b5d607cb121e1e9c1dec","BRD:unmapped:Stakeholders and actors:0":"sha256:4660f0a5b083676cceac105f14ca48f61178e358042b7c9a61ef24cbf2f07561","BRD:unmapped:Stakeholders and actors:4":"sha256:72bdf5d075aff8ac31d1694ec34df8fe41568095626a576e1c6c1ad6ce737cad","BRD:unmapped:Success measures:1":"sha256:d829372214a932a83be4a13b7b23beab9c46997216a895f190cb1525eef54dcd","BRD:unmapped:Traceability:1":"sha256:418faf927fe56611e48197e2de8f82df3731c0c7ad88c1b5552278f21180985c","BRD:unmapped:Traceability:4":"sha256:f656d3ff988a225e19a90670466df23d06fb528991e268dbd10bb82405e6cc16","BRD:unmapped:Traceability:8":"sha256:2831ac927fc003681fcadb517dbdf6eea8891c2710e010ccbca60d6e35bc49dc"},"Status":"Current","Reason":null,"History":[]}

#### Recorded answer

Extend the existing monorepo architecture and preserve its classified component boundaries; do not introduce independently deployed services without an evidenced ownership, scale, security, or release need.

### TI-Q-005 — Code and ownership topology

How should components be divided across repositories and deployable units?

- Why this is needed: Repository layout should follow ownership and release boundaries rather than create accidental distributed-system complexity.
- Common options: Single repository and deployable; Monorepo with multiple deployables; Separate frontend/backend/infra repositories; Service-per-repository; Extend current repository topology
- Advisory starting direction: Keep the smallest topology that supports independent ownership and release. Record one owner for every component and deployable.
- Status: Derived
- Answered by: Not recorded
- Answered at: Not recorded
- Resolution source: accepted-document
- Confidence: source-explicit
- Evidence: docs/specs/technical-intent-spec.md:20 — CISLIB product technical intent (sha256:3f8b6e04a743a1b4bc067f8730dc1ea9b3b538da92ac22b0745b4f942f9c156c); docs/specs/technical-intent-spec.md:56 — Backend technology and topology (sha256:3f8b6e04a743a1b4bc067f8730dc1ea9b3b538da92ac22b0745b4f942f9c156c); docs/specs/business-requirements.md:48 — Stakeholders and actors (sha256:7e0e542c011a0fa65c23ce63e305f4a62cd4c4c607d73c05b3a0bc46f2fcf042); docs/specs/business-requirements.md:87 — Constraints and assumptions (sha256:7e0e542c011a0fa65c23ce63e305f4a62cd4c4c607d73c05b3a0bc46f2fcf042)
- Assessment record: {"SupportingBasis":{"BRD:Constraints and assumptions:1":"sha256:d36f3f8506b6a2d61d89fbe5aadc9a1f391a0e098a0521080a69365135f92576","BRD:Stakeholders and actors:2":"sha256:8fc074a3ff7ce5bd69a64abfa420e6163dcf671939c4ce188a7e2f58e5bac2ac","BRD:unmapped:Business outcomes:0":"sha256:63f06b10d37e84d466481fa587bdb9285edd597c5efcf41b5cbc189008dceee3","BRD:unmapped:Canonical registration:0":"sha256:26027a377571f4b99f4595c7fc8a91cced04dacd4f04e6bfbc1b54ce48cf4ad9","BRD:unmapped:Functional requirements:1":"sha256:cbc46b87f50780d62831f4680c2942455d58d98de6ef10d840bfad06c3913a3d","BRD:unmapped:Functional requirements:3":"sha256:7f7984db9040b8819b7deb8ecd19c6fecdbf10931f9b47035ab908dcb4692892","BRD:unmapped:Functional requirements:5":"sha256:89428a5f0e3c2b87c8bef5b7c0ce5f21cc120d3ab446c18fc3e7eda89c64504d","BRD:unmapped:Open questions:0":"sha256:950ebf522c1271f11155a3c349a1188af4c26fbdff01b5d607cb121e1e9c1dec","BRD:unmapped:Stakeholders and actors:0":"sha256:4660f0a5b083676cceac105f14ca48f61178e358042b7c9a61ef24cbf2f07561","BRD:unmapped:Stakeholders and actors:4":"sha256:72bdf5d075aff8ac31d1694ec34df8fe41568095626a576e1c6c1ad6ce737cad","BRD:unmapped:Success measures:1":"sha256:d829372214a932a83be4a13b7b23beab9c46997216a895f190cb1525eef54dcd","BRD:unmapped:Traceability:1":"sha256:418faf927fe56611e48197e2de8f82df3731c0c7ad88c1b5552278f21180985c","BRD:unmapped:Traceability:4":"sha256:f656d3ff988a225e19a90670466df23d06fb528991e268dbd10bb82405e6cc16","BRD:unmapped:Traceability:8":"sha256:2831ac927fc003681fcadb517dbdf6eea8891c2710e010ccbca60d6e35bc49dc"},"Status":"Current","Reason":null,"History":[],"SourceFactAuthority":{"Basis":{"docs/specs/technical-intent-spec.md":"sha256:3502a5e0cf9a35afcff08131afa56921849ac1b088f6aaf5965abcca8f8a9cae","docs/specs/business-requirements.md":"sha256:6484b22372b781e9694e4cd9738bcc72322791bb30884a8f1b1ae0df94eb556a"},"AssessmentStatus":"Current","AssessmentReason":null}}

#### Recorded answer

This registration document maps existing source direction and the cleaned implementation at reviewed revision `f05b02c0c4967bfee2aabb1f4140709923ad5d3a` into one product-level technical baseline. It does not alter accepted requirements, resolve new technical choices or certify release readiness. Canonical technical-intent approval is recorded separately through supported CIS controls in the document metadata. Source paths below are relative to the CISLIB repository.

Use existing C# `net10.0`, pinned SDK `10.0.401`, selected .NET/ASP.NET runtime `10.0.12`, nullable analysis and warnings as errors. Maintain one repository, one `CIS.Core` package/assembly and module namespaces `CIS.Logging`, `CIS.Logging.MicrosoftLogger`, `CIS.TypeManagement` and `CIS.Healthcheck`. The product is a modular reusable library; there is no new server process, distributed-service topology, application database or UI deployable.

| CISLIB maintainer | Maintain the official repository, review changes and publication decisions, preserve actual published compatibility and require appropriate native qualification. |

The existing two approved runtime NuGets remain `Microsoft.Extensions.Logging.Abstractions` and `Microsoft.Extensions.DependencyInjection.Abstractions`, both `10.0.12`, with their recorded closure. Earlier zero-package, separate-bridge and optional-ASP.NET proposals are superseded by current repository decisions. [S01, S03]

### TI-Q-006 — Primary data store

What should be the primary transactional system of record?

- Why this is needed: Consistency, migrations, concurrency, backup, restore, and operational cost depend on this choice.
- Common options: SQLite; PostgreSQL; SQL Server; MySQL/MariaDB; Managed document database; No durable application data
- Advisory starting direction: No transactional application system of record is introduced. Logging files and Type Manager manifests have their existing diagnostic/deployment roles; they are not inferred databases. Unrequested caches/search/object stores and AI/model execution do not become selected architecture.
- Status: Unanswered
- Answered by: Not recorded
- Answered at: Not recorded
- Resolution source: technical-intent-document
- Confidence: Not assessed
- Evidence: docs/specs/technical-intent-spec.md:87 — Application model and invariants (sha256:3f8b6e04a743a1b4bc067f8730dc1ea9b3b538da92ac22b0745b4f942f9c156c)
- Assessment record: {"SupportingBasis":{"BRD:unmapped:Business outcomes:0":"sha256:63f06b10d37e84d466481fa587bdb9285edd597c5efcf41b5cbc189008dceee3","BRD:unmapped:Canonical registration:0":"sha256:26027a377571f4b99f4595c7fc8a91cced04dacd4f04e6bfbc1b54ce48cf4ad9","BRD:unmapped:Functional requirements:1":"sha256:cbc46b87f50780d62831f4680c2942455d58d98de6ef10d840bfad06c3913a3d","BRD:unmapped:Functional requirements:3":"sha256:7f7984db9040b8819b7deb8ecd19c6fecdbf10931f9b47035ab908dcb4692892","BRD:unmapped:Functional requirements:5":"sha256:89428a5f0e3c2b87c8bef5b7c0ce5f21cc120d3ab446c18fc3e7eda89c64504d","BRD:unmapped:Open questions:0":"sha256:950ebf522c1271f11155a3c349a1188af4c26fbdff01b5d607cb121e1e9c1dec","BRD:unmapped:Stakeholders and actors:0":"sha256:4660f0a5b083676cceac105f14ca48f61178e358042b7c9a61ef24cbf2f07561","BRD:unmapped:Stakeholders and actors:4":"sha256:72bdf5d075aff8ac31d1694ec34df8fe41568095626a576e1c6c1ad6ce737cad","BRD:unmapped:Success measures:1":"sha256:d829372214a932a83be4a13b7b23beab9c46997216a895f190cb1525eef54dcd","BRD:unmapped:Traceability:1":"sha256:418faf927fe56611e48197e2de8f82df3731c0c7ad88c1b5552278f21180985c","BRD:unmapped:Traceability:4":"sha256:f656d3ff988a225e19a90670466df23d06fb528991e268dbd10bb82405e6cc16","BRD:unmapped:Traceability:8":"sha256:2831ac927fc003681fcadb517dbdf6eea8891c2710e010ccbca60d6e35bc49dc"},"Status":"Current","Reason":null,"History":[]}

#### Recorded answer

Not answered.

### TI-Q-007 — Supporting data services

Which additional data services are required?

- Why this is needed: Caches, search, object storage, analytics, and graph stores add separate ownership and recovery obligations.
- Common options: None; Distributed cache; Search index; Object/blob storage; Graph database; Analytics warehouse; Vector store
- Advisory starting direction: Add no supporting store until a BRD outcome or measurable quality constraint requires it; every derived store needs a rebuild path.
- Status: Derived
- Answered by: Not recorded
- Answered at: Not recorded
- Resolution source: accepted-document
- Confidence: source-explicit
- Evidence: docs/specs/technical-intent-spec.md:65 — Runtime architecture and trust boundaries (sha256:3f8b6e04a743a1b4bc067f8730dc1ea9b3b538da92ac22b0745b4f942f9c156c); docs/specs/technical-intent-spec.md:83 — Application model and invariants (sha256:3f8b6e04a743a1b4bc067f8730dc1ea9b3b538da92ac22b0745b4f942f9c156c); docs/specs/technical-intent-spec.md:87 — Application model and invariants (sha256:3f8b6e04a743a1b4bc067f8730dc1ea9b3b538da92ac22b0745b4f942f9c156c); docs/specs/business-requirements.md:55 — Business capabilities and processes (sha256:7e0e542c011a0fa65c23ce63e305f4a62cd4c4c607d73c05b3a0bc46f2fcf042); docs/specs/business-requirements.md:68 — Functional requirements (sha256:7e0e542c011a0fa65c23ce63e305f4a62cd4c4c607d73c05b3a0bc46f2fcf042)
- Assessment record: {"SupportingBasis":{"BRD:Business capabilities and processes:0":"sha256:226405eaf82695481c2a8bb5a3683692e6cc6f41ca945ea41ff643a998f05bed","BRD:Functional requirements:4":"sha256:3ccfec7cb1cc9b4805f010b45dd5b8e5897c474dd0991de912c7f9a8a96f52f9","BRD:unmapped:Business outcomes:0":"sha256:63f06b10d37e84d466481fa587bdb9285edd597c5efcf41b5cbc189008dceee3","BRD:unmapped:Canonical registration:0":"sha256:26027a377571f4b99f4595c7fc8a91cced04dacd4f04e6bfbc1b54ce48cf4ad9","BRD:unmapped:Functional requirements:1":"sha256:cbc46b87f50780d62831f4680c2942455d58d98de6ef10d840bfad06c3913a3d","BRD:unmapped:Functional requirements:3":"sha256:7f7984db9040b8819b7deb8ecd19c6fecdbf10931f9b47035ab908dcb4692892","BRD:unmapped:Functional requirements:5":"sha256:89428a5f0e3c2b87c8bef5b7c0ce5f21cc120d3ab446c18fc3e7eda89c64504d","BRD:unmapped:Open questions:0":"sha256:950ebf522c1271f11155a3c349a1188af4c26fbdff01b5d607cb121e1e9c1dec","BRD:unmapped:Stakeholders and actors:0":"sha256:4660f0a5b083676cceac105f14ca48f61178e358042b7c9a61ef24cbf2f07561","BRD:unmapped:Stakeholders and actors:4":"sha256:72bdf5d075aff8ac31d1694ec34df8fe41568095626a576e1c6c1ad6ce737cad","BRD:unmapped:Success measures:1":"sha256:d829372214a932a83be4a13b7b23beab9c46997216a895f190cb1525eef54dcd","BRD:unmapped:Traceability:1":"sha256:418faf927fe56611e48197e2de8f82df3731c0c7ad88c1b5552278f21180985c","BRD:unmapped:Traceability:4":"sha256:f656d3ff988a225e19a90670466df23d06fb528991e268dbd10bb82405e6cc16","BRD:unmapped:Traceability:8":"sha256:2831ac927fc003681fcadb517dbdf6eea8891c2710e010ccbca60d6e35bc49dc"},"Status":"Current","Reason":null,"History":[],"SourceFactAuthority":{"Basis":{"docs/specs/technical-intent-spec.md":"sha256:2eb18696ddd95839ef3ee889116cbc4e35f5fbfeaf97688841b26c20fdebe599","docs/specs/business-requirements.md":"sha256:06c2be01655156e4a8a614bea2c272ee94a8668ee2e4a102020cab9fb681f3c5"},"AssessmentStatus":"Current","AssessmentReason":null}}

#### Recorded answer

| Type Manager runtime | Prepare the pre-DI catalog, CLR identities, fixed membership and cached queries. | Core `CIS.TypeManagement` definitions, inventory consumption and read-only queries. | Application/configuration construction, DI registration, dynamic plugins or arbitrary load contexts. | CIS-BR-004 |

Type Manager retains actual CLR type/assembly identity, fixed lifetime membership, deterministic read-only query results and cached valid implementation mappings. Queries do not rescan or construct application objects. Configuration persistence/creation policy remains a consumer or future-module concern.

No transactional application system of record is introduced. Logging files and Type Manager manifests have their existing diagnostic/deployment roles; they are not inferred databases. Unrequested caches/search/object stores and AI/model execution do not become selected architecture.

Applications establish environment/module options, then use Logging to capture and process bounded diagnostic occurrences through independent destinations. Hosts can explicitly observe flush/stop outcomes without confusing admission with delivery. Before building the DI container, a host asks Type Manager to prepare its declared managed application assembly set and uses its completed read-only catalog and cached queries. An application can create Healthcheck state and monitor instances, optionally own collector execution, and publish detached health/readiness reports over the supported host-integrated transports. [S01, S04, S05, S06]

| CIS-BR-004 | Prepare the declared managed assembly set before DI construction, automatically generate the supported host inventory through the existing `CIS.TypeManager.Build` assets, expose actual CLR type identities and fixed prepared membership, and cache valid read-only queries without constructing application objects. | S01, S05 |

### TI-Q-008 — Contracts and integrations

How will clients, components, and external systems communicate and evolve contracts?

- Why this is needed: Interaction style determines compatibility, failure, authentication, idempotency, and test boundaries.
- Common options: Versioned HTTP/JSON APIs; GraphQL; gRPC; Events/messages; Files/batches; In-process module contracts; Platform-native SDKs
- Advisory starting direction: Contract or integration evidence is detected in cislib/countrylist: csharp, aspnet-core, backend-api-producer. Preserve its supported compatibility policy unless this intent explicitly changes it.
- Status: Derived
- Answered by: CIS deterministic repository analysis
- Answered at: Not recorded
- Resolution source: repository-evidence
- Confidence: medium
- Evidence: cislib:implementations/csharp/examples/healthcheck/CountryList; cislib:implementations/csharp/examples/healthcheck/CountryList/CountryList.csproj; cislib:ASP.NET Core Web SDK or host markers
- Assessment record: {"SupportingBasis":{"BRD:Business outcomes:2":"sha256:d0b906480acd99a1990bd1caa3462ed7ed6fed09dfb3e787dd9e187e3418e037","BRD:Constraints and assumptions:2":"sha256:67728cf5261d1f576b66ed3e81ac8f0c26fb5ffbe7da0ed2d75e6e8d2625adc5","BRD:Executive summary:1":"sha256:bb1e1ea426a5d6bfa91c15652ea6071c2fe73a819d19a5c9c9dd039ed486379a","BRD:Functional requirements:0":"sha256:ec9cabb39449bdabc062fb8dd2e698a9385c70119a1fc0e603af1f35b3b0564b","BRD:Functional requirements:6":"sha256:3660d8249b1dea45b605dd4559bab01f332dd05e27f2cab3e4083a0dd4f89284","BRD:Functional requirements:7":"sha256:0c4b37a6d7f28d53ee32843e52ffc196ebae854fd0880649a6ddc28bd62b3b01","BRD:Functional requirements:8":"sha256:7bb0d6920904acdab94cfd03e5b1d8190600fe4c6d6fe7710129de42c101d381","BRD:Scope:0":"sha256:60b07e4d08d635a2449d464752ccea2e66918874d14d8a359482596006a1f448","BRD:Scope:1":"sha256:e70d46816a42a372b1d95c2f923b3d85825216021d4e7e6c6f182dbf5ed08c64","BRD:Scope:2":"sha256:1c7f59dcbd3c2655a02fcc7ca17c2fae52745bbe27c67ae62167358fd8c2f919","BRD:Stakeholders and actors:2":"sha256:8fc074a3ff7ce5bd69a64abfa420e6163dcf671939c4ce188a7e2f58e5bac2ac","BRD:Success measures:0":"sha256:b7d04b090dc6d9822bf9cd397a7c3e7143e0382032d24d37140b2976a58d9f58","BRD:Traceability:0":"sha256:097d16ea0a3f065dfe47991ea7ef86035f418dbe88eb9be22a2f2f089bd9045c","BRD:Traceability:2":"sha256:d3363aa9959b5fbd8571f4728d58a887f7fa990b3dc0535da468426df1f81222","BRD:Traceability:5":"sha256:a5560f4cd7bc5fa577e926a090089d82e69ab801fe814a581a14850bf2e88fa4","BRD:unmapped:Business outcomes:0":"sha256:63f06b10d37e84d466481fa587bdb9285edd597c5efcf41b5cbc189008dceee3","BRD:unmapped:Canonical registration:0":"sha256:26027a377571f4b99f4595c7fc8a91cced04dacd4f04e6bfbc1b54ce48cf4ad9","BRD:unmapped:Functional requirements:1":"sha256:cbc46b87f50780d62831f4680c2942455d58d98de6ef10d840bfad06c3913a3d","BRD:unmapped:Functional requirements:3":"sha256:7f7984db9040b8819b7deb8ecd19c6fecdbf10931f9b47035ab908dcb4692892","BRD:unmapped:Functional requirements:5":"sha256:89428a5f0e3c2b87c8bef5b7c0ce5f21cc120d3ab446c18fc3e7eda89c64504d","BRD:unmapped:Open questions:0":"sha256:950ebf522c1271f11155a3c349a1188af4c26fbdff01b5d607cb121e1e9c1dec","BRD:unmapped:Stakeholders and actors:0":"sha256:4660f0a5b083676cceac105f14ca48f61178e358042b7c9a61ef24cbf2f07561","BRD:unmapped:Stakeholders and actors:4":"sha256:72bdf5d075aff8ac31d1694ec34df8fe41568095626a576e1c6c1ad6ce737cad","BRD:unmapped:Success measures:1":"sha256:d829372214a932a83be4a13b7b23beab9c46997216a895f190cb1525eef54dcd","BRD:unmapped:Traceability:1":"sha256:418faf927fe56611e48197e2de8f82df3731c0c7ad88c1b5552278f21180985c","BRD:unmapped:Traceability:4":"sha256:f656d3ff988a225e19a90670466df23d06fb528991e268dbd10bb82405e6cc16","BRD:unmapped:Traceability:8":"sha256:2831ac927fc003681fcadb517dbdf6eea8891c2710e010ccbca60d6e35bc49dc"},"Status":"Current","Reason":null,"History":[]}

#### Recorded answer

Preserve the detected interaction styles: versioned HTTP/API contracts, in-process component contracts. Maintain explicit compatibility, authentication, failure, idempotency, and test contracts at every cross-component boundary.

### TI-Q-009 — Identity and access

What identity, authentication, authorization, and tenancy model is required?

- Why this is needed: Identity is a trust boundary and must be selected before exposing components or data flows.
- Common options: No identity; Managed OIDC/OAuth; Self-hosted identity provider; Enterprise SSO; Anonymous/local identity; Single tenant; Multi-tenant
- Advisory starting direction: Use a maintained standards-based identity provider when authentication is required; enforce authorization and object ownership at the trusted backend or platform boundary.
- Status: Derived
- Answered by: Not recorded
- Answered at: Not recorded
- Resolution source: accepted-document
- Confidence: source-explicit
- Evidence: docs/specs/technical-intent-spec.md:79 — Application model and invariants (sha256:3f8b6e04a743a1b4bc067f8730dc1ea9b3b538da92ac22b0745b4f942f9c156c); docs/specs/technical-intent-spec.md:83 — Application model and invariants (sha256:3f8b6e04a743a1b4bc067f8730dc1ea9b3b538da92ac22b0745b4f942f9c156c); docs/specs/technical-intent-spec.md:94 — API, integration, and compatibility intent (sha256:3f8b6e04a743a1b4bc067f8730dc1ea9b3b538da92ac22b0745b4f942f9c156c); docs/specs/technical-intent-spec.md:99 — API, integration, and compatibility intent (sha256:3f8b6e04a743a1b4bc067f8730dc1ea9b3b538da92ac22b0745b4f942f9c156c); docs/specs/technical-intent-spec.md:107 — Security and privacy intent (sha256:3f8b6e04a743a1b4bc067f8730dc1ea9b3b538da92ac22b0745b4f942f9c156c); docs/specs/business-requirements.md:40 — Scope (sha256:7e0e542c011a0fa65c23ce63e305f4a62cd4c4c607d73c05b3a0bc46f2fcf042); docs/specs/business-requirements.md:66 — Functional requirements (sha256:7e0e542c011a0fa65c23ce63e305f4a62cd4c4c607d73c05b3a0bc46f2fcf042); docs/specs/business-requirements.md:85 — Constraints and assumptions (sha256:7e0e542c011a0fa65c23ce63e305f4a62cd4c4c607d73c05b3a0bc46f2fcf042); docs/specs/business-requirements.md:91 — Constraints and assumptions (sha256:7e0e542c011a0fa65c23ce63e305f4a62cd4c4c607d73c05b3a0bc46f2fcf042); docs/specs/business-requirements.md:118 — Open questions (sha256:7e0e542c011a0fa65c23ce63e305f4a62cd4c4c607d73c05b3a0bc46f2fcf042)
- Assessment record: {"SupportingBasis":{"BRD:Constraints and assumptions:0":"sha256:1b6487cd2655d7c2bfcdd9dfd49cfff6170e66945b808ca530cb380ad3cabb03","BRD:Constraints and assumptions:3":"sha256:071aa05f8fea7bcc341b2cf14cb5eca4b9db0eab9d813599577c818c0705a1e1","BRD:Functional requirements:2":"sha256:a5dc5c727aaa351960252e17dc674470e26df52507d7fbe675cd80809bb43598","BRD:Open questions:1":"sha256:f1b12ce6c137a66ab8194d88080d73c7d43994db2f130589042f494f600073eb","BRD:Scope:2":"sha256:1c7f59dcbd3c2655a02fcc7ca17c2fae52745bbe27c67ae62167358fd8c2f919","BRD:unmapped:Business outcomes:0":"sha256:63f06b10d37e84d466481fa587bdb9285edd597c5efcf41b5cbc189008dceee3","BRD:unmapped:Canonical registration:0":"sha256:26027a377571f4b99f4595c7fc8a91cced04dacd4f04e6bfbc1b54ce48cf4ad9","BRD:unmapped:Functional requirements:1":"sha256:cbc46b87f50780d62831f4680c2942455d58d98de6ef10d840bfad06c3913a3d","BRD:unmapped:Functional requirements:3":"sha256:7f7984db9040b8819b7deb8ecd19c6fecdbf10931f9b47035ab908dcb4692892","BRD:unmapped:Functional requirements:5":"sha256:89428a5f0e3c2b87c8bef5b7c0ce5f21cc120d3ab446c18fc3e7eda89c64504d","BRD:unmapped:Open questions:0":"sha256:950ebf522c1271f11155a3c349a1188af4c26fbdff01b5d607cb121e1e9c1dec","BRD:unmapped:Stakeholders and actors:0":"sha256:4660f0a5b083676cceac105f14ca48f61178e358042b7c9a61ef24cbf2f07561","BRD:unmapped:Stakeholders and actors:4":"sha256:72bdf5d075aff8ac31d1694ec34df8fe41568095626a576e1c6c1ad6ce737cad","BRD:unmapped:Success measures:1":"sha256:d829372214a932a83be4a13b7b23beab9c46997216a895f190cb1525eef54dcd","BRD:unmapped:Traceability:1":"sha256:418faf927fe56611e48197e2de8f82df3731c0c7ad88c1b5552278f21180985c","BRD:unmapped:Traceability:4":"sha256:f656d3ff988a225e19a90670466df23d06fb528991e268dbd10bb82405e6cc16","BRD:unmapped:Traceability:8":"sha256:2831ac927fc003681fcadb517dbdf6eea8891c2710e010ccbca60d6e35bc49dc"},"Status":"Current","Reason":null,"History":[],"SourceFactAuthority":{"Basis":{"docs/specs/technical-intent-spec.md":"sha256:24e39cc97012da02ba246558aa4eeabecf8b6c7134a0b927a3926ad074746123","docs/specs/business-requirements.md":"sha256:2232357c823957895d743c94ec056474185181a0435ab32b1b43a57049ba850e"},"AssessmentStatus":"Current","AssessmentReason":null}}

#### Recorded answer

Environment options retain the selected readonly static container, first-access constructor defaults followed by known overrides and bounded identity interning. Modules own mutable option values and explicit environment resolution; this product has no new global startup, reset or readiness framework.

Type Manager retains actual CLR type/assembly identity, fixed lifetime membership, deterministic read-only query results and cached valid implementation mappings. Queries do not rescan or construct application objects. Configuration persistence/creation policy remains a consumer or future-module concern.

| Host build → Type Manager inventory → runtime | Existing SDK targets/helper produce the versioned manifest from resolved runtime assets for supported layouts; runtime validates host identity/paths/required assets and fails visibly rather than returning a partial catalog. No application code runs during inventory production. |

Exact APIs, enum/JSON shapes, routes, status codes, pipe framing/budgets, authorization rules, options, defaults, timeout/cancellation and recovery conditions remain in `contracts/`, `conformance/`, `planning/healthcheck/contract-catalog.md`, `execution-facts.md`, applicable addenda and current consumer guides. This registration adds no endpoint, authentication scheme, retry, persistence or compatibility promise.

Healthcheck detail access and local pipe authorization/endpoint protections retain the current transport contracts; this baseline does not infer missing privileged/cross-user/platform qualification. Logging remains diagnostic rather than a transactional audit guarantee. No new product identity provider, tenancy model, regulated-data guarantee, credential store or compliance certification is selected by registration.

Current scope preserves existing Logging/environment behaviour, automatic Type Manager inventory and prepared queries, Healthcheck policies/collection/execution-fact binding and reporting, native verification, consumer examples and portable contracts. Full qualification, publication and feature-gap assessment are separate work. Heartbeat scheduler and generic startup-provider integrations remain absent. Configuration, wider Startup Pipeline work, later languages and other future specifications are retained without being labelled implemented or authorized by this registration. [S01, S02, S05, S06]

| CIS-BR-002 | Preserve the user-selected readonly static `EnvironmentOptions` entry point, constructor defaults followed by known overrides, bounded identity interning and module-owned explicit option resolution. | S01, S02 |

Use `net10.0`, SDK `10.0.401` and the selected .NET/ASP.NET qualification runtime `10.0.12`. The native ApiCompat tool separately requires .NET `8.0.31`; that tool-host requirement does not retarget the library. Keep existing namespaces, assembly identity `CIS.Core`, neutral culture, unsigned assembly and `AssemblyVersion` `1.0.0.0`. [S01–S03, S07]

Registration does not authorize future language ports, Configuration, Heartbeat implementation, broader engineering-policy adoption, full release qualification or publication. Retired campaign time/storage/attempt ceilings keep their original scope and supply no adoption capacity. [S01]

Canonical product-baseline approval, source assessment and native work allocation are distinct records. The authorized retained Logging source assessment is recorded below; CIS accounting capabilities are delivered separately. This document grants no implementation, execution, release or allocation authority.

### TI-Q-010 — Hosting and deployment

Where and how will the product run?

- Why this is needed: Runtime topology defines networks, secrets, configuration, availability, release, cost, and recovery responsibilities.
- Common options: Local/self-hosted containers; Provider-neutral containers; Managed cloud application platform; Kubernetes; Serverless; Mobile stores; Static hosting; Hybrid
- Advisory starting direction: Stay provider neutral at product level unless a requirement selects a provider; define local development, test, staging, and production topology separately.
- Status: Derived
- Answered by: Not recorded
- Answered at: Not recorded
- Resolution source: accepted-document
- Confidence: source-explicit
- Evidence: docs/specs/technical-intent-spec.md:56 — Backend technology and topology (sha256:3f8b6e04a743a1b4bc067f8730dc1ea9b3b538da92ac22b0745b4f942f9c156c); docs/specs/technical-intent-spec.md:67 — Runtime architecture and trust boundaries (sha256:3f8b6e04a743a1b4bc067f8730dc1ea9b3b538da92ac22b0745b4f942f9c156c); docs/specs/technical-intent-spec.md:69 — Runtime architecture and trust boundaries (sha256:3f8b6e04a743a1b4bc067f8730dc1ea9b3b538da92ac22b0745b4f942f9c156c); docs/specs/technical-intent-spec.md:71 — Runtime architecture and trust boundaries (sha256:3f8b6e04a743a1b4bc067f8730dc1ea9b3b538da92ac22b0745b4f942f9c156c); docs/specs/technical-intent-spec.md:79 — Application model and invariants (sha256:3f8b6e04a743a1b4bc067f8730dc1ea9b3b538da92ac22b0745b4f942f9c156c); docs/specs/technical-intent-spec.md:87 — Application model and invariants (sha256:3f8b6e04a743a1b4bc067f8730dc1ea9b3b538da92ac22b0745b4f942f9c156c); docs/specs/technical-intent-spec.md:105 — Security and privacy intent (sha256:3f8b6e04a743a1b4bc067f8730dc1ea9b3b538da92ac22b0745b4f942f9c156c); docs/specs/technical-intent-spec.md:111 — Operations, migration, and recovery intent (sha256:3f8b6e04a743a1b4bc067f8730dc1ea9b3b538da92ac22b0745b4f942f9c156c); docs/specs/technical-intent-spec.md:113 — Operations, migration, and recovery intent (sha256:3f8b6e04a743a1b4bc067f8730dc1ea9b3b538da92ac22b0745b4f942f9c156c); docs/specs/business-requirements.md:23 — Executive summary (sha256:7e0e542c011a0fa65c23ce63e305f4a62cd4c4c607d73c05b3a0bc46f2fcf042); docs/specs/business-requirements.md:36 — Scope (sha256:7e0e542c011a0fa65c23ce63e305f4a62cd4c4c607d73c05b3a0bc46f2fcf042); docs/specs/business-requirements.md:47 — Stakeholders and actors (sha256:7e0e542c011a0fa65c23ce63e305f4a62cd4c4c607d73c05b3a0bc46f2fcf042); docs/specs/business-requirements.md:55 — Business capabilities and processes (sha256:7e0e542c011a0fa65c23ce63e305f4a62cd4c4c607d73c05b3a0bc46f2fcf042); docs/specs/business-requirements.md:70 — Functional requirements (sha256:7e0e542c011a0fa65c23ce63e305f4a62cd4c4c607d73c05b3a0bc46f2fcf042); docs/specs/business-requirements.md:79 — Quality, regulatory, and operational requirements (sha256:7e0e542c011a0fa65c23ce63e305f4a62cd4c4c607d73c05b3a0bc46f2fcf042); docs/specs/business-requirements.md:81 — Quality, regulatory, and operational requirements (sha256:7e0e542c011a0fa65c23ce63e305f4a62cd4c4c607d73c05b3a0bc46f2fcf042); docs/specs/business-requirements.md:89 — Constraints and assumptions (sha256:7e0e542c011a0fa65c23ce63e305f4a62cd4c4c607d73c05b3a0bc46f2fcf042)
- Assessment record: {"SupportingBasis":{"BRD:Business capabilities and processes:0":"sha256:226405eaf82695481c2a8bb5a3683692e6cc6f41ca945ea41ff643a998f05bed","BRD:Constraints and assumptions:2":"sha256:67728cf5261d1f576b66ed3e81ac8f0c26fb5ffbe7da0ed2d75e6e8d2625adc5","BRD:Executive summary:0":"sha256:633454819f1bbba147ac8aaa479e1166301cf6246edfd28dbca6258ea7df0b5d","BRD:Functional requirements:6":"sha256:3660d8249b1dea45b605dd4559bab01f332dd05e27f2cab3e4083a0dd4f89284","BRD:Quality, regulatory, and operational requirements:3":"sha256:a4ae05c407c91ca6361b12c49af1a15f97949f8073b77ccc006eac9e3a59b9ac","BRD:Quality, regulatory, and operational requirements:5":"sha256:1a61168d2f87248ced8b1eacb9fa2f6372b150938bd4769205b95646bf0e5ab3","BRD:Scope:0":"sha256:60b07e4d08d635a2449d464752ccea2e66918874d14d8a359482596006a1f448","BRD:Stakeholders and actors:1":"sha256:04d7cb14d188e1debe31c048cf4f2f25c3cd966e22596d37ae31612b5009a84c","BRD:unmapped:Business outcomes:0":"sha256:63f06b10d37e84d466481fa587bdb9285edd597c5efcf41b5cbc189008dceee3","BRD:unmapped:Canonical registration:0":"sha256:26027a377571f4b99f4595c7fc8a91cced04dacd4f04e6bfbc1b54ce48cf4ad9","BRD:unmapped:Functional requirements:1":"sha256:cbc46b87f50780d62831f4680c2942455d58d98de6ef10d840bfad06c3913a3d","BRD:unmapped:Functional requirements:3":"sha256:7f7984db9040b8819b7deb8ecd19c6fecdbf10931f9b47035ab908dcb4692892","BRD:unmapped:Functional requirements:5":"sha256:89428a5f0e3c2b87c8bef5b7c0ce5f21cc120d3ab446c18fc3e7eda89c64504d","BRD:unmapped:Open questions:0":"sha256:950ebf522c1271f11155a3c349a1188af4c26fbdff01b5d607cb121e1e9c1dec","BRD:unmapped:Stakeholders and actors:0":"sha256:4660f0a5b083676cceac105f14ca48f61178e358042b7c9a61ef24cbf2f07561","BRD:unmapped:Stakeholders and actors:4":"sha256:72bdf5d075aff8ac31d1694ec34df8fe41568095626a576e1c6c1ad6ce737cad","BRD:unmapped:Success measures:1":"sha256:d829372214a932a83be4a13b7b23beab9c46997216a895f190cb1525eef54dcd","BRD:unmapped:Traceability:1":"sha256:418faf927fe56611e48197e2de8f82df3731c0c7ad88c1b5552278f21180985c","BRD:unmapped:Traceability:4":"sha256:f656d3ff988a225e19a90670466df23d06fb528991e268dbd10bb82405e6cc16","BRD:unmapped:Traceability:8":"sha256:2831ac927fc003681fcadb517dbdf6eea8891c2710e010ccbca60d6e35bc49dc"},"Status":"Current","Reason":null,"History":[],"SourceFactAuthority":{"Basis":{"docs/specs/technical-intent-spec.md":"sha256:3ef133bd11f86633562cf33d472e7d1aa5aaeb927bcefa7b74e109050ccd802f","docs/specs/business-requirements.md":"sha256:e1ddb7e1c5872e6cf30750022c2542e218885ae3ee70ad5540a36f7df151b32b"},"AssessmentStatus":"Current","AssessmentReason":null}}

#### Recorded answer

Use existing C# `net10.0`, pinned SDK `10.0.401`, selected .NET/ASP.NET runtime `10.0.12`, nullable analysis and warnings as errors. Maintain one repository, one `CIS.Core` package/assembly and module namespaces `CIS.Logging`, `CIS.Logging.MicrosoftLogger`, `CIS.TypeManagement` and `CIS.Healthcheck`. The product is a modular reusable library; there is no new server process, distributed-service topology, application database or UI deployable.

| Type Manager inventory build helper | Generate the supported host assembly inventory from SDK-resolved assets. | Existing `CIS.TypeManager.Build` project and Core package SDK/targets; build-only machinery. | Runtime application dependencies/instantiation, separately deployed services or unsupported publish profiles. | CIS-BR-004/008 |

Applications own startup/shutdown, option inputs, external resources, listeners, deployment and background work. Logging exposes bounded capture/queue behaviour and independent backend output paths. The consumer processes queued occurrences and writers own only their documented resources. Preserve callback/provider/scope ownership and recoverable-failure containment; caller expressions and fatal process failures retain their documented limits.

The host explicitly awaits `TypeManager.CreateAsync` before DI container construction. The runtime validates a stable selected deployment inventory, reuses or loads required assemblies in the default context and builds the completed catalog without constructing application objects or attributes. Missing/stale/unsupported inventory and required load failures reject preparation; no arbitrary-directory or loaded-only fallback is permitted.

Environment options retain the selected readonly static container, first-access constructor defaults followed by known overrides and bounded identity interning. Modules own mutable option values and explicit environment resolution; this product has no new global startup, reset or readiness framework.

No transactional application system of record is introduced. Logging files and Type Manager manifests have their existing diagnostic/deployment roles; they are not inferred databases. Unrequested caches/search/object stores and AI/model execution do not become selected architecture.

Library inputs, local deployment files, application callbacks and transport peers retain their distinct trust boundaries. Type Manager's manifest is deployment metadata, not a hostile-file sandbox or authenticity signature. The deployment must remain stable while preparation runs. Hosts own access, credentials, network exposure, HTTP concurrency and application data policy.

Application hosts own runtime startup/shutdown, deployment, external resources, cancellation requests and recovery policy. Preserve explicit Logging flush/stop outcomes, file recovery, Type Manager immutable preparation reports and Healthcheck truthful detached facts. Retired generated receipts, campaigns and old recovery owners supply neither operational authority nor new capacity.

Initial platform lanes remain Windows 11 x64 and Ubuntu 24.04 x64. Type Manager's supported layouts remain framework-dependent untrimmed .NET 10 conventional build/folder publish on Windows/Linux x64. No AOT, trimming, single-file, self-contained, arbitrary load-context or Visual Studio/MSBuild.exe hosting claim is added. Exact package and platform qualification remain open where not actually recorded for the current revision.

CISLIB supplies reusable application foundations through one C# `CIS.Core` package and assembly: environment values and Logging, Type Manager, and Healthcheck. Applications retain ownership of startup, shutdown, business execution, deployment and external resources. The product is independent of Change Impact Studio (CIS), collector SDKs and application orchestration. Its MIT licence and official maintainer policy remain unchanged. [S01, S02, S03]

The current implemented language is C# on .NET 10. One `CIS.Core` package/assembly contains `CIS.Logging`, `CIS.Logging.MicrosoftLogger`, `CIS.TypeManagement` and `CIS.Healthcheck`, plus the existing environment support. HTTP Healthcheck integration stays inside Core under the user-approved ASP.NET shared-framework dependency. `CIS.TypeManager.Build` is packaged build machinery, not a second application runtime or independently deployed service. [S01, S02, S03, S06]

| Application/operator consumer | Inspect truthful log delivery/flush/stop observations and detached health/readiness reports within the host's access and deployment policy. |

Applications establish environment/module options, then use Logging to capture and process bounded diagnostic occurrences through independent destinations. Hosts can explicitly observe flush/stop outcomes without confusing admission with delivery. Before building the DI container, a host asks Type Manager to prepare its declared managed application assembly set and uses its completed read-only catalog and cached queries. An application can create Healthcheck state and monitor instances, optionally own collector execution, and publish detached health/readiness reports over the supported host-integrated transports. [S01, S04, S05, S06]

| CIS-BR-006 | Preserve bounded Healthcheck serialization, reserved HTTP reporting, local pipe/probe behaviour and host-owned access/deployment boundaries with the exact existing wire and transport contracts. | S06 |

- Windows 11 x64 and Ubuntu 24.04 x64 are the selected initial qualification lanes. Supported Type Manager deployment is framework-dependent, untrimmed .NET 10 in conventional build or folder-publish layouts. Actual qualification remains to be recorded for the current revision. [S01, S05]

- Retain MIT licensing and maintainer-only official publication controls. The source material establishes no sector-specific regulatory certification or new data-residency guarantee. Host deployment and data policy remain the application's responsibility. [S01, S03, S06]

Library operations assume the host supplies stable deployment resources and follows documented ownership and access contracts. Type Manager is not a plugin system, arbitrary load-context manager, hostile-file integrity verifier or application-object factory. Healthcheck is not a scheduler, retry engine, DI container or forced-cancellation mechanism. [S05, S06]

### TI-Q-011 — Asynchronous work

Are background jobs, queues, scheduled work, or domain events required?

- Why this is needed: Asynchrony changes ordering, retry, idempotency, dead-letter, replay, and observability behavior.
- Common options: None; In-process background jobs; Durable queue; Event bus; Scheduled jobs; Workflow engine
- Advisory starting direction: Keep work synchronous unless latency, reliability, fan-out, or scheduling requires an asynchronous boundary; then define delivery and recovery semantics explicitly.
- Status: Derived
- Answered by: Not recorded
- Answered at: Not recorded
- Resolution source: accepted-document
- Confidence: source-explicit
- Evidence: docs/specs/technical-intent-spec.md:38 — Design goals and principles (sha256:3f8b6e04a743a1b4bc067f8730dc1ea9b3b538da92ac22b0745b4f942f9c156c); docs/specs/technical-intent-spec.md:69 — Runtime architecture and trust boundaries (sha256:3f8b6e04a743a1b4bc067f8730dc1ea9b3b538da92ac22b0745b4f942f9c156c); docs/specs/technical-intent-spec.md:149 — Source traceability (sha256:3f8b6e04a743a1b4bc067f8730dc1ea9b3b538da92ac22b0745b4f942f9c156c); docs/specs/business-requirements.md:30 — Business outcomes (sha256:7e0e542c011a0fa65c23ce63e305f4a62cd4c4c607d73c05b3a0bc46f2fcf042); docs/specs/business-requirements.md:106 — Traceability (sha256:7e0e542c011a0fa65c23ce63e305f4a62cd4c4c607d73c05b3a0bc46f2fcf042)
- Assessment record: {"SupportingBasis":{"BRD:Business outcomes:1":"sha256:ed80a8d13c4b73bdb29848f9c514efc151abe7b3227edb9e73953618df78dcc1","BRD:Traceability:3":"sha256:2fc0570903f07ed723aefc54a491c169b4241812ccb2e187229e1461b46b01ce","BRD:unmapped:Business outcomes:0":"sha256:63f06b10d37e84d466481fa587bdb9285edd597c5efcf41b5cbc189008dceee3","BRD:unmapped:Canonical registration:0":"sha256:26027a377571f4b99f4595c7fc8a91cced04dacd4f04e6bfbc1b54ce48cf4ad9","BRD:unmapped:Functional requirements:1":"sha256:cbc46b87f50780d62831f4680c2942455d58d98de6ef10d840bfad06c3913a3d","BRD:unmapped:Functional requirements:3":"sha256:7f7984db9040b8819b7deb8ecd19c6fecdbf10931f9b47035ab908dcb4692892","BRD:unmapped:Functional requirements:5":"sha256:89428a5f0e3c2b87c8bef5b7c0ce5f21cc120d3ab446c18fc3e7eda89c64504d","BRD:unmapped:Open questions:0":"sha256:950ebf522c1271f11155a3c349a1188af4c26fbdff01b5d607cb121e1e9c1dec","BRD:unmapped:Stakeholders and actors:0":"sha256:4660f0a5b083676cceac105f14ca48f61178e358042b7c9a61ef24cbf2f07561","BRD:unmapped:Stakeholders and actors:4":"sha256:72bdf5d075aff8ac31d1694ec34df8fe41568095626a576e1c6c1ad6ce737cad","BRD:unmapped:Success measures:1":"sha256:d829372214a932a83be4a13b7b23beab9c46997216a895f190cb1525eef54dcd","BRD:unmapped:Traceability:1":"sha256:418faf927fe56611e48197e2de8f82df3731c0c7ad88c1b5552278f21180985c","BRD:unmapped:Traceability:4":"sha256:f656d3ff988a225e19a90670466df23d06fb528991e268dbd10bb82405e6cc16","BRD:unmapped:Traceability:8":"sha256:2831ac927fc003681fcadb517dbdf6eea8891c2710e010ccbca60d6e35bc49dc"},"Status":"Current","Reason":null,"History":[],"SourceFactAuthority":{"Basis":{"docs/specs/technical-intent-spec.md":"sha256:8c77a22f87f80ef9f34f90f7e0e02cc0aa8ef1af17eae417a3000f3a551dbb08","docs/specs/business-requirements.md":"sha256:a4991b436cbf5f703572081794675b25a54c8e718c2da35c8a7a084ce2b11dcc"},"AssessmentStatus":"Current","AssessmentReason":null}}

#### Recorded answer

Keep direct, readable code with explicit module and host responsibilities. Keep actual product behaviour and meaningful assertions while using ordinary native development commands. Preserve bounded work, detached observations and truthful failure/settlement facts. A snapshot/report must not mutate health, a wait timeout must not claim underlying work stopped, and a queue admission must not claim durable output delivery.

Applications own startup/shutdown, option inputs, external resources, listeners, deployment and background work. Logging exposes bounded capture/queue behaviour and independent backend output paths. The consumer processes queued occurrences and writers own only their documented resources. Preserve callback/provider/scope ownership and recoverable-failure containment; caller expressions and fatal process failures retain their documented limits.

| Environment and Logging | Current `docs/modules/logging/` guides; `planning/logging/priority-spool/brd-v1.2.md`; `planning/logging/sources/core-library-implementation-guide-v1.md` interpreted through current decisions |

- Preserve predictable host ownership, bounded work and diagnostics with truthful failure and delivery observations. A successful queue admission is not durable delivery; a reported timeout does not prove underlying execution has stopped. [S01, S04, S06]

| S04 | `planning/logging/sources/core-library-implementation-guide-v1.md`, `planning/logging/priority-spool/brd-v1.2.md`, current `docs/modules/logging/` guides: detailed Logging source and selected frontend behaviour; historical names/package assumptions are interpreted through S01. |

### TI-Q-012 — Operations and observability

What operational model, telemetry, and support expectations apply?

- Why this is needed: The architecture must be diagnosable and recoverable, including during automated test and delivery runs.
- Common options: Structured logs; Metrics; Distributed tracing; Health/readiness; Alerts/dashboards; Audit trail; Support runbooks
- Advisory starting direction: This registration document maps existing source direction and the cleaned implementation at reviewed revision `f05b02c0c4967bfee2aabb1f4140709923ad5d3a` into one product-level technical baseline. It does not alter accepted requirements, resolve new technical choices or certify release readiness. Canonical technical-intent approval is recorded separately through supported CIS controls in the document metadata. Source paths below are relative to the CISLIB repository.

| Logging and environment | Preserve bounded diagnostics and explicit lifecycle/delivery observations. | Existing Core environment, `CIS.Logging` and `CIS.Logging.MicrosoftLogger` contracts, options, capture, processing and writers. | Application lifecycle/resources, collector SDKs or a durable business-audit store. | CIS-BR-002/003 |

Environment options retain the selected readonly static container, first-access constructor defaults followed by known overrides and bounded identity interning. Modules own mutable option values and explicit environment resolution; this product has no new global startup, reset or readiness framework.

Healthcheck retains explicit monitor criticality, policy transitions, counts/revisions, safe collection ownership and immutable detached snapshots. Snapshot capture performs no collection or failure replay. Observed exception/runtime text is projected through the existing bounded diagnostic rules; explicit caller inputs retain their own validation contract. A watchdog may affect a caller's wait without gaining health authority or forced cancellation.

No transactional application system of record is introduced. Logging files and Type Manager manifests have their existing diagnostic/deployment roles; they are not inferred databases. Unrequested caches/search/object stores and AI/model execution do not become selected architecture.

Healthcheck detail access and local pipe authorization/endpoint protections retain the current transport contracts; this baseline does not infer missing privileged/cross-user/platform qualification. Logging remains diagnostic rather than a transactional audit guarantee. No new product identity provider, tenancy model, regulated-data guarantee, credential store or compliance certification is selected by registration.

The continuation's prepared native selection is the existing `HealthDiagnosticRuntimeAcceptanceBoundaryTests` class. Source inspection confirms that this class contains one MSTest method, `ValidCallerIdentifiersAndRuntimeExceptionProjectionProduceADetachedDiagnostic`, with `[DataRow(false)]` and `[DataRow(true)]`; the expected result is two cases, not an observed pass. It checks real Unicode sanitization/explicit caller projection and detached bounded diagnostics. Preserve the existing local class filter `FullyQualifiedName~CIS.Tests.UnitTests.Healthcheck.HealthDiagnosticRuntimeAcceptanceBoundaryTests&TestCategory!=PerformanceObservation&TestCategory!=ExtendedStress`. It must run through CIS with `--layer unit`, no `--force`, the actual association and parsed TRX. Recheck the class if a descendant changes its test membership. A successful narrow run is not release qualification. Historical cleanup passes remain historical.

Preserve current dependency/assembly/ownership/compatibility/platform policies in `docs/architecture/`, native test guidance, module contract catalogs and `planning/decisions.md`. Human-readable source requirements retain their original files and identities. No Active CIS standard is fabricated from a documentation metadata status, and optional standards/security capabilities remain explicit follow-up work.

| Healthcheck policy/collection/execution/transport | `planning/healthcheck/technical-intent.md`, `contract-catalog.md`, `execution-facts.md`, `collection-contract-amendment.md`, `execution-contract-amendment.md`, `registration-diagnostic-amendment.md`, current `docs/modules/healthcheck/`, `contracts/healthcheck/v1/` |

- Preserve predictable host ownership, bounded work and diagnostics with truthful failure and delivery observations. A successful queue admission is not durable delivery; a reported timeout does not prove underlying execution has stopped. [S01, S04, S06]

| Application/operator consumer | Inspect truthful log delivery/flush/stop observations and detached health/readiness reports within the host's access and deployment policy. |

Applications establish environment/module options, then use Logging to capture and process bounded diagnostic occurrences through independent destinations. Hosts can explicitly observe flush/stop outcomes without confusing admission with delivery. Before building the DI container, a host asks Type Manager to prepare its declared managed application assembly set and uses its completed read-only catalog and cached queries. An application can create Healthcheck state and monitor instances, optionally own collector execution, and publish detached health/readiness reports over the supported host-integrated transports. [S01, S04, S05, S06]

| S06 | `planning/healthcheck/technical-intent.md`, `contract-catalog.md`, `execution-facts.md`, `collection-contract-amendment.md`, `execution-contract-amendment.md`, `registration-diagnostic-amendment.md`, `docs/modules/healthcheck/`, `contracts/healthcheck/v1/`: retained Healthcheck contracts and applicable amendments. |
- Status: Unanswered
- Answered by: Not recorded
- Answered at: Not recorded
- Resolution source: technical-intent-document
- Confidence: Not assessed
- Evidence: docs/specs/technical-intent-spec.md:20 — CISLIB product technical intent (sha256:3f8b6e04a743a1b4bc067f8730dc1ea9b3b538da92ac22b0745b4f942f9c156c); docs/specs/technical-intent-spec.md:64 — Runtime architecture and trust boundaries (sha256:3f8b6e04a743a1b4bc067f8730dc1ea9b3b538da92ac22b0745b4f942f9c156c); docs/specs/technical-intent-spec.md:79 — Application model and invariants (sha256:3f8b6e04a743a1b4bc067f8730dc1ea9b3b538da92ac22b0745b4f942f9c156c); docs/specs/technical-intent-spec.md:85 — Application model and invariants (sha256:3f8b6e04a743a1b4bc067f8730dc1ea9b3b538da92ac22b0745b4f942f9c156c); docs/specs/technical-intent-spec.md:87 — Application model and invariants (sha256:3f8b6e04a743a1b4bc067f8730dc1ea9b3b538da92ac22b0745b4f942f9c156c); docs/specs/technical-intent-spec.md:107 — Security and privacy intent (sha256:3f8b6e04a743a1b4bc067f8730dc1ea9b3b538da92ac22b0745b4f942f9c156c); docs/specs/technical-intent-spec.md:123 — Quality attributes and verification direction (sha256:3f8b6e04a743a1b4bc067f8730dc1ea9b3b538da92ac22b0745b4f942f9c156c); docs/specs/technical-intent-spec.md:129 — Architecture guidelines and applicable standards (sha256:3f8b6e04a743a1b4bc067f8730dc1ea9b3b538da92ac22b0745b4f942f9c156c); docs/specs/technical-intent-spec.md:151 — Source traceability (sha256:3f8b6e04a743a1b4bc067f8730dc1ea9b3b538da92ac22b0745b4f942f9c156c); docs/specs/business-requirements.md:30 — Business outcomes (sha256:7e0e542c011a0fa65c23ce63e305f4a62cd4c4c607d73c05b3a0bc46f2fcf042); docs/specs/business-requirements.md:47 — Stakeholders and actors (sha256:7e0e542c011a0fa65c23ce63e305f4a62cd4c4c607d73c05b3a0bc46f2fcf042); docs/specs/business-requirements.md:55 — Business capabilities and processes (sha256:7e0e542c011a0fa65c23ce63e305f4a62cd4c4c607d73c05b3a0bc46f2fcf042); docs/specs/business-requirements.md:108 — Traceability (sha256:7e0e542c011a0fa65c23ce63e305f4a62cd4c4c607d73c05b3a0bc46f2fcf042)
- Assessment record: {"SupportingBasis":{"BRD:Business capabilities and processes:0":"sha256:226405eaf82695481c2a8bb5a3683692e6cc6f41ca945ea41ff643a998f05bed","BRD:Business outcomes:1":"sha256:ed80a8d13c4b73bdb29848f9c514efc151abe7b3227edb9e73953618df78dcc1","BRD:Stakeholders and actors:1":"sha256:04d7cb14d188e1debe31c048cf4f2f25c3cd966e22596d37ae31612b5009a84c","BRD:Traceability:5":"sha256:a5560f4cd7bc5fa577e926a090089d82e69ab801fe814a581a14850bf2e88fa4","BRD:unmapped:Business outcomes:0":"sha256:63f06b10d37e84d466481fa587bdb9285edd597c5efcf41b5cbc189008dceee3","BRD:unmapped:Canonical registration:0":"sha256:26027a377571f4b99f4595c7fc8a91cced04dacd4f04e6bfbc1b54ce48cf4ad9","BRD:unmapped:Functional requirements:1":"sha256:cbc46b87f50780d62831f4680c2942455d58d98de6ef10d840bfad06c3913a3d","BRD:unmapped:Functional requirements:3":"sha256:7f7984db9040b8819b7deb8ecd19c6fecdbf10931f9b47035ab908dcb4692892","BRD:unmapped:Functional requirements:5":"sha256:89428a5f0e3c2b87c8bef5b7c0ce5f21cc120d3ab446c18fc3e7eda89c64504d","BRD:unmapped:Open questions:0":"sha256:950ebf522c1271f11155a3c349a1188af4c26fbdff01b5d607cb121e1e9c1dec","BRD:unmapped:Stakeholders and actors:0":"sha256:4660f0a5b083676cceac105f14ca48f61178e358042b7c9a61ef24cbf2f07561","BRD:unmapped:Stakeholders and actors:4":"sha256:72bdf5d075aff8ac31d1694ec34df8fe41568095626a576e1c6c1ad6ce737cad","BRD:unmapped:Success measures:1":"sha256:d829372214a932a83be4a13b7b23beab9c46997216a895f190cb1525eef54dcd","BRD:unmapped:Traceability:1":"sha256:418faf927fe56611e48197e2de8f82df3731c0c7ad88c1b5552278f21180985c","BRD:unmapped:Traceability:4":"sha256:f656d3ff988a225e19a90670466df23d06fb528991e268dbd10bb82405e6cc16","BRD:unmapped:Traceability:8":"sha256:2831ac927fc003681fcadb517dbdf6eea8891c2710e010ccbca60d6e35bc49dc"},"Status":"Current","Reason":null,"History":[]}

#### Recorded answer

Not answered.

### TI-Q-013 — Security, privacy, and compliance

Which data classifications, threats, privacy duties, or compliance constraints shape the design?

- Why this is needed: These constraints can change trust boundaries, storage, retention, audit, hosting, and assurance.
- Common options: Public data; Personal data; Sensitive/regulated data; Secrets; Financial data; Health data; Data residency; No special compliance
- Advisory starting direction: Classify data and actors explicitly, minimize retained sensitive data, use least privilege and default deny, and record any applicable regulatory or residency boundary.
- Status: Derived
- Answered by: Not recorded
- Answered at: Not recorded
- Resolution source: accepted-document
- Confidence: source-explicit
- Evidence: docs/specs/technical-intent-spec.md:105 — Security and privacy intent (sha256:3f8b6e04a743a1b4bc067f8730dc1ea9b3b538da92ac22b0745b4f942f9c156c); docs/specs/technical-intent-spec.md:107 — Security and privacy intent (sha256:3f8b6e04a743a1b4bc067f8730dc1ea9b3b538da92ac22b0745b4f942f9c156c); docs/specs/technical-intent-spec.md:129 — Architecture guidelines and applicable standards (sha256:3f8b6e04a743a1b4bc067f8730dc1ea9b3b538da92ac22b0745b4f942f9c156c)
- Assessment record: {"SupportingBasis":{"BRD:unmapped:Business outcomes:0":"sha256:63f06b10d37e84d466481fa587bdb9285edd597c5efcf41b5cbc189008dceee3","BRD:unmapped:Canonical registration:0":"sha256:26027a377571f4b99f4595c7fc8a91cced04dacd4f04e6bfbc1b54ce48cf4ad9","BRD:unmapped:Functional requirements:1":"sha256:cbc46b87f50780d62831f4680c2942455d58d98de6ef10d840bfad06c3913a3d","BRD:unmapped:Functional requirements:3":"sha256:7f7984db9040b8819b7deb8ecd19c6fecdbf10931f9b47035ab908dcb4692892","BRD:unmapped:Functional requirements:5":"sha256:89428a5f0e3c2b87c8bef5b7c0ce5f21cc120d3ab446c18fc3e7eda89c64504d","BRD:unmapped:Open questions:0":"sha256:950ebf522c1271f11155a3c349a1188af4c26fbdff01b5d607cb121e1e9c1dec","BRD:unmapped:Stakeholders and actors:0":"sha256:4660f0a5b083676cceac105f14ca48f61178e358042b7c9a61ef24cbf2f07561","BRD:unmapped:Stakeholders and actors:4":"sha256:72bdf5d075aff8ac31d1694ec34df8fe41568095626a576e1c6c1ad6ce737cad","BRD:unmapped:Success measures:1":"sha256:d829372214a932a83be4a13b7b23beab9c46997216a895f190cb1525eef54dcd","BRD:unmapped:Traceability:1":"sha256:418faf927fe56611e48197e2de8f82df3731c0c7ad88c1b5552278f21180985c","BRD:unmapped:Traceability:4":"sha256:f656d3ff988a225e19a90670466df23d06fb528991e268dbd10bb82405e6cc16","BRD:unmapped:Traceability:8":"sha256:2831ac927fc003681fcadb517dbdf6eea8891c2710e010ccbca60d6e35bc49dc"},"Status":"Current","Reason":null,"History":[],"SourceFactAuthority":{"Basis":{"docs/specs/technical-intent-spec.md":"sha256:84da4e56684bfd2e319af850eda0fce1995f4af8d13e704c9a18f5dd926b1bc3"},"AssessmentStatus":"Current","AssessmentReason":null}}

#### Recorded answer

Library inputs, local deployment files, application callbacks and transport peers retain their distinct trust boundaries. Type Manager's manifest is deployment metadata, not a hostile-file sandbox or authenticity signature. The deployment must remain stable while preparation runs. Hosts own access, credentials, network exposure, HTTP concurrency and application data policy.

Healthcheck detail access and local pipe authorization/endpoint protections retain the current transport contracts; this baseline does not infer missing privileged/cross-user/platform qualification. Logging remains diagnostic rather than a transactional audit guarantee. No new product identity provider, tenancy model, regulated-data guarantee, credential store or compliance certification is selected by registration.

Preserve current dependency/assembly/ownership/compatibility/platform policies in `docs/architecture/`, native test guidance, module contract catalogs and `planning/decisions.md`. Human-readable source requirements retain their original files and identities. No Active CIS standard is fabricated from a documentation metadata status, and optional standards/security capabilities remain explicit follow-up work.

### TI-Q-014 — Quality and assurance

Which quality attributes and verification layers are required?

- Why this is needed: Performance, reliability, accessibility, security, cost, and maintainability need measurable direction before detailed plans are produced.
- Common options: Unit; Component; Integration; Business acceptance; Architecture; Accessibility; Security; Performance; Mutation; Operational recovery
- Advisory starting direction: Select layered tests by risk, require deterministic architecture and security checks, and retain exact run evidence; define measurable budgets only where evidence supports them.
- Status: Derived
- Answered by: CIS deterministic repository analysis
- Answered at: Not recorded
- Resolution source: repository-evidence
- Confidence: high
- Evidence: cislib:implementations/csharp/examples/type-manager/Console; cislib:implementations/csharp/examples/type-manager/Console/Console.csproj; cislib:implementations/csharp/Directory.Build.props; cislib:Literal .NET test declarations; execution and result production are unverified; cislib:implementations/csharp/examples/type-manager/Web; cislib:implementations/csharp/examples/type-manager/Web/Web.csproj; cislib:implementations/csharp/tests; cislib:implementations/csharp/tests/TestAssembly.cs; cislib:implementations/csharp/tests/CIS.Core.ArchitectureTests; cislib:implementations/csharp/tests/CIS.Core.ArchitectureTests/CIS.Core.ArchitectureTests.csproj; cislib:implementations/csharp/tests/Directory.Build.targets; cislib:implementations/csharp/tests/CIS.Core.BusinessCases
- Assessment record: {"SupportingBasis":{"BRD:Business capabilities and processes:1":"sha256:3826e297b75b82eb4b4aa23122c2e9340c51b0a72556f3efb8d11a9911a99ec2","BRD:Business outcomes:3":"sha256:d390d64f26ae8f6db367768c5c5e5bf6f8ccee29e23efa18baa1b272a694b93f","BRD:CISLIB product business requirements:0":"sha256:37092a82d9c318fc4ea68f0b7f6a86065da017bf891a235377b776f8f47fe97b","BRD:Executive summary:1":"sha256:bb1e1ea426a5d6bfa91c15652ea6071c2fe73a819d19a5c9c9dd039ed486379a","BRD:Functional requirements:8":"sha256:7bb0d6920904acdab94cfd03e5b1d8190600fe4c6d6fe7710129de42c101d381","BRD:Quality, regulatory, and operational requirements:0":"sha256:f92d77877115d6e2d2a28e815b1107afca880289ab9272ea0d85512bff21f936","BRD:Quality, regulatory, and operational requirements:1":"sha256:505036c75ac4ace3b394bd38b6bf1e57a6526aaa4d207ede2305d5691250906d","BRD:Quality, regulatory, and operational requirements:2":"sha256:f4a6c3403393847b62ed4bd45f63f4971ebdf1da29b940244311a0e2e0a63822","BRD:Quality, regulatory, and operational requirements:3":"sha256:a4ae05c407c91ca6361b12c49af1a15f97949f8073b77ccc006eac9e3a59b9ac","BRD:Quality, regulatory, and operational requirements:4":"sha256:db5910319fd291f44825ae2aae9d06adc3aa47dc8ecffeaad1228015c7303565","BRD:Quality, regulatory, and operational requirements:5":"sha256:1a61168d2f87248ced8b1eacb9fa2f6372b150938bd4769205b95646bf0e5ab3","BRD:Scope:2":"sha256:1c7f59dcbd3c2655a02fcc7ca17c2fae52745bbe27c67ae62167358fd8c2f919","BRD:Stakeholders and actors:3":"sha256:315a1d0fec206c89f4f27e902aaa1629ca0a8900f960f1bc724cf8113d5639fa","BRD:Traceability:6":"sha256:701c5b96847279e829e58cfe2ae08efbd5e0aaecaf089df0df1004b44d800254","BRD:Traceability:7":"sha256:12edea97cb04774639411ac4cb60cb19c89c9277d3ea10121ad20cb5c4566072","BRD:unmapped:Business outcomes:0":"sha256:63f06b10d37e84d466481fa587bdb9285edd597c5efcf41b5cbc189008dceee3","BRD:unmapped:Canonical registration:0":"sha256:26027a377571f4b99f4595c7fc8a91cced04dacd4f04e6bfbc1b54ce48cf4ad9","BRD:unmapped:Functional requirements:1":"sha256:cbc46b87f50780d62831f4680c2942455d58d98de6ef10d840bfad06c3913a3d","BRD:unmapped:Functional requirements:3":"sha256:7f7984db9040b8819b7deb8ecd19c6fecdbf10931f9b47035ab908dcb4692892","BRD:unmapped:Functional requirements:5":"sha256:89428a5f0e3c2b87c8bef5b7c0ce5f21cc120d3ab446c18fc3e7eda89c64504d","BRD:unmapped:Open questions:0":"sha256:950ebf522c1271f11155a3c349a1188af4c26fbdff01b5d607cb121e1e9c1dec","BRD:unmapped:Stakeholders and actors:0":"sha256:4660f0a5b083676cceac105f14ca48f61178e358042b7c9a61ef24cbf2f07561","BRD:unmapped:Stakeholders and actors:4":"sha256:72bdf5d075aff8ac31d1694ec34df8fe41568095626a576e1c6c1ad6ce737cad","BRD:unmapped:Success measures:1":"sha256:d829372214a932a83be4a13b7b23beab9c46997216a895f190cb1525eef54dcd","BRD:unmapped:Traceability:1":"sha256:418faf927fe56611e48197e2de8f82df3731c0c7ad88c1b5552278f21180985c","BRD:unmapped:Traceability:4":"sha256:f656d3ff988a225e19a90670466df23d06fb528991e268dbd10bb82405e6cc16","BRD:unmapped:Traceability:8":"sha256:2831ac927fc003681fcadb517dbdf6eea8891c2710e010ccbca60d6e35bc49dc"},"Status":"Current","Reason":null,"History":[]}

#### Recorded answer

Preserve the existing verification stack: MSTest. Extend it according to changed-boundary risk and retain exact execution evidence.

### TI-Q-015 — AI and model lifecycle

Does the product use AI or learned models, and if so how are they hosted and governed?

- Why this is needed: Model execution introduces provider, data, evaluation, explainability, drift, cost, and human-oversight boundaries.
- Common options: Not applicable; Local model; Managed model API; Self-hosted model; Retrieval-augmented generation; Training/fine-tuning; Human approval required
- Advisory starting direction: No transactional application system of record is introduced. Logging files and Type Manager manifests have their existing diagnostic/deployment roles; they are not inferred databases. Unrequested caches/search/object stores and AI/model execution do not become selected architecture.
- Status: Unanswered
- Answered by: Not recorded
- Answered at: Not recorded
- Resolution source: technical-intent-document
- Confidence: Not assessed
- Evidence: docs/specs/technical-intent-spec.md:87 — Application model and invariants (sha256:3f8b6e04a743a1b4bc067f8730dc1ea9b3b538da92ac22b0745b4f942f9c156c)
- Assessment record: {"SupportingBasis":{"BRD:Constraints and assumptions:0":"sha256:1b6487cd2655d7c2bfcdd9dfd49cfff6170e66945b808ca530cb380ad3cabb03","BRD:Constraints and assumptions:1":"sha256:d36f3f8506b6a2d61d89fbe5aadc9a1f391a0e098a0521080a69365135f92576","BRD:Constraints and assumptions:2":"sha256:67728cf5261d1f576b66ed3e81ac8f0c26fb5ffbe7da0ed2d75e6e8d2625adc5","BRD:Constraints and assumptions:3":"sha256:071aa05f8fea7bcc341b2cf14cb5eca4b9db0eab9d813599577c818c0705a1e1","BRD:unmapped:Business outcomes:0":"sha256:63f06b10d37e84d466481fa587bdb9285edd597c5efcf41b5cbc189008dceee3","BRD:unmapped:Canonical registration:0":"sha256:26027a377571f4b99f4595c7fc8a91cced04dacd4f04e6bfbc1b54ce48cf4ad9","BRD:unmapped:Functional requirements:1":"sha256:cbc46b87f50780d62831f4680c2942455d58d98de6ef10d840bfad06c3913a3d","BRD:unmapped:Functional requirements:3":"sha256:7f7984db9040b8819b7deb8ecd19c6fecdbf10931f9b47035ab908dcb4692892","BRD:unmapped:Functional requirements:5":"sha256:89428a5f0e3c2b87c8bef5b7c0ce5f21cc120d3ab446c18fc3e7eda89c64504d","BRD:unmapped:Open questions:0":"sha256:950ebf522c1271f11155a3c349a1188af4c26fbdff01b5d607cb121e1e9c1dec","BRD:unmapped:Stakeholders and actors:0":"sha256:4660f0a5b083676cceac105f14ca48f61178e358042b7c9a61ef24cbf2f07561","BRD:unmapped:Stakeholders and actors:4":"sha256:72bdf5d075aff8ac31d1694ec34df8fe41568095626a576e1c6c1ad6ce737cad","BRD:unmapped:Success measures:1":"sha256:d829372214a932a83be4a13b7b23beab9c46997216a895f190cb1525eef54dcd","BRD:unmapped:Traceability:1":"sha256:418faf927fe56611e48197e2de8f82df3731c0c7ad88c1b5552278f21180985c","BRD:unmapped:Traceability:4":"sha256:f656d3ff988a225e19a90670466df23d06fb528991e268dbd10bb82405e6cc16","BRD:unmapped:Traceability:8":"sha256:2831ac927fc003681fcadb517dbdf6eea8891c2710e010ccbca60d6e35bc49dc"},"Status":"Current","Reason":null,"History":[]}

#### Recorded answer

Not answered.

### TI-Q-016 — Constraints and exclusions

Which technical constraints, mandated choices, explicit exclusions, and future options must be preserved?

- Why this is needed: Explicit boundaries prevent a high-level intent from silently expanding scope or hard-coding a future option into the MVP.
- Common options: Mandated technology; Existing platform constraint; Offline requirement; Cost ceiling; Licensing; Provider neutrality; Deferred option; Explicitly excluded technology
- Advisory starting direction: Record only evidenced constraints and distinguish current decisions from post-MVP options and explicit exclusions.
- Status: Derived
- Answered by: Not recorded
- Answered at: Not recorded
- Resolution source: accepted-document
- Confidence: source-explicit
- Evidence: docs/specs/technical-intent-spec.md:38 — Design goals and principles (sha256:3f8b6e04a743a1b4bc067f8730dc1ea9b3b538da92ac22b0745b4f942f9c156c); docs/specs/technical-intent-spec.md:75 — Runtime architecture and trust boundaries (sha256:3f8b6e04a743a1b4bc067f8730dc1ea9b3b538da92ac22b0745b4f942f9c156c); docs/specs/technical-intent-spec.md:119 — Quality attributes and verification direction (sha256:3f8b6e04a743a1b4bc067f8730dc1ea9b3b538da92ac22b0745b4f942f9c156c); docs/specs/business-requirements.md:77 — Quality, regulatory, and operational requirements (sha256:7e0e542c011a0fa65c23ce63e305f4a62cd4c4c607d73c05b3a0bc46f2fcf042); docs/specs/business-requirements.md:81 — Quality, regulatory, and operational requirements (sha256:7e0e542c011a0fa65c23ce63e305f4a62cd4c4c607d73c05b3a0bc46f2fcf042); docs/specs/business-requirements.md:105 — Traceability (sha256:7e0e542c011a0fa65c23ce63e305f4a62cd4c4c607d73c05b3a0bc46f2fcf042)
- Assessment record: {"SupportingBasis":{"BRD:Constraints and assumptions:0":"sha256:1b6487cd2655d7c2bfcdd9dfd49cfff6170e66945b808ca530cb380ad3cabb03","BRD:Constraints and assumptions:1":"sha256:d36f3f8506b6a2d61d89fbe5aadc9a1f391a0e098a0521080a69365135f92576","BRD:Constraints and assumptions:2":"sha256:67728cf5261d1f576b66ed3e81ac8f0c26fb5ffbe7da0ed2d75e6e8d2625adc5","BRD:Constraints and assumptions:3":"sha256:071aa05f8fea7bcc341b2cf14cb5eca4b9db0eab9d813599577c818c0705a1e1","BRD:Quality, regulatory, and operational requirements:1":"sha256:505036c75ac4ace3b394bd38b6bf1e57a6526aaa4d207ede2305d5691250906d","BRD:Quality, regulatory, and operational requirements:5":"sha256:1a61168d2f87248ced8b1eacb9fa2f6372b150938bd4769205b95646bf0e5ab3","BRD:Traceability:2":"sha256:d3363aa9959b5fbd8571f4728d58a887f7fa990b3dc0535da468426df1f81222","BRD:unmapped:Business outcomes:0":"sha256:63f06b10d37e84d466481fa587bdb9285edd597c5efcf41b5cbc189008dceee3","BRD:unmapped:Canonical registration:0":"sha256:26027a377571f4b99f4595c7fc8a91cced04dacd4f04e6bfbc1b54ce48cf4ad9","BRD:unmapped:Functional requirements:1":"sha256:cbc46b87f50780d62831f4680c2942455d58d98de6ef10d840bfad06c3913a3d","BRD:unmapped:Functional requirements:3":"sha256:7f7984db9040b8819b7deb8ecd19c6fecdbf10931f9b47035ab908dcb4692892","BRD:unmapped:Functional requirements:5":"sha256:89428a5f0e3c2b87c8bef5b7c0ce5f21cc120d3ab446c18fc3e7eda89c64504d","BRD:unmapped:Open questions:0":"sha256:950ebf522c1271f11155a3c349a1188af4c26fbdff01b5d607cb121e1e9c1dec","BRD:unmapped:Stakeholders and actors:0":"sha256:4660f0a5b083676cceac105f14ca48f61178e358042b7c9a61ef24cbf2f07561","BRD:unmapped:Stakeholders and actors:4":"sha256:72bdf5d075aff8ac31d1694ec34df8fe41568095626a576e1c6c1ad6ce737cad","BRD:unmapped:Success measures:1":"sha256:d829372214a932a83be4a13b7b23beab9c46997216a895f190cb1525eef54dcd","BRD:unmapped:Traceability:1":"sha256:418faf927fe56611e48197e2de8f82df3731c0c7ad88c1b5552278f21180985c","BRD:unmapped:Traceability:4":"sha256:f656d3ff988a225e19a90670466df23d06fb528991e268dbd10bb82405e6cc16","BRD:unmapped:Traceability:8":"sha256:2831ac927fc003681fcadb517dbdf6eea8891c2710e010ccbca60d6e35bc49dc"},"Status":"Current","Reason":null,"History":[],"SourceFactAuthority":{"Basis":{"docs/specs/technical-intent-spec.md":"sha256:e46c80b35744f7d1d203ab0727bcbc18e34e01a454ae2480eda066237a1e4c8b","docs/specs/business-requirements.md":"sha256:d3ac1663338ef47b4a8b554781b9ac9f7f82612635742ac3e739338fe3f6a269"},"AssessmentStatus":"Current","AssessmentReason":null}}

#### Recorded answer

Keep direct, readable code with explicit module and host responsibilities. Keep actual product behaviour and meaningful assertions while using ordinary native development commands. Preserve bounded work, detached observations and truthful failure/settlement facts. A snapshot/report must not mutate health, a wait timeout must not claim underlying work stopped, and a queue admission must not claim durable output delivery.

Core's approved runtime closure is `Microsoft.Extensions.Logging.Abstractions` and `Microsoft.Extensions.DependencyInjection.Abstractions`, both `10.0.12`, plus recorded shared-framework dependencies and user-approved `Microsoft.AspNetCore.App`. Do not remove the established NuGets or Core package-pruning exception as an unrelated migration. Third-party runtime integrations require separate optional packages; development tooling and the MSBuild helper must not enter Core's runtime assembly closure.

Retain five actual MSTest/VSTest families: Unit, Integration, Architecture, Compatibility and Business Cases, with genuine shared support and fixtures. Ordinary regression excludes `TestCategory=PerformanceObservation` and `TestCategory=ExtendedStress`; focused selection preserves both exclusions. Purpose-specific release verification separately covers accepted performance/stress, compatibility, platforms and package consumers.

- Preserve the approved Microsoft runtime dependency closure and `Microsoft.AspNetCore.App`; third-party runtime integrations require separate optional packages. Development-only tooling and the Type Manager build helper must not enter the runtime dependency closure. [S01, S03]

- Retain MIT licensing and maintainer-only official publication controls. The source material establishes no sector-specific regulatory certification or new data-residency guarantee. Host deployment and data policy remain the application's responsibility. [S01, S03, S06]

| S03 | `docs/architecture/csharp-dependency-policy.md`, `csharp-assembly-policy.md`, `compatibility-policy.md`, `CONTRIBUTING.md`, `LICENSE.md`: current package, dependency, compatibility and maintainer rules. |
