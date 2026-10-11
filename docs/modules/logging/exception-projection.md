# Exception capture foundation

WP04-S02-T01 implements internal `CIS.Logging.Exceptions.ExceptionProjection.Capture` and an owned `CapturedException` model in CIS.Core. They prepare the exception part of later event capture; there is no new public exception API, logger, queue or shipping serializer. The [original guide](../../../planning/logging/sources/core-library-implementation-guide-v1.md) supplies the declared fields and bounds. The [plan amendments](../../../planning/logging/plan-amendments.md) preserve the user's truncation direction.

Capture accepts a nullable Exception. Null produces no exception projection. Every nonnull node copies its actual runtime full type name (simple name fallback), Message and optional StackTrace. Message and StackTrace are read once by the capture helper. A null message from an override becomes the required empty string; a null stack is absent, while an explicitly empty stack remains a supplied string. Single inner exceptions use an array of one. AggregateException children retain their original order and nested shape without flattening; each occurrence of a shared exception produces a distinct owned node.

The result stores immutable strings and a copied read-only collection of captured children. It retains no Exception, runtime Type, arbitrary object, delegate or caller collection. Data, Source, HelpLink and arbitrary properties are not read or serialized, and capture never calls arbitrary ToString. Internal shape metadata gives node count and depth for later event construction. The future wire shape is:

```json
{
  "type": "System.InvalidOperationException",
  "message": "outer",
  "inner_exceptions": [
    { "type": "System.ArgumentException", "message": "inner" }
  ]
}
```

Initial safety guards enforce at most 16 retained exception nodes and depth 4, with the root at node/depth 1. Traversal is preorder and counts each containment occurrence. Message and stack prefixes retain at most 4096 and 16384 Unicode scalars respectively; the shared CIS.Text prefix helper checks each retained scalar and clips without splitting surrogate pairs or copying an oversized suffix. Valid controls remain text for the later renderer to escape. Malformed retained UTF-16 throws a fixed ArgumentException with parameter `value`, without echoing rejected text. The omitted suffix is not inspected.

A node's IsTruncated records omitted text or children at that node. HasTruncatedContent also reflects loss in retained descendants, leaving the local marker distinct from whole-tree loss. Shape and loss metadata are internal capture state; aggregate encoded-entry measurement, rendering and runtime InvalidEvent accounting remain later work. T02/T03 will qualify all exact/over boundaries, mixed preorder budgets and detailed marker/exclusion semantics; T04 will complete the shared deterministic fixtures.

This synchronous boundary requires cooperative accessors. Virtual exception getters can allocate, block, throw or mutate their own state; capture propagates their failures without inventing successful fields. AggregateException.Message may itself inspect its children and construct a composite message inside its getter. The helper's bounded copying does not place a wall-clock or allocation bound on caller/BCL getter execution, and sequential reads do not promise a transaction across concurrently changing getters. These are implementation boundaries, not additional instructions attributed to the user.

[T01 evidence](../../../planning/logging/evidence/wp04-s02-t01-windows-linux.json) retains 18 new unit, six integration and three architecture cases bound to twelve native methods. Unit tests exercise required fields across three cultures, actual thrown/unthrown stacks, single/aggregate order, repeated occurrences, read-only children, mutable getters, initial oversize guards, safe malformed-text errors and propagated accessor failures. Architecture checks inspect actual compiled calls/model storage and detect six forbidden never-invoked controls. An initial guard classified the fallback type-name accessor as arbitrary reflection because Type.Name is declared on MemberInfo; its retained failing report and narrowly corrected identity allowance remain separate from the passing run. The initial wrong solution filename also retains its failed build command record.

Windows Developer verification and a fresh copied-source replay pass 1980 unit, 219 integration and 385 architecture cases (2584 total), with no failures or skips. All 55 existing compiler controls still match. Six actual capture children run on Windows and Ubuntu with identical host/Core bytes under exact .NET 10.0.12, roll-forward disabled, invalid logging configuration unread, startup uninitialized and no output created. The process JSON projection is a development-only observation. Two approved runtime packages remain unchanged. Full Linux SDK/native-family, event/runtime, release and collector qualification remain pending; S02 is still in progress.
