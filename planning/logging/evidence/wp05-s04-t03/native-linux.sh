#!/usr/bin/env bash
set -euo pipefail
root=/mnt/c/miscwork/portfolio/cislib
runtime="$root/.artifacts/csharp/tasks/wp03-s01-t04/linux-runtime/dotnet"
host="$root/implementations/csharp/tests/fixtures/DiscoveryHost/bin/Release/net10.0/CIS.Core.DiscoveryFixture.dll"
fixtures="$root/implementations/csharp/tests/fixtures/DiscoveryHost"
base="$root/.artifacts/csharp/tasks/wp05-s04-t03/native"
cat /etc/os-release > "$root/.artifacts/csharp/tasks/wp05-s04-t03/linux-os-release.txt"
python3 -X utf8 "$fixtures/verify-console-startup.py" --dotnet "$runtime" --host "$host" --catalog "$root/conformance/logging/console-startup-cases.json" --output "$base/linux-startup" --family linux
python3 -X utf8 "$fixtures/verify-console-ownership.py" --dotnet "$runtime" --host "$host" --catalog "$root/conformance/logging/console-ownership-cases.json" --output "$base/linux-ownership" --family linux
python3 "$fixtures/verify-console-composition.py" --dotnet "$runtime" --host "$host" --catalog "$root/conformance/logging/console-composition-cases.json" --output "$base/linux-console" --family linux
python3 "$fixtures/verify-diagnostics.py" --dotnet "$runtime" --host "$host" --catalog "$root/conformance/logging/diagnostics-cases.json" --output "$base/linux-diagnostics" --family linux
python3 "$fixtures/verify-output-states.py" --dotnet "$runtime" --host "$host" --catalog "$root/conformance/logging/output-state-cases.json" --output "$base/linux-state" --family linux
python3 "$fixtures/verify-delivery-observations.py" --dotnet "$runtime" --host "$host" --catalog "$root/conformance/logging/delivery-observation-cases.json" --output "$base/linux" --family linux
python3 "$fixtures/verify-output-accounting.py" --dotnet "$runtime" --host "$host" --catalog "$root/conformance/logging/output-accounting-cases.json" --output "$base/linux-accounting" --family linux
python3 "$fixtures/verify-queue-bursts.py" --dotnet "$runtime" --host "$host" --catalog "$root/conformance/logging/queue-burst-cases.json" --output "$base/linux-burst" --family linux
python3 "$fixtures/verify-queued-fanout.py" --dotnet "$runtime" --host "$host" --catalog "$root/conformance/logging/queue-overflow-cases.json" --output "$base/linux-overflow" --family linux --task WP05-S02-T03
python3 "$fixtures/verify-queued-fanout.py" --dotnet "$runtime" --host "$host" --catalog "$root/conformance/logging/queued-fanout-cases.json" --output "$base/linux-fanout" --family linux
python3 "$fixtures/verify-output-queues.py" --dotnet "$runtime" --host "$host" --catalog "$root/conformance/logging/output-queue-cases.json" --output "$base/linux-foundation" --family linux
