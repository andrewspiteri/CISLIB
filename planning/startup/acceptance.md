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

`Core` below means `CIS.Tests.UnitTests.Startup`; `Host` means
`CIS.Tests.IntegrationTests.Startup`; `Story` means
`CIS.Tests.BusinessCases.Startup`. Core's 57-case checkpoint and Host's 25-case
checkpoint passed. The actual source consumer and ten connected Type Manager
stories also passed. The final candidate passed 176 affected unit cases, 334
architecture cases and 179 compatibility cases; all have zero failures/skips.
The 57-case component checkpoint overlaps the final unit selection and is not
added again to an aggregate. Exact receipts and scopes are in verification.

| Acceptance | Concrete test symbols / source evidence | Checkpoint |
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
