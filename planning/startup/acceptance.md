---
title: Startup acceptance and native evidence mapping
type: planning-reference
status: Draft
---

# Startup acceptance and native evidence mapping

IDs retain their meanings in the [byte-preserved supplied source](sources/CISLIB_Startup_and_DI_Specification_v1_0.md).
This maps behavior to existing native test families, not a new test catalogue.
Actual commands, counts, failures, platform limits and costs are in
[CIS-0007 verification](../../docs/changes/CIS-0007/verification.md).

The counts and table below retain the initial implementation checkpoint before
the subsequent coordinator/hosting boundary refinement. They are historical
observations, not a claim that a changed candidate has passed. Current refinement
results remain those actually recorded in verification.

`Core` below means `CIS.Tests.UnitTests.Startup`; `Host` means
`CIS.Tests.IntegrationTests.Startup`; `Story` means
`CIS.Tests.BusinessCases.Startup`. Core's 57-case checkpoint and Host's 25-case
checkpoint passed. The actual source consumer and ten connected Type Manager
stories also passed. The final candidate passed 176 affected unit cases, 334
architecture cases and 179 compatibility cases; all have zero failures/skips.
The 57-case component checkpoint overlaps the final unit selection and is not
added again to an aggregate. Exact receipts and scopes are in verification.

| Acceptance | Concrete test symbols / source evidence | Initial checkpoint |
| --- | --- | --- |
| AC-01 | Actual `examples/startup/CountryList/Program.cs` awaits TypeManager before Build and registers that instance. `Story.StartupCountryListStories.RealConsumerPreparesThenPrimesThenReadsBeforeReadinessAndRecurringRefresh`; existing `CIS.Tests.BusinessCases.TypeManagerStartupStories.PreparationHandsUsableCatalogToApplicationBeforeItCreatesServices` and `FailedPreparationNeverInvokesApplicationFactory`. | Passed actual source story and connected positive/negative preparation stories |
| AC-02 | `Core.StartupCoordinatorEnrollmentTests.OnlyOrdinaryInterfaceRegistrationsEnrollAndAnEmptyScheduledSequenceSucceeds` includes concrete/other-interface/keyed-only/empty cases; `Host.StartupHostingTests.StartupAloneUsesTheActualRootWithoutRequiringOptionalHealthOrHeartbeatModules`. | Passed |
| AC-03 | `Core.StartupCoordinatorEnrollmentTests.EveryScheduledFactoryFinishesBeforeTheFirstStartAndUnrelatedServicesRemainLazy` and `ALateFactoryFailureCallsNoStartupMethodAndTheSameFailureDoesNotResolveAgain`. | Passed |
| AC-04 | `Core.StartupCoordinatorEnrollmentTests.ConstructorDependencyOrderDoesNotReplaceTheInterfaceRegistrationStartupOrder`; `Core.StartupCoordinatorExecutionTests.AHeldActualStartupTaskPreventsTheNextInvocationAndReadinessUntilItCompletes`. | Passed |
| AC-05 | `Core.StartupCoordinatorEnrollmentTests.ReferenceIdentityDeduplicatesAliasesAndPreservesDistinctEqualObjects`. | Passed |
| AC-06 | `Core.StartupCoordinatorEnrollmentTests.NormalResolutionAndStartupUseTheSameRootSingletonAndOnlyItsProviderDisposesIt`, `NonSingletonUnkeyedEnrollmentIsRejectedBeforeAnyStartupMethod`, `StandardRootScopeValidationRejectsASingletonScopedCaptureBeforeStageTwo`, `ExternallySuppliedStartupInstancesRemainExternallyOwnedAfterStopAndProviderDisposal`. | Passed standard validation path; observed mixed-instance framework limitation retained below |
| AC-07 | `Core.StartupCoordinatorExecutionTests.ConcurrentAndRepeatedEntryRetainTheFirstOperationAndIgnoreLaterCancellationTokens`, `TheRetainedOperationIsPublishedBeforeAnyUserCodeCanReenterStartup`; failure and pre-cancel cases also assert retained task identity/no new resolution. | Passed |
| AC-08 | `Core.StartupCoordinatorExecutionTests.EscapingStartupFailuresStopTheSequenceAndRemainFailedWithoutReplay`; `Host.StartupReadinessTests.StartupFailureAndKnownHostCancellationPreserveClosedAdmissionAndTheirActualHealthMeaning`. | Passed |
| AC-09 | `Core.StartupCoordinatorCancellationTests.APreCanceledFirstHostTokenRetainsCancellationWithoutResolvingParticipants`, `CancellationBetweenParticipantsOrAtTheFinalBoundaryNeverPublishesCompletion`, `StoppingFromTheFinalParticipantCannotPublishStartupCompletion`, `StopJoinsActualParticipantSettlementBeforeItsRootDependencyCanBeDisposed`; `Core.StartupHeartbeatCompletionTests.HostCancellationDuringActualTimerCreationCannotPublishOrDispatch`. | Passed |
| AC-10 | `Core.StartupCoordinatorExecutionTests.ExistingHealthPolicyAffectsReadinessWithoutBlockingOrReplayingSuccessfulStartup`; `Host.StartupReadinessTests.ExistingCriticalityAndRecoveryChangeReadinessWithoutReplayingSuccessfulStartup`. | Passed |
| AC-11 | `Host.StartupReadinessTests.BoundHttpAndRealPipeRemainNotReadyUntilSequentialStartupThenHeartbeatActivatesAutomatically`. | Passed with real bound Kestrel listener / business counter |
| AC-12 | Same Host test invokes existing real `CisHealthProbe` against the configured pipe and checks 1/0 plus complete bounded capture. | Passed |
| AC-13 | Same Host test holds the first callback after startup success; `Host.StartupReadinessTests.AStoppedHeartbeatCannotBeMistakenForSuccessfulActivationAfterAllParticipantsSucceed`; `Core.StartupHeartbeatCompletionTests.ActivationFailureOrStoppedNoOpCannotPublishStartupSuccess`. | Passed |
| AC-14 | `Host.StartupReadinessTests.StopApplicationFromTheFinalParticipantCannotReopenReadinessOrHeartbeatDispatch`; `Host.StartupHostingTests.HostStoppingJoinsAnIgnoringStartupTaskBeforeDependentLifecycleStoppingAndRootDisposal`, `HostStoppingRetainsDependenciesThroughHeartbeatCallbackAndItsAwaitedRecovery`. | Passed |
| AC-15 | Actual source example and `Story.StartupCountryListStories.RealConsumerPreparesThenPrimesThenReadsBeforeReadinessAndRecurringRefresh`. | Passed actual source story |

