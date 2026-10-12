#!/usr/bin/env bash
set -euo pipefail
cd /mnt/c/miscwork/portfolio/cislib
python3 implementations/csharp/tests/fixtures/DiscoveryHost/verify-logger-outcome.py --dotnet /mnt/c/miscwork/portfolio/cislib/.artifacts/csharp/tasks/wp03-s01-t04/linux-runtime/dotnet --host /mnt/c/miscwork/portfolio/cislib/implementations/csharp/tests/fixtures/DiscoveryHost/bin/Release/net10.0/CIS.Core.DiscoveryFixture.dll --catalog /mnt/c/miscwork/portfolio/cislib/conformance/logging/logger-outcome-cases.json --family linux --output /mnt/c/miscwork/portfolio/cislib/.artifacts/csharp/tasks/wp04-s05-t04/native-linux
