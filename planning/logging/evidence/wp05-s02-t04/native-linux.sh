#!/usr/bin/env bash
set -euo pipefail
root=/mnt/c/miscwork/portfolio/cislib
runtime="$root/.artifacts/csharp/tasks/wp03-s01-t04/linux-runtime/dotnet"
host="$root/implementations/csharp/tests/fixtures/DiscoveryHost/bin/Release/net10.0/CIS.Core.DiscoveryFixture.dll"
fixtures="$root/implementations/csharp/tests/fixtures/DiscoveryHost"
base="$root/.artifacts/csharp/tasks/wp05-s02-t04/native"
cat /etc/os-release > "$root/.artifacts/csharp/tasks/wp05-s02-t04/linux-os-release.txt"
python3 "$fixtures/verify-queue-bursts.py" --dotnet "$runtime" --host "$host" --catalog "$root/conformance/logging/queue-burst-cases.json" --output "$base/linux" --family linux
python3 "$fixtures/verify-queued-fanout.py" --dotnet "$runtime" --host "$host" --catalog "$root/conformance/logging/queue-overflow-cases.json" --output "$base/linux-overflow" --family linux --task WP05-S02-T03
python3 "$fixtures/verify-queued-fanout.py" --dotnet "$runtime" --host "$host" --catalog "$root/conformance/logging/queued-fanout-cases.json" --output "$base/linux-fanout" --family linux
python3 "$fixtures/verify-output-queues.py" --dotnet "$runtime" --host "$host" --catalog "$root/conformance/logging/output-queue-cases.json" --output "$base/linux-foundation" --family linux
