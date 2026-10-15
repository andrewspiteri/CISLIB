set -eu
[ ! -e /tmp/cislib-wp07-s01-t02-7734db31e8714f54b6c3472cfc8d0cf5 ]
mkdir -p /tmp/cislib-wp07-s01-t02-7734db31e8714f54b6c3472cfc8d0cf5
tar -xf /mnt/c/miscwork/portfolio/cislib/.artifacts/csharp/tasks/wp07-s01-t02/linux-payload-05.tar -C /tmp/cislib-wp07-s01-t02-7734db31e8714f54b6c3472cfc8d0cf5
export DOTNET_ROOT=/mnt/c/miscwork/portfolio/cislib/.artifacts/type-manager/toolchains/linux-sdk-10.0.401
export PATH=/mnt/c/miscwork/portfolio/cislib/.artifacts/type-manager/toolchains/linux-sdk-10.0.401:"$PATH"
cd /tmp/cislib-wp07-s01-t02-7734db31e8714f54b6c3472cfc8d0cf5/implementations/csharp
cat /etc/os-release > /tmp/cislib-wp07-s01-t02-7734db31e8714f54b6c3472cfc8d0cf5/os-release.txt
/mnt/c/miscwork/portfolio/cislib/.artifacts/type-manager/toolchains/linux-sdk-10.0.401/dotnet --info > /tmp/cislib-wp07-s01-t02-7734db31e8714f54b6c3472cfc8d0cf5/dotnet-info.txt
sha256sum /tmp/cislib-wp07-s01-t02-7734db31e8714f54b6c3472cfc8d0cf5/implementations/csharp/tests/CIS.Core.IntegrationTests/bin/Release/net10.0/CIS.Core.IntegrationTests.dll > /tmp/cislib-wp07-s01-t02-7734db31e8714f54b6c3472cfc8d0cf5/test-assembly.sha256
set +e
/mnt/c/miscwork/portfolio/cislib/.artifacts/type-manager/toolchains/linux-sdk-10.0.401/dotnet vstest /tmp/cislib-wp07-s01-t02-7734db31e8714f54b6c3472cfc8d0cf5/implementations/csharp/tests/CIS.Core.IntegrationTests/bin/Release/net10.0/CIS.Core.IntegrationTests.dll '--TestCaseFilter:FullyQualifiedName~CandidateLoggingProcessTests' --ResultsDirectory:/tmp/cislib-wp07-s01-t02-7734db31e8714f54b6c3472cfc8d0cf5/results '--logger:trx;LogFileName=report.trx'
case_code=$?
set -e
cd /tmp/cislib-wp07-s01-t02-7734db31e8714f54b6c3472cfc8d0cf5
tar -cf /mnt/c/miscwork/portfolio/cislib/.artifacts/csharp/tasks/wp07-s01-t02/linux-evidence-05.tar results .artifacts/csharp/candidate-consumers os-release.txt dotnet-info.txt test-assembly.sha256
exit "$case_code"