The AC-06 scope prohibition remains in force. Validation uses the real Microsoft
provider, with both standard options enabled on the actual application builder.
An observed pinned .NET 10 mixed-instance cache-key limitation is explicitly
documented in [the core guide](../../docs/modules/startup/core.md) and retained
as a failed observation in [verification](../../docs/changes/CIS-0007/verification.md#standard-di-validation-boundary).
No universal hidden-capture detection, registration rewrite, extra provider or
custom dependency graph validator is claimed.

Additional host composition tests reject concurrent lifecycle execution and
unknown hosted-service tails without a second resolution, and ignore keyed-only
hosted registrations. Seven existing Heartbeat hosting cases passed alongside
the new integration cases; the standalone behavior is preserved.

## Coordinator and hosting boundary refinement

The subsequent user request changes the internal responsibility boundary while
preserving AC-01–AC-15. `StartupCoordinator` has no Heartbeat type dependency or
concrete activation, cancellation-cutoff or drain logic. Hosting owns that work;
the coordinator retains the same sequential operation, monitor and readiness
publication through one internal Boolean completion callback.

Affected verification must preserve these existing acceptance paths:

- AC-07/AC-08/AC-13: completion confirmation stays inside the retained operation.
  Activation failure flags the existing startup condition and replays its terminal
  failure without re-resolution, participant replay or a second host operation.
- AC-09/AC-13/AC-14: hosting connects the original operation token to Heartbeat's
  cutoff through actual startup settlement, and connects the first host-start
  token before construction so cancellation immediately closes owner/registration
  admission even with an ignoring initializer held and ApplicationStopping unset.
  Cancellation during timer creation
  cannot dispatch or publish readiness; the coordinator rechecks stopping after
  confirmation before treating a negative acknowledgement as failure.
- AC-10/AC-13: hosting confirms actual activation before Healthcheck startup
  publication, without waiting for the first callback or aggregate healthy state.
- AC-14: hosting closes dispatch before entering coordinator stopping and joins
  both actual startup and Heartbeat work before dependent stopping/disposal,
  including an already-canceled host stop budget and failed-start cleanup.
- AC-02/AC-07: Startup remains usable without Heartbeat, and later callers cannot
  replace the first operation's tokens. Standalone Heartbeat keeps its existing
  lifecycle behavior and composition checks.

Heartbeat-specific completion tests exercise the hosted integration boundary;
coordinator-only tests continue to exercise its direct DI/Healthcheck contract.
The existing test families and ordinary native workflow provide this evidence.
No new pass, broader platform claim or verification harness is recorded here.
