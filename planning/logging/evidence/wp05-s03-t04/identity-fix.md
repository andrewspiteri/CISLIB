# Native unit result identity correction

The first full Developer process executed all 2740 unit cases successfully, but the strict native TRX reader correctly rejected the report: both Type-valued DataRow cases for PublicDiagnosticContainersHaveOnlySealedGetterOnlyFacts shared testId 1eaf511e-d651-84f3-b5ff-7d17e9c721f3, despite different executionIds and display names. That run exited1 and does not qualify Developer completion. Its summary, source manifest, raw TRX and top-level log are retained separately.

The two declared DataRows now use distinct runtime/output string identities and resolve their real Type inside the method. Exact property, sealed/getter-only, constructor and public API checks are unchanged. No report validator is weakened. Fresh focused/full verification must prove native result uniqueness; shipping sources and native fixture bytes are unchanged by this unit-only correction. Claude review records this qualification correction before commit.
