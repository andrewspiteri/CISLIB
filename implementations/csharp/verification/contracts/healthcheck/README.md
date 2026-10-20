# HC00 Healthcheck compiler contract

`Reference/` contains the **nonshipping** shape of runtime entry points that have
not been implemented. It references the real `CIS.Core` foundation contracts,
options, detached values, and DTOs. Every facade entry point throws an explicit
reference-only `NotSupportedException`; none represents working health behavior.
Its project produces a nonshipping facade DLL for ordinary compiler consumption;
that assembly supplies no executable evidence.

`Consumer/` contains compiler-only usage scenarios. It is a class library with
no `Main` entry point. Compilation checks policy construction, immutable snapshot
reads, report views, owner-controlled collection registration, execution-fact
binding, HTTP installation, and pipe lifecycle signatures. The methods are never
executed and do not count as native runtime tests, transport tests, or Heartbeat
integration evidence.

The coordinator owns project files and the build receipt. The projects are
nonpackable and reference the pinned framework plus the actual Core project;
there is no additional HTTP adapter package. The application owns service,
runner, pipe, monitor, collection-registration, and binding lifetimes. Collector
ownership is expressed through `IHealthCollector.TryAcquire` and a real owner
lease; these compile consumers accept such an owner instead of faking one.

When actual runtime implementations arrive, remove the corresponding facade
declarations and compile these consumers against Core directly. Runtime tests
must separately exercise the actual implementation at the recorded revision.
