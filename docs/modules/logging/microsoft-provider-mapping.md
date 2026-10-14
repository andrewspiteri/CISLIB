# Microsoft event mappings

CoreLoggerProvider maps every defined Microsoft severity to the matching Core severity. Trace, Debug, Information, Warning, Error and Critical project as trace, debug, info, warn, error and critical. None has no destination interest and does not invoke the formatter. The existing category validation and undefined-level exceptions remain unchanged.

EventId.Name becomes event_name. Null or empty names become log.message; other supplied names remain subject to core validation. The numeric EventId.Id is always retained as attributes.event_id, including default zero, negative IDs and both Int32 limits. It is an ordinary core integer value, created inside the existing lazy capture path. This provider-owned attribute will retain numeric ID precedence when structured state is added in T03.

The provider invokes the supplied formatter with the original state and exception and forwards its returned message unchanged. It uses the same core exception projection as direct logging; Exception.Data is excluded. It adds no template parser, reflection serialization, dependency, worker or retry.

Four cases compare provider and equivalent direct-core records at all emitted levels, across named/absent/empty names, category changes and the integer extremes. A controlled payload case distinguishes rendered text from input state and verifies exact formatter exception identity and projection. Each pair uses the same metadata, explicit fields and exception; independently sampled timestamps are excluded from semantic comparison. Physical cases use actual public creation/file ownership and independent raw-file checks. Twenty-two targeted corruption controls validate the reader. Counts and executed evidence are recorded in the task receipt.

Structured state and {OriginalFormat} remain T03; complete filter/rejection behavior and story qualification remain T04/T05. Host scopes, final provider surface, examples, instructions/skills and release qualification remain required. This task adds no owning provider mode or public placeholder API.
