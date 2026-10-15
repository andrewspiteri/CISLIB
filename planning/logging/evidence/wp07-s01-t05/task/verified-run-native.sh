set -eu
[ ! -e /tmp/cislib-wp07-s01-t05-3a3c9e92a61d4c8db557133d36af6296 ]
mkdir /tmp/cislib-wp07-s01-t05-3a3c9e92a61d4c8db557133d36af6296
sha256sum /mnt/c/miscwork/portfolio/cislib/.artifacts/csharp/tasks/wp07-s01-t05/verified-native-source.tar > /mnt/c/miscwork/portfolio/cislib/.artifacts/csharp/tasks/wp07-s01-t05/verified-native-payload.sha256
tar -xf /mnt/c/miscwork/portfolio/cislib/.artifacts/csharp/tasks/wp07-s01-t05/verified-native-source.tar -C /tmp/cislib-wp07-s01-t05-3a3c9e92a61d4c8db557133d36af6296
mkdir -p /tmp/cislib-wp07-s01-t05-3a3c9e92a61d4c8db557133d36af6296-tools/sdk /tmp/cislib-wp07-s01-t05-3a3c9e92a61d4c8db557133d36af6296-tools/pwsh
tar -cf - -C /mnt/c/miscwork/portfolio/cislib/.artifacts/type-manager/toolchains/linux-sdk-10.0.401 . | tar -xf - -C /tmp/cislib-wp07-s01-t05-3a3c9e92a61d4c8db557133d36af6296-tools/sdk
tar -cf - -C /mnt/c/miscwork/portfolio/cislib/.artifacts/csharp/tasks/wp07-s01-t05/tools/powershell . | tar -xf - -C /tmp/cislib-wp07-s01-t05-3a3c9e92a61d4c8db557133d36af6296-tools/pwsh
printf "Native tools copied to Linux storage\n"
export DOTNET_ROOT=/tmp/cislib-wp07-s01-t05-3a3c9e92a61d4c8db557133d36af6296-tools/sdk
export PATH=/tmp/cislib-wp07-s01-t05-3a3c9e92a61d4c8db557133d36af6296-tools/pwsh:/tmp/cislib-wp07-s01-t05-3a3c9e92a61d4c8db557133d36af6296-tools/sdk:"$PATH"
export NUGET_PACKAGES=/tmp/cislib-wp07-s01-t05-3a3c9e92a61d4c8db557133d36af6296-packages
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export TESTINGPLATFORM_TELEMETRY_OPTOUT=1
export MSBUILDDISABLENODEREUSE=1
export UseSharedCompilation=false
export DOTNET_CLI_USE_MSBUILD_SERVER=0
cd /tmp/cislib-wp07-s01-t05-3a3c9e92a61d4c8db557133d36af6296
cat /etc/os-release > /tmp/cislib-wp07-s01-t05-3a3c9e92a61d4c8db557133d36af6296/os-release.txt
dotnet --info > /tmp/cislib-wp07-s01-t05-3a3c9e92a61d4c8db557133d36af6296/dotnet-info-root.txt
git rev-parse HEAD > /tmp/cislib-wp07-s01-t05-3a3c9e92a61d4c8db557133d36af6296/snapshot-commit.txt
set +e
pwsh -NoProfile -File eng/verify.ps1 -Profile Developer -MaxBuildNodes 1
code=$?
set -e
printf "%s\n" "$code" > verified-native.exit
cd implementations/csharp
dotnet --info > /tmp/cislib-wp07-s01-t05-3a3c9e92a61d4c8db557133d36af6296/dotnet-info-pinned.txt
pwsh -NoProfile -Command '$PSVersionTable | ConvertTo-Json' > /tmp/cislib-wp07-s01-t05-3a3c9e92a61d4c8db557133d36af6296/powershell-info.json
cd /tmp/cislib-wp07-s01-t05-3a3c9e92a61d4c8db557133d36af6296
set +e
if [ -f .artifacts/csharp/verification/Developer/families/integration/report.trx ]; then python3 /mnt/c/miscwork/portfolio/cislib/.artifacts/csharp/tasks/wp07-s01-t05/collect-candidates.py --root /tmp/cislib-wp07-s01-t05-3a3c9e92a61d4c8db557133d36af6296 --output /tmp/cislib-wp07-s01-t05-3a3c9e92a61d4c8db557133d36af6296/.artifacts/csharp/verification/Developer/candidate-observations; fi
candidate_code=$?
set -e
if [ "$code" -eq 0 ] && [ "$candidate_code" -ne 0 ]; then code=$candidate_code; fi
tar -cf /mnt/c/miscwork/portfolio/cislib/.artifacts/csharp/tasks/wp07-s01-t05/verified-native-evidence.tar .artifacts/csharp/verification/Developer verified-native.exit os-release.txt dotnet-info-pinned.txt powershell-info.json snapshot-commit.txt
exit "$code"
