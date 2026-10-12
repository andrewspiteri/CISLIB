#!/usr/bin/env bash
set -euo pipefail
cat /etc/os-release > /mnt/c/miscwork/portfolio/cislib/.artifacts/csharp/tasks/wp05-s01-t02/linux-os-release.txt
python3 /mnt/c/miscwork/portfolio/cislib/implementations/csharp/tests/fixtures/DiscoveryHost/verify-human-rendering.py --dotnet /mnt/c/miscwork/portfolio/cislib/.artifacts/csharp/tasks/wp03-s01-t04/linux-runtime/dotnet --host /mnt/c/miscwork/portfolio/cislib/implementations/csharp/tests/fixtures/DiscoveryHost/bin/Release/net10.0/CIS.Core.DiscoveryFixture.dll --catalog /mnt/c/miscwork/portfolio/cislib/conformance/logging/human-rendering-cases.json --output /mnt/c/miscwork/portfolio/cislib/.artifacts/csharp/tasks/wp05-s01-t02/native/linux --family linux
