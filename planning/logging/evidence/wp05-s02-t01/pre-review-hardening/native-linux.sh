#!/usr/bin/env bash
set -euo pipefail
cat /etc/os-release > /mnt/c/miscwork/portfolio/cislib/.artifacts/csharp/tasks/wp05-s02-t01/linux-os-release.txt
python3 /mnt/c/miscwork/portfolio/cislib/implementations/csharp/tests/fixtures/DiscoveryHost/verify-output-queues.py --dotnet /mnt/c/miscwork/portfolio/cislib/.artifacts/csharp/tasks/wp03-s01-t04/linux-runtime/dotnet --host /mnt/c/miscwork/portfolio/cislib/implementations/csharp/tests/fixtures/DiscoveryHost/bin/Release/net10.0/CIS.Core.DiscoveryFixture.dll --catalog /mnt/c/miscwork/portfolio/cislib/conformance/logging/output-queue-cases.json --output /mnt/c/miscwork/portfolio/cislib/.artifacts/csharp/tasks/wp05-s02-t01/native/linux --family linux
