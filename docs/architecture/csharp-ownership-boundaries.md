---
title: 'Ownership and dependency boundaries'
type: architecture
status: unknown
cis:
  stable_id: cislib.architecture.architecture.csharp.ownership.boundaries
---

# Ownership and dependency boundaries

Applications own runtimes and external resources. Loggers and the Microsoft provider borrow their runtime. Custom writers are borrowed unless ownership is explicitly transferred by successful startup; failed startup leaves caller ownership intact. Keep resources alive until real worker/cleanup completion, including after a bounded stop times out.

Captured metadata, values, scope/event data and public result/diagnostic/snapshot values own their bounded immutable observations. Constructors copy caller collections where promised; readonly fields or interfaces alone do not prove ownership. Runtime configuration is captured before work begins. Concurrent setup mutation is unsupported. `EnvironmentOptions` keeps readonly automatic facts, ordinary caller policy fields and a getter-only mutable logging-options reference.

Configuration, text/value/event capture and immutable contracts do not depend on output queues, concrete writers, provider orchestration, CIS or test tooling. Runtime/provider layers perform composition. Mutable runtime lifecycle and synchronization state remain internal where needed. Healthcheck snapshots detach owner/collector graphs; collection leases and settlement barriers protect actual owner lifetime.

The Type Manager helper is a package build asset, absent from application runtime deployment. Test binaries, checkers, copied source/evidence trees and unrelated scripts are not shipping assets. Meaningful ownership, mutation-after-capture, concurrency, settlement and package-consumer behavior belongs in native tests. The former generated architecture inspection protocol is retired.
