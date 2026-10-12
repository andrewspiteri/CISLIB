#!/bin/bash
set -euo pipefail
repo=/mnt/c/miscwork/portfolio/cislib
runtime="$repo/.artifacts/csharp/tasks/wp03-s01-t04/linux-runtime/dotnet"
python3 "$repo/implementations/csharp/tests/fixtures/DiscoveryHost/verify-logger-foundation.py" \
  --dotnet "$runtime" \
  --host "$repo/implementations/csharp/tests/fixtures/DiscoveryHost/bin/Release/net10.0/CIS.Core.DiscoveryFixture.dll" \
  --catalog "$repo/conformance/logging/logger-foundation-cases.json" \
  --family linux \
  --output "$repo/.artifacts/csharp/tasks/wp04-s05-t01/native/linux-final"
