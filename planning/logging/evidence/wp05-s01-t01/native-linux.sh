#!/usr/bin/env bash
set -euo pipefail
cd /mnt/c/miscwork/portfolio/cislib
python3 implementations/csharp/tests/fixtures/DiscoveryHost/verify-jsonl-reuse.py --dotnet /mnt/c/miscwork/portfolio/cislib/.artifacts/csharp/tasks/wp03-s01-t04/linux-runtime/dotnet --host /mnt/c/miscwork/portfolio/cislib/implementations/csharp/tests/fixtures/DiscoveryHost/bin/Release/net10.0/CIS.Core.DiscoveryFixture.dll --catalog /mnt/c/miscwork/portfolio/cislib/conformance/logging/jsonl-reuse-cases.json --family linux --output /mnt/c/miscwork/portfolio/cislib/.artifacts/csharp/tasks/wp05-s01-t01/native-linux
