set -eu
[ ! -e /tmp/cislib-wp07-s01-t03-66c838e02dfc4ace985badcb00708bbb ]
mkdir -p /tmp/cislib-wp07-s01-t03-66c838e02dfc4ace985badcb00708bbb
sha256sum /mnt/c/miscwork/portfolio/cislib/.artifacts/csharp/tasks/wp07-s01-t03/native-inputs.tar > /tmp/cislib-wp07-s01-t03-66c838e02dfc4ace985badcb00708bbb/payload.sha256
tar -xf /mnt/c/miscwork/portfolio/cislib/.artifacts/csharp/tasks/wp07-s01-t03/native-inputs.tar -C /tmp/cislib-wp07-s01-t03-66c838e02dfc4ace985badcb00708bbb
export DOTNET_ROOT=/mnt/c/miscwork/portfolio/cislib/.artifacts/type-manager/toolchains/linux-sdk-10.0.401
export PATH=/mnt/c/miscwork/portfolio/cislib/.artifacts/type-manager/toolchains/linux-sdk-10.0.401:"$PATH"
cd /tmp/cislib-wp07-s01-t03-66c838e02dfc4ace985badcb00708bbb/implementations/csharp
cat /etc/os-release > /tmp/cislib-wp07-s01-t03-66c838e02dfc4ace985badcb00708bbb/os-release.txt
/mnt/c/miscwork/portfolio/cislib/.artifacts/type-manager/toolchains/linux-sdk-10.0.401/dotnet --info > /tmp/cislib-wp07-s01-t03-66c838e02dfc4ace985badcb00708bbb/dotnet-info.txt
sha256sum /tmp/cislib-wp07-s01-t03-66c838e02dfc4ace985badcb00708bbb/implementations/csharp/tests/CIS.Core.UnitTests/bin/Release/net10.0/CIS.Core.UnitTests.dll /tmp/cislib-wp07-s01-t03-66c838e02dfc4ace985badcb00708bbb/implementations/csharp/tests/CIS.Core.IntegrationTests/bin/Release/net10.0/CIS.Core.IntegrationTests.dll > /tmp/cislib-wp07-s01-t03-66c838e02dfc4ace985badcb00708bbb/test-assemblies.sha256
set +e
/mnt/c/miscwork/portfolio/cislib/.artifacts/type-manager/toolchains/linux-sdk-10.0.401/dotnet vstest /tmp/cislib-wp07-s01-t03-66c838e02dfc4ace985badcb00708bbb/implementations/csharp/tests/CIS.Core.UnitTests/bin/Release/net10.0/CIS.Core.UnitTests.dll '--TestCaseFilter:FullyQualifiedName~ConcurrencyQualificationTests|FullyQualifiedName~FlushBoundaryTests|FullyQualifiedName~FlushCoordinationTests|FullyQualifiedName~FlushResultTests|FullyQualifiedName~FlushStressTests|FullyQualifiedName~StopStartTests|FullyQualifiedName~StopJoinTests|FullyQualifiedName~StopDrainTests|FullyQualifiedName~StopLifecycleTests|FullyQualifiedName~OutputAccountingTests' --ResultsDirectory:/tmp/cislib-wp07-s01-t03-66c838e02dfc4ace985badcb00708bbb/results/unit '--logger:trx;LogFileName=report.trx'
unit_code=$?
/mnt/c/miscwork/portfolio/cislib/.artifacts/type-manager/toolchains/linux-sdk-10.0.401/dotnet vstest /tmp/cislib-wp07-s01-t03-66c838e02dfc4ace985badcb00708bbb/implementations/csharp/tests/CIS.Core.IntegrationTests/bin/Release/net10.0/CIS.Core.IntegrationTests.dll '--TestCaseFilter:FullyQualifiedName~FlushBoundaryProcessTests|FullyQualifiedName~FlushCoordinationProcessTests|FullyQualifiedName~FlushResultProcessTests|FullyQualifiedName~FlushStressProcessTests|FullyQualifiedName~StopStartProcessTests|FullyQualifiedName~StopJoinProcessTests|FullyQualifiedName~StopDrainProcessTests|FullyQualifiedName~StopLifecycleProcessTests|FullyQualifiedName~OutputAccountingProcessTests' --ResultsDirectory:/tmp/cislib-wp07-s01-t03-66c838e02dfc4ace985badcb00708bbb/results/integration '--logger:trx;LogFileName=report.trx'
integration_code=$?
set -e
cd /tmp/cislib-wp07-s01-t03-66c838e02dfc4ace985badcb00708bbb
tar -cf /mnt/c/miscwork/portfolio/cislib/.artifacts/csharp/tasks/wp07-s01-t03/native-evidence.tar results payload.sha256 test-assemblies.sha256 os-release.txt dotnet-info.txt
[ "$unit_code" -eq 0 ] && [ "$integration_code" -eq 0 ]
