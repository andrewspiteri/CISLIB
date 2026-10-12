#!/usr/bin/env bash
set -eu
repo=/mnt/c/miscwork/portfolio/cislib
drivers="$repo/implementations/csharp/tests/fixtures/DiscoveryHost"
host="$drivers/bin/Release/net10.0/CIS.Core.DiscoveryFixture.dll"
runtime="$repo/.artifacts/csharp/tasks/wp03-s01-t04/linux-runtime/dotnet"
output="$repo/.artifacts/csharp/tasks/environment-constructor/native"
python3 "$drivers/verify-constructor-options.py" --dotnet "$runtime" --host "$host" --catalog "$repo/contracts/logging/v1/configuration.json" --family linux --output "$output/linux-constructor"
python3 "$drivers/verify-process.py" --dotnet "$runtime" --host "$host" --family linux --output "$output/linux-process"
python3 "$drivers/verify-configuration.py" --dotnet "$runtime" --host "$host" --inputs "$repo/conformance/logging/configuration-resolution-cases.json" --family linux --output "$output/linux-configuration"
python3 "$drivers/verify-validation.py" --dotnet "$runtime" --host "$host" --inputs "$repo/conformance/logging/passive-configuration-validation-cases.json" --family linux --output "$output/linux-validation"
python3 "$drivers/verify-paths.py" --dotnet "$runtime" --host "$host" --inputs "$repo/conformance/logging/configuration-cases.json" --family linux --output "$output/linux-paths"
python3 "$drivers/verify-event-correlation.py" --dotnet "$runtime" --host "$host" --inputs "$repo/conformance/logging/event-correlation-cases.json" --family linux --output "$output/linux-correlation"
