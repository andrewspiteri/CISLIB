# Logging module

Implement logging contracts, immutable event capture, context, output selection, buffering, formatting and lifecycle in this folder, using namespace `CIS.Logging` and responsibility-specific children where appropriate.

This module is compiled into the Core project. It is not a separate project/package. Future Core modules are siblings of this folder.

`Configuration/SeverityTokenParser.cs` is the first implemented behavior. It recognizes the seven named levels plus the user-approved `TRC`, `DBG`, `INF`/`INFO`, `WRN`/`WARN`, `ERR` and `CRT`/`CRIT` abbreviations, with invariant case normalization and trimming. It keeps absent, valid and invalid input distinct and supplies no fallback/default. It remains internal and is preserved for the later configuration resolver. Runtime logging and MicrosoftLogger behavior are still planned.

`LogValue` now implements the typed immutable attribute model, including bounded owned collections. `Exceptions/` holds the internal owned exception capture foundation for later event creation; [exception guidance](../../../../../docs/modules/logging/exception-projection.md) records its scope. Neither adds a working logger/output lifecycle.
