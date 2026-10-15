set -eu
[ ! -e /tmp/cislib-wp07-s01-t02-da7b9167abe5498cb0cff5949c6a5dbd ]
mkdir -p /tmp/cislib-wp07-s01-t02-da7b9167abe5498cb0cff5949c6a5dbd
tar -xf /mnt/c/miscwork/portfolio/cislib/.artifacts/csharp/tasks/wp07-s01-t02/linux-payload-04.tar -C /tmp/cislib-wp07-s01-t02-da7b9167abe5498cb0cff5949c6a5dbd
export DOTNET_ROOT=/mnt/c/miscwork/portfolio/cislib/.artifacts/type-manager/toolchains/linux-sdk-10.0.401
export PATH=/mnt/c/miscwork/portfolio/cislib/.artifacts/type-manager/toolchains/linux-sdk-10.0.401:"$PATH"
cd /tmp/cislib-wp07-s01-t02-da7b9167abe5498cb0cff5949c6a5dbd/implementations/csharp
cat /etc/os-release > /tmp/cislib-wp07-s01-t02-da7b9167abe5498cb0cff5949c6a5dbd/os-release.txt
/mnt/c/miscwork/portfolio/cislib/.artifacts/type-manager/toolchains/linux-sdk-10.0.401/dotnet --info > /tmp/cislib-wp07-s01-t02-da7b9167abe5498cb0cff5949c6a5dbd/dotnet-info.txt
set +e
/mnt/c/miscwork/portfolio/cislib/.artifacts/type-manager/toolchains/linux-sdk-10.0.401/dotnet vstest /tmp/cislib-wp07-s01-t02-da7b9167abe5498cb0cff5949c6a5dbd/implementations/csharp/tests/CIS.Core.IntegrationTests/bin/Release/net10.0/CIS.Core.IntegrationTests.dll '--TestCaseFilter:FullyQualifiedName~CandidateLoggingProcessTests' --ResultsDirectory:/tmp/cislib-wp07-s01-t02-da7b9167abe5498cb0cff5949c6a5dbd/results '--logger:trx;LogFileName=report.trx'
case_code=$?
set -e
cd /tmp/cislib-wp07-s01-t02-da7b9167abe5498cb0cff5949c6a5dbd
tar -cf /mnt/c/miscwork/portfolio/cislib/.artifacts/csharp/tasks/wp07-s01-t02/linux-evidence-04.tar results .artifacts/csharp/candidate-consumers os-release.txt dotnet-info.txt
exit "$case_code"
