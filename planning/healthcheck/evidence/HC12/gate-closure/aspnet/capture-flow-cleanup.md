# ASP.NET capture flow cleanup

The coordinator's actual a2 native log reports CS0165 for the unassigned
`authorized` and `report` locals during instrumented compilation, followed by
safe-mode rollback of the 72 `HandleAsync` mutations. Those rollback labels do
not prove that each original mutation is independently uncompilable.

This candidate initializes authorization to false before the existing awaited
authorization try/catches. Synchronous snapshot capture and formatting move to a
private `CaptureReport` helper that returns its actual buffer directly from each
successful or error path. `HandleAsync` initializes its report once from that
return. Successful requests still capture and format exactly once. Failed capture
or formatting creates `ReportingUnavailable` only on the actual exception path;
there is no eager fallback allocation or placeholder report.

A source/formatter OperationCanceledException is rethrown when the request token
is canceled, allowing the existing outer handler to Abort before writing response
metadata. An unrelated source cancellation remains a reporting-unavailable
response. Authorization cancellation/error handling, route selection, headers,
GET/HEAD writes and public API remain unchanged.

Only the ASP.NET implementation and this note change. The existing 70-case HTTP
selection (including scheduling) stays byte-identical. A fresh source snapshot,
native baseline and full selected mutation run are required to measure actual
compilation/behavior and the strict threshold. No omission reduction, kill or
qualification is claimed. Frozen04, a1/a2 and historical/current omission replay
reports remain evidence for their own exact prior sources and harnesses.
