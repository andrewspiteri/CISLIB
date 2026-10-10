# C# publication history format

WP02-S04-T02 defines the storage format and development validator. The maintained [ledger](ledger.json) registers `cis.core.csharp` (`CIS.Core`, assembly `CIS.Core`, root namespace `CIS`) and has **zero publication rows**. Logging and MicrosoftLogger share that identity inside Core. A future optional third-party integration receives its own identity. Other languages keep their own publication mechanics.

A local `0.1.0-dev.1` package, contract reference assembly, synthetic test record or verified candidate is not a publication. Register a row only after the exact package/version is actually exposed to consumers. Every such exposure belongs to history, including prereleases, intermediate releases and a package exposed during partial aggregate promotion. Never reduce membership to latest/stable versions, successful aggregate releases or versions that current tooling happens to support.

The [permanent preservation policy](../../../docs/architecture/compatibility-policy.md) applies. Changing a major version, adding an obsolete warning, dropping a target, editing a baseline or unlisting a package cannot remove a historical obligation. Current schemas are prepublication implementation choices, not previously shipped contracts.

## Ledger and immutable records

[ledger.schema.json](ledger.schema.json) defines a UTF-8 JSON registry with immutable header, identity membership and ordered publication rows. IDs use exact case; package IDs and package/version uniqueness use case-insensitive comparison. `compatibilityLine` is fixed to `permanent`. Identity and publication arrays may only grow by appending; previous entries cannot be edited, removed or reordered. Publication sequence numbers are contiguous from one in exposure order, rather than semantic version order.

Each row contains its identity, exact canonical package version and an artifact reference to one immutable [baseline record](baseline.schema.json). Selected version syntax is three nonnegative components without leading zeroes, optionally followed by dotted prerelease labels. Numeric prerelease labels cannot have leading zeroes. Build metadata, four-part versions and version ranges are excluded before publication so one published package has one key. Prerelease label case cannot create a second package/version identity.

A record repeats the ledger's sequence/identity/version and the registered package identity. It contains:

| Record area | Retained information |
| --- | --- |
| Exposure | UTC instant with seven fractional digits, aggregate release identity, promotion state at exposure, publication location and immutable receipt. |
| Package | Exact verified candidate and exact distributed package bytes, separately identified to accommodate distribution signing. |
| Source | Commit when available, complete source archive and source hash inventory; an inventory identifies dirty builds independently of a commit. |
| Targets | Every published TFM/RID/asset path, minimum runtime family, assembly bytes, full assembly/file/informational identity, culture/key token and complete public API metadata. |
| Contracts | Versioned API/behavior specification and configuration, wire and observable-behavior fixtures for each exposed module contract. |
| Source consumers | Immutable source archive, original target/language/nullable/warnings-as-errors settings, build settings, resolved dependency inventory and expected observations. |
| Binary consumers | Original unchanged assembly, runtime configuration, dependency manifest/inventory/archives, expected observations, source origin and original execution identity. |
| Executions | Lane/OS/architecture/TFM, exact SDK/runtime, roll-forward and execution mode, consumer membership, toolchain/prerequisite inventories, materialization archive and native reports. |
| Verification | Exact retained verification bundle and summary. |

Targets are unique by TFM/RID/asset path. Source, binary and execution IDs are unique within their respective arrays. A source consumer's target must have a retained asset; a binary consumer must name a source origin. Each execution names retained binary consumers and a retained target. A binary's original execution must include that consumer. Contract ID/version pairs are unique. At least one target, contract, source consumer, binary consumer and execution is required; coverage of every real contract and platform is a later release qualification obligation, not inferred from these minimum counts.

An artifact reference has a portable relative `path`, lowercase SHA-256, exact `bytes` and nonempty `mediaType`. Paths use ASCII letters/digits/underscore/hyphen/dot and `/` separators; absolute paths, empty segments, `.`/`..`, backslashes and drive prefixes are forbidden. Referenced files must stay under the selected artifact root and cannot traverse symbolic links/junctions. Media type describes the payload; the format checker does not inspect or execute it.

Retain record/artifact bytes in durable publication storage, with an independently retained ledger snapshot/hash. `.artifacts/`, temporary caches and mutable feed URLs cannot be the sole retained source. Write and verify all artifacts and the immutable record before appending its ledger row. Preserve the record and every referenced artifact byte-for-byte afterwards. Corrected annotations require separate provenance; they must not replace the original record, exposure state or hashes. A future schema migration must retain and validate all older records rather than rewrite them into a new baseline.

## Validation and trusted history

From the repository root, using PowerShell 7.6.5 (the executed development version; parsing requires 7.5 or later):

```powershell
pwsh -NoProfile -File eng/check-publications.ps1
pwsh -NoProfile -File eng/check-publications.ps1 `
  -LedgerPath compatibility/csharp/publications/ledger.json `
  -ArtifactRoot . `
  -PreviousLedgerPath <independently-retained-previous-ledger> `
  -PreviousLedgerSha256 <trusted-lowercase-sha256>
```

The first command validates schema, links and artifact hashes, reporting `NotCompared` for append-only history. The second also verifies the supplied previous bytes against a trusted hash and compares immutable header/array prefixes. The previous path and hash must be supplied together. Deriving a new trusted hash from potentially rewritten history during the same check defeats the anchor; the validator cannot establish that trust itself. Ledger whitespace/member order may change because prefix comparison uses JSON values; referenced baseline/artifact hashes bind exact original bytes.

Exit 0 means format/integrity checks passed; exit 2 means rejection. The default report is `.artifacts/csharp/publications/validation.json`. Custom output must be new and outside retained publication storage; output never replaces either ledger. The validator reads and does not mutate retained inputs. Developer verification invokes it and records its own disposable report under `contracts/publications/validation.json`, with a `publication-ledger` stage log. It does not automatically obtain a trusted previous ledger or authorize publication.

Reports always declare `actualPublicationAttested:false`, `compatibilityChecksExecuted:0` and `compatibilityQualified:false`. A valid schema/hash does not prove a NuGet archive is legitimate, API metadata complete, consumer executable, feed receipt authentic or historical replay successful. With zero rows the report says `NoPublicationsRecorded`, while `firstReleaseHarnessRequired:true` keeps first-release qualification mandatory. Actual receipt verification, package/consumer materialization and the four compatibility layers remain separate roadmap work.

## Executed format controls

`PublicationManifestTests` exercises 43 native development controls: six valid format cases and 37 expected rejections. They cover empty history, first prerelease, intermediate membership, partial promotion, added identity and integrity without an anchor; removal/reordering/rewriting, wrong/missing anchors, duplicate/unknown membership, invalid JSON/versions/time, absent source/binary/API records, broken links and changed/missing artifacts.

Synthetic payloads are opaque test bytes, use `example.invalid` and never enter the maintained ledger. Tests also verify input hashes remain unchanged. Passing these architecture controls qualifies the format validator only. No historical release or runtime compatibility execution is claimed. T03 defines replay/equality rules; T04 prepares compiling source patterns and deliberately incompatible samples for the later package harness.
