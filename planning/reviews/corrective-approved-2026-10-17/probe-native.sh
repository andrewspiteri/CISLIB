#!/bin/sh
set -eu
task_base=/home/user/cislib-verification/ubuntu-developer-396e87d6-archive
export DOTNET_ROOT="$task_base/toolchain/sdk"
export PATH="$task_base/toolchain/powershell:$DOTNET_ROOT:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin"
export DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1
task_output="$task_base/claude-review-2026-10-17-probes"
mkdir -p "$task_output"
dotnet run --file /mnt/c/miscwork/portfolio/cislib/.artifacts/claude-review-2026-10-17-approved/ProbeInventory.cs -- "$task_base/source" "$task_output"
cp "$task_output/source-inventory-probes.json" /mnt/c/miscwork/portfolio/cislib/.artifacts/claude-review-2026-10-17-approved/source-inventory-probes.json
