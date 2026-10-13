# Run roles and concrete source changes

Final qualification must use the final Developer source manifest and final native host/Core/catalog hashes. Earlier focused49unit and37/4failed integration reports predate strict UTF-8 decoding and stronger complete verbose/default-path checks. Corrected focused integration41 and native-focused41 pass; final Developer/native runs qualify all final critical inputs.

The initial four physical parent failures came from ProcessRunner using the Windows host code page for actual UTF-8 stream bytes. Both readers now decode strict UTF-8. Shipping console bytes were unchanged by that helper correction. The independent native driver preserves original stdout/stderr bytes before decoding and rejects unsafe framing/BOM/facts.

A standalone full architecture run passed416 and failed the actual-Core compiler consumer hash check because its upstream compiler-control receipt still named the previous task's Core DLL. No assertion was changed or weakened. Final Developer regenerates compiler controls before architecture, which is the authoritative qualification path.

Console creation copies consumed private values before stream selection; no public snapshot/factory placeholder is added. Output representations are prepared before admission so combined capture/display loss counts once. Slots keep the original JSONL plus optional prepared console memory, without rewriting file events. Full borrowed ownership/startup/environment consumers and public bounded runtime/file/provider/release remain pending.
