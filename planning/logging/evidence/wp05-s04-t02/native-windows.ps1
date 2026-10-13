$ErrorActionPreference = 'Stop'
$taskRoot = '.artifacts/csharp/tasks/wp05-s04-t02'
python -X utf8 implementations/csharp/tests/fixtures/DiscoveryHost/verify-console-ownership.py --dotnet dotnet --host C:/miscwork/portfolio/cislib/implementations/csharp/tests/fixtures/DiscoveryHost/bin/Release/net10.0/CIS.Core.DiscoveryFixture.dll --catalog C:/miscwork/portfolio/cislib/conformance/logging/console-ownership-cases.json --output "$taskRoot/native/windows-ownership" --family windows *> "$taskRoot/windows-ownership.log"
$nativeExit = $LASTEXITCODE
$nativeExit | Set-Content "$taskRoot/windows-ownership.exit-code"
if ($nativeExit -ne 0) { Get-Content "$taskRoot/windows-ownership.log" -Tail 12; throw 'Native windows ownership failed.' }
Get-Content "$taskRoot/windows-ownership.log" -Tail 1

python -X utf8 implementations/csharp/tests/fixtures/DiscoveryHost/verify-console-composition.py --dotnet dotnet --host C:/miscwork/portfolio/cislib/implementations/csharp/tests/fixtures/DiscoveryHost/bin/Release/net10.0/CIS.Core.DiscoveryFixture.dll --catalog C:/miscwork/portfolio/cislib/conformance/logging/console-composition-cases.json --output "$taskRoot/native/windows-console" --family windows *> "$taskRoot/windows-console.log"
$nativeExit = $LASTEXITCODE
$nativeExit | Set-Content "$taskRoot/windows-console.exit-code"
if ($nativeExit -ne 0) { Get-Content "$taskRoot/windows-console.log" -Tail 10; throw 'Native windows console failed.' }
Get-Content "$taskRoot/windows-console.log" -Tail 1
python -X utf8 implementations/csharp/tests/fixtures/DiscoveryHost/verify-diagnostics.py --dotnet dotnet --host C:/miscwork/portfolio/cislib/implementations/csharp/tests/fixtures/DiscoveryHost/bin/Release/net10.0/CIS.Core.DiscoveryFixture.dll --catalog C:/miscwork/portfolio/cislib/conformance/logging/diagnostics-cases.json --output "$taskRoot/native/windows-diagnostics" --family windows *> "$taskRoot/windows-diagnostics.log"
$nativeExit = $LASTEXITCODE
$nativeExit | Set-Content "$taskRoot/windows-diagnostics.exit-code"
if ($nativeExit -ne 0) { Get-Content "$taskRoot/windows-diagnostics.log" -Tail 10; throw 'Native windows-diagnostics failed.' }
Get-Content "$taskRoot/windows-diagnostics.log" -Tail 1
python -X utf8 implementations/csharp/tests/fixtures/DiscoveryHost/verify-output-states.py --dotnet dotnet --host C:/miscwork/portfolio/cislib/implementations/csharp/tests/fixtures/DiscoveryHost/bin/Release/net10.0/CIS.Core.DiscoveryFixture.dll --catalog C:/miscwork/portfolio/cislib/conformance/logging/output-state-cases.json --output "$taskRoot/native/windows-state" --family windows *> "$taskRoot/windows-state.log"
$nativeExit = $LASTEXITCODE
$nativeExit | Set-Content "$taskRoot/windows-state.exit-code"
if ($nativeExit -ne 0) { Get-Content "$taskRoot/windows-state.log" -Tail 10; throw 'Native windows-state failed.' }
Get-Content "$taskRoot/windows-state.log" -Tail 1
python -X utf8 implementations/csharp/tests/fixtures/DiscoveryHost/verify-delivery-observations.py --dotnet dotnet --host C:/miscwork/portfolio/cislib/implementations/csharp/tests/fixtures/DiscoveryHost/bin/Release/net10.0/CIS.Core.DiscoveryFixture.dll --catalog C:/miscwork/portfolio/cislib/conformance/logging/delivery-observation-cases.json --output "$taskRoot/native/windows" --family windows *> "$taskRoot/windows.log"
$nativeExit = $LASTEXITCODE
$nativeExit | Set-Content "$taskRoot/windows.exit-code"
if ($nativeExit -ne 0) { Get-Content "$taskRoot/windows.log" -Tail 12; throw 'Native windows delivery failed.' }
Get-Content "$taskRoot/windows.log" -Tail 1
python -X utf8 implementations/csharp/tests/fixtures/DiscoveryHost/verify-output-accounting.py --dotnet dotnet --host C:/miscwork/portfolio/cislib/implementations/csharp/tests/fixtures/DiscoveryHost/bin/Release/net10.0/CIS.Core.DiscoveryFixture.dll --catalog C:/miscwork/portfolio/cislib/conformance/logging/output-accounting-cases.json --output "$taskRoot/native/windows-accounting" --family windows *> "$taskRoot/windows-accounting.log"
$nativeExit = $LASTEXITCODE
$nativeExit | Set-Content "$taskRoot/windows-accounting.exit-code"
if ($nativeExit -ne 0) { Get-Content "$taskRoot/windows-accounting.log" -Tail 10; throw 'Native windows-accounting failed.' }
Get-Content "$taskRoot/windows-accounting.log" -Tail 1
python -X utf8 implementations/csharp/tests/fixtures/DiscoveryHost/verify-queue-bursts.py --dotnet dotnet --host C:/miscwork/portfolio/cislib/implementations/csharp/tests/fixtures/DiscoveryHost/bin/Release/net10.0/CIS.Core.DiscoveryFixture.dll --catalog C:/miscwork/portfolio/cislib/conformance/logging/queue-burst-cases.json --output "$taskRoot/native/windows-burst" --family windows *> "$taskRoot/windows-burst.log"
$nativeExit = $LASTEXITCODE
$nativeExit | Set-Content "$taskRoot/windows-burst.exit-code"
if ($nativeExit -ne 0) { Get-Content "$taskRoot/windows-burst.log" -Tail 10; throw 'Native windows-burst failed.' }
Get-Content "$taskRoot/windows-burst.log" -Tail 1
python -X utf8 implementations/csharp/tests/fixtures/DiscoveryHost/verify-queued-fanout.py --dotnet dotnet --host C:/miscwork/portfolio/cislib/implementations/csharp/tests/fixtures/DiscoveryHost/bin/Release/net10.0/CIS.Core.DiscoveryFixture.dll --catalog C:/miscwork/portfolio/cislib/conformance/logging/queue-overflow-cases.json --output "$taskRoot/native/windows-overflow" --family windows --task WP05-S02-T03 *> "$taskRoot/windows-overflow.log"
$nativeExit = $LASTEXITCODE
$nativeExit | Set-Content "$taskRoot/windows-overflow.exit-code"
if ($nativeExit -ne 0) { Get-Content "$taskRoot/windows-overflow.log" -Tail 10; throw 'Native windows-overflow failed.' }
Get-Content "$taskRoot/windows-overflow.log" -Tail 1
python -X utf8 implementations/csharp/tests/fixtures/DiscoveryHost/verify-queued-fanout.py --dotnet dotnet --host C:/miscwork/portfolio/cislib/implementations/csharp/tests/fixtures/DiscoveryHost/bin/Release/net10.0/CIS.Core.DiscoveryFixture.dll --catalog C:/miscwork/portfolio/cislib/conformance/logging/queued-fanout-cases.json --output "$taskRoot/native/windows-fanout" --family windows *> "$taskRoot/windows-fanout.log"
$nativeExit = $LASTEXITCODE
$nativeExit | Set-Content "$taskRoot/windows-fanout.exit-code"
if ($nativeExit -ne 0) { Get-Content "$taskRoot/windows-fanout.log" -Tail 10; throw 'Native windows-fanout failed.' }
Get-Content "$taskRoot/windows-fanout.log" -Tail 1
python -X utf8 implementations/csharp/tests/fixtures/DiscoveryHost/verify-output-queues.py --dotnet dotnet --host C:/miscwork/portfolio/cislib/implementations/csharp/tests/fixtures/DiscoveryHost/bin/Release/net10.0/CIS.Core.DiscoveryFixture.dll --catalog C:/miscwork/portfolio/cislib/conformance/logging/output-queue-cases.json --output "$taskRoot/native/windows-foundation" --family windows *> "$taskRoot/windows-foundation.log"
$nativeExit = $LASTEXITCODE
$nativeExit | Set-Content "$taskRoot/windows-foundation.exit-code"
if ($nativeExit -ne 0) { Get-Content "$taskRoot/windows-foundation.log" -Tail 10; throw 'Native windows-foundation failed.' }
Get-Content "$taskRoot/windows-foundation.log" -Tail 1
