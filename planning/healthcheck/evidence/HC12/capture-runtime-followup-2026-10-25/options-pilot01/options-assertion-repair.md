# Options pilot assertion repairs

Root authorized only the two canonical Foundation test files below. No shipping code, classifier, profile, timeout, source variant, case row, current194 proof or native outcome was changed. No build/test/SSH/classifier/audit entry has been performed for this repair. Current report IDs below are local to this one pilot, not mappings to a later campaign or applied kill credit.

## Actual source and failures

The complete regular-file readback is `../unit194-options-pilot-closed-receiving01/readback01`:9,616 files/1,143,709,088 bytes, original manifest `407c008dc6b851d28f9567cf79fcde23001ad9c8021bf9b41de68d7405320e88`. Its11 nonregular TMP sockets and first failed preparation remain separate retained facts. Compact report `587b737dac6d0215bc3cb91d5655213e88339b63b66d1dcda263e2de833f9ee7` has120 definitions:87Killed/4Survived/5Timeout/6CompileError/18Ignored. Audit `f5a54c0b11c2728e6fb17a29067fb507b1a887a0afde3accfb5e316c9a2b83bb` records98 execution hosts/71 normal,57 direct IDs/430 witnesses and27 aborted hosts. Its30 missing-witness IDs are not all finite assertion gaps.

All eight requested rawKilled IDs lack an accepted direct witness but have no abort issue. Their exact native host-sent rows were read from the full immutable logs: all eight have IsAborted=false/IsCanceled=false/Error=null and unique returned result GUIDs. This is a bounded recorded-data read, not a new auditor or complete lifecycle qualification. The original audit/capture and every other hold remain mandatory.

All tuples belong to `Healthcheck/Contracts/Options/HealthcheckOptions.cs` physical SHA256 `75d3e327b107682e5321b65991825fd51b7c5a6c12f7c539aded2dd758d8d419`; UTF16 spans/original text below are the same pilot's retained actual-scope rows, also inspected against its frozen source. Static=false for all8.

| Pilot ID; span | Exact source replacement | Host; returned/failed | Actual existing test/error |
| --- | --- | --- | --- |
|484;4433..4479|ReadBoolean's `{ return parsed; }` block → `{}`|2974156;1/1|EnvironmentOverridesSetupAndReadsEachKnownKeyExactlyOnce; escaped CoreConfigurationException from Resolve.|
|512;5893..5936|`issue.Key == "CIS_HEALTHCHECK_PIPE_ENABLED"` → `!=`|2974662;2/1|IndependentInvalidSettingsAreReportedWithoutRawValues; actual CollectionAssert.AreEquivalent,3expected keys/2actual.|
|517;6032..6059|`"CIS_HEALTHCHECK_PIPE_NAME"` → `""`|2974742;2/1|Same test; actual CollectionAssert reports missing required name key.|
|528;6567..6583|`issue.Key == key` → `!=`|2974874;20/1|Same test; actual CollectionAssert reports3expected keys/2actual.|
|534;6654..6669|`value < minimum` → `value <= minimum`|2974982;850/79|All79 failures are CoreConfigurationException; the existing InclusiveLimitsAndPortableMaximumPipeNameAreAccepted is one concrete valid-lower-bound oracle.|
|548;7008..7011|pipe-name pattern `< 1` → `<= 1`|2975227;65/1|PortablePipeNamesAcceptTheirInclusiveMinimumAndEachAllowedSeparator("A"); CoreConfigurationException.|
|550;7015..7019|pipe-name pattern `> 64` → `>= 64`|2975279;65/1|InclusiveLimitsAndPortableMaximumPipeNameAreAccepted; CoreConfigurationException at its high-value copy.|
|561;7311..7321|`'.' or '_'` → `'.' and '_'`|2975464;53/2|PortablePipeNamesAcceptTheirInclusiveMinimumAndEachAllowedSeparator("a.b"/"a_b"); CoreConfigurationException.|

The current public option contract is explicit in `docs/modules/healthcheck/usage.md:58–71` and the option XML summaries: valid selected true/false values, inclusive numeric bounds and1–64 portable name characters/separators are accepted. The actual injected-reader Resolve overload and ValidatedCopy under test are internal boundaries behind that public contract; they are not new public APIs. The tested dictionary reader performs no asynchronous/resource work. The five known valid-input rejection cases can therefore be asserted at their real synchronous call boundary without catching arbitrary exceptions or promoting timeouts/EOF.

512/517/528 already failed a genuine collection assertion. Their actual synchronous MSTest stack terminates with MethodBaseInvoker, while the maintained DirectAssertion collection branch additionally requires the adapter ExecuteInternalAsync frame. This is a receiving-shape limitation, not a missing semantic multiset oracle. This repair does not widen that classifier or relabel the historical errors.

## Exact canonical scope

Preimages match the pilot source: OptionsTests `c46eb14f7ecbafa044fddf75665426658768775938d66d758096ef70df147fd6`; ContractTests `6deee4229e7401525864fa9ef932010face82e0fe63f5db8f70ad9a792a099ee`. Both exact bodies are retained under `preimages`.

- `implementations/csharp/tests/CIS.Core.UnitTests/Healthcheck/Foundation/HealthcheckOptionsTests.cs` current `c8aa288196d5952115d88cc64f895bb8d5c77bf581d7480907c6f74145b82d3d`: EnvironmentOverrides uses a local AcceptedResolution helper; the existing low/high InclusiveLimits calls use local AcceptedCopy. Each helper catches only the actual CoreConfigurationException, passes that original object to Assert.IsNull, then requires/returns the actual SUT result. No fallback object is supplied. Existing reads, setup identity, values, inclusive observations and invalid65-character rejection remain.
- `implementations/csharp/tests/CIS.Core.UnitTests/Healthcheck/Foundation/HealthcheckContractMutationQualificationTests.cs` current `260fc80386e634ec4ef119ee418afbf6b6a5481cc87e60eb1ccbaef20a95c8d1`: only the existing portable-name dataset call uses the same local exact-exception acceptance pattern; all five names and both value/enabled assertions remain.
- The existing IndependentInvalidSettings multiset assertion becomes cardinality3 plus multiplicity1 for each of its three expected keys. This is mathematically identical for extras, omissions and duplicates; ordering remains irrelevant. It is not weaker inclusion or a duplicate test. No diagnostic key or exception allowlist changes.

These are four source call sites covering five pilot definitions because the portable-name dataset supplies both minimum/separator cases. All existing method/DataRow identities remain. The scope deliberately does not convert the other78 errors under534, arbitrary constructor failures, unknown exceptions, canceled/timed-out tasks or cleanup errors into assertions. Original exceptions remain observed objects in the failure assertion; every exception outside the known configuration type escapes as before.

Exact canonical delta is `canonical-assertions01.diff`. Capture's independent source-only peer is external `../unit195-options-assertion-repair-capture-source-peer01.md`, SHA256 `48d34ebdc59d6c58061411d8a8c05ccef59aca6886221e62bdde286d70a6a1a0`. Root review and official focused verification are still separate prerequisites. No prospective or historical numerator is claimed.
