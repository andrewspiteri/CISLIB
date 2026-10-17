# Runtime documentation correction

17 October 2026, based on 3d1084af. Addresses continuation 22-F2, F3 and F5.

The unpublished contract and XML now describe callback-flow false/recursive decisions, synchronous flush/stop/disposal restrictions across runtimes and inherited child tasks, callback guard precedence over invalid timeout, and startup registration/interceptor argument failures. The existing recursion test now also checks disposal and zero-timeout precedence. Public incoming outcomes are distinguished from internal output-stage qualification in enum XML.

The nonshipping startup reference catalog now includes ConsoleInitializationFailed, FileFlushTimerInitializationFailed, WorkerInitializationFailed and ShutdownTimerCleanupFailed. No shipping LogErrorCodes type or public outcome was added. Initialization OutputName is a safe component label and may identify the incoming worker.

Both native lanes pass 22 ProcessingPolicyTests and 79 RuntimeContractTests, zero failures/skips. Run IDs are retained in the four focused summaries. This documentation batch does not repeat full Developer, coverage, mutation, compatibility or release qualification. The roadmap remains paused.
