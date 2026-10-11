# T03 scope ownership decision — pending

T02 is committed as 3b0e68409982cafeda373d8519bf10ac79226f4e. T03 is not complete or committed.

The original supplied guide section10 and WP04-S03-AC03/AC04 require creating-flow ownership, LIFO errors without mutation, harmless valid repeats, inherited context, and a prohibition on transferring handles across concurrent flows. During WP02-S03 we added a stronger unpublished universal cross-flow exception promise. Actual native probes contradict that promise. No published contract exists, and original source plans/historical evidence remain unchanged.

Recommended choice: preserve BeginScope returning IDisposable and the original caller ownership rule. Precisely document that a non-head ancestor throws InvalidOperationException unchanged; an absent frame whose handle has not previously completed disposal also throws unchanged. The handle's completion flag is idempotence state, not evidence of asynchronous-flow ownership. Inherited-child disposal and foreign calls after completion remain prohibited misuse with no promised automatic detection. Change the misleading absent-frame error message. Never add task-ID/thread-ID/ExecutionContext/AsyncLocal-token heuristics.

Example planned use:

```csharp
using var scope = runtime.BeginScope(context);
await ProcessOrderAsync();
```

The method that opens the scope keeps and disposes the handle. Child tasks inherit copied scope values and may open/dispose their own scopes. They must not receive the parent's handle. A scope opened inside an async helper stays inside that helper; returning its handle does not install a scope in the caller's context. Unawaited child work retains its inherited context independently, so its lifetime must be documented deliberately.

Alternative: reconsider direct callback APIs such as WithScopeAsync(context, work). Keeping the direct handle private reduces transfer opportunities, but cannot eliminate the product's handle-based model: Microsoft ILogger.BeginScope and IExternalScopeProvider.Push must still return IDisposable. This would change the guide's API, reference surface and consumers, and need separate synchronous/async callback and detached-work rules. No stronger product-wide ownership claim follows from adding a callback method.

Evidence: ownership-probe/windows.stdout.json and linux.stdout.json execute the same reviewed Core and same probe DLL under exactly .NET10.0.12 with roll-forward disabled. They reproduce inherited frame/ExecutionContext/AsyncLocal token sharing, null Task.CurrentId after selected awaits, owner thread/context changes, absent foreign calls before/after owner disposal, and actual LIFO/repeat behavior. These are internal-store investigations, not public runtime/provider qualification or conformance passes. One run per platform; null task-ID examples are deliberately constructed, not a universal task claim.

Claude's read-only review recommends the original caller contract, precise detectable-state/error wording, async-helper/unawaited-work documentation, and corrects the callback alternative's Microsoft-provider limitation. Its Windows-only evidence limitation applied to the input list for that review; Ubuntu subsequently completed with identical managed hashes. Nine new valid-flow native tests pass. They do not claim to detect inherited misuse. No production or effective contract changes have been made pending the user's choice.

Primary runtime reference: https://github.com/dotnet/runtime/blob/v10.0.12/src/libraries/System.Private.CoreLib/src/System/Threading/ExecutionContext.cs (Capture returns the immutable ambient context; CreateCopy returns this). See also Microsoft Task.CurrentId documentation; it is the executing task's nullable ID, not an async-flow identifier. These source facts support the design inference; actual probes are retained separately.
