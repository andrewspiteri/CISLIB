# HC06 internal seam addendum — revision 0.4

The coordinator accepts two targeted additions to C05's internal state/binder seam. The preserved HC00 baseline and public signatures/defaults remain unchanged.

`IHealthExecutionState.IsActive(BindingTarget)` checks the issued target under the state gate without changing revisions. The binder uses this before duplicate/obsolete classification so a withdrawn target returns Retired even for late notifications. BeginStopping alone leaves existing bindings active for their final handoffs; only new admission closes.

`UpdateLifecycle` accepts an already-deduplicated Outcome fact to enrich its detached context without counting that outcome again. `ApplyOutcome` remains the only path that performs outcome policy arithmetic. The binder validates retained immutable fields before either call. No user code is called under the state gate.

IntegrationFault's optional SourcePhase identifies diagnostic origin; it does not promote the accumulated lifecycle phase. Existing actual phase remains, or Registered is used when no lifecycle notification exists. In particular, diagnostics cannot fabricate settlement or retirement. This corrects the state integration without changing the accepted fact matrix.

Affected owners: HC06 implementation and independent execution tests. HC03 continues against detached snapshot values and has no dependency on these internal methods. No project/package changes are required.
