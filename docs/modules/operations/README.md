---
title: Durable operations and idempotency
type: guide
status: Draft
---

# Durable operations and original results

`CIS.Operations` coordinates a short authorized operation in the application's
explicit local transaction. A matching retry recovers its original committed
business result while the complete scoped identity remains protected. The
application owns commit and data-source lifetime.

- [Coordinator, identity and ownership](core.md)
- [Production PostgreSQL journal and explicit installation](postgresql.md)
- [Privileged retention maintenance and recovery](retention.md)
- [Bounded Logging and Metrics](observability.md)
- [Compiling consumer and completion/recovery flow](example.md)
- [Public CommonTesting substitutions and package evidence](testing.md)
- [Portable contracts and exact canonical vectors](../../../contracts/operations/README.md)
- [Requirements, approved plan and current evidence](../../../planning/operations/README.md)
