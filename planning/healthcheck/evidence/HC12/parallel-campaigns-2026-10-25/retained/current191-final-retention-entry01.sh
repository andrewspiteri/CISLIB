#!/bin/bash
cd /data/cis-healthcheck-20261021-01a15a72 || exit 99
hc191Task=/data/cis-healthcheck-20261021-01a15a72
hc191Retention="$hc191Task/retention-fast191-install01/.artifacts/hcg/retention-fast191-proposal/bin01/Retention.dll"
for hc191Done in current191-union-raw-entry01.exit.txt current191-join-entry01.exit.txt current191-joined-gate-entry01.exit.txt; do test -f "$hc191Done" || exit 98; done
test "$(cat current191-joined-gate-entry01.exit.txt)" = 0 || exit 97
{
printf '%s\n' current191-aspnet-index10-derived01 current191-aspnet-metadata01 current191-postclosure-inputs01 current191-unit-index12-derived01 current191-unit-metadata01 current191-unit-log-descriptor01 current191-pipes-index12-derived01 current191-pipes-metadata01 unit191-rotation-assessment01 pipes191-capture-assessment01 current191-union-input-assembly01 current191-union-raw01 current191-joined-input-assembly01 current191-joined-gate01
find . -maxdepth 1 -type f \( -name '*191*entry*' -o -name '*191*install*readback*' -o -name '*191*linux-readback*' -o -name '*191*receiv*' \) ! -name 'current191-final-retention-entry01*' -printf '%f\n'
printf '%s\n' current191-union-summary01.json current191-union-detail-summary01.json
} | LC_ALL=C sort -u >current191-final-retention-roots01.txt
jq -Rn '{Roots:[inputs],ExcludedPaths:[],ExcludedTarGzUnder:[]}' <current191-final-retention-roots01.txt >current191-final-retention-selection01.json
hc191Args=("$hc191Task/dotnet/dotnet" exec --fx-version 10.0.12 --roll-forward Disable "$hc191Retention" prepare "$hc191Task" "$hc191Task/current191-final-retention-selection01.json" "$hc191Task/current191-final-retention-manifest01.json" "$hc191Task/current191-final-retention-tar-inputs01.txt")
printf '%q ' "${hc191Args[@]}" >current191-final-retention-prepare01.arguments.txt
printf '\n' >>current191-final-retention-prepare01.arguments.txt
date -u +%FT%T.%NZ >current191-final-retention-entry01.started.txt
"${hc191Args[@]}" >current191-final-retention-prepare01.stdout.txt 2>current191-final-retention-prepare01.stderr.txt
hc191Exit=$?; printf '%s\n' "$hc191Exit" >current191-final-retention-prepare01.exit.txt
test "$hc191Exit" = 0 || exit "$hc191Exit"
test ! -e current191-final-audit-closed01.tar.gz || exit 96
tar -czf current191-final-audit-closed01.tar.gz -T current191-final-retention-tar-inputs01.txt >current191-final-tar01.stdout.txt 2>current191-final-tar01.stderr.txt
hc191Exit=$?; printf '%s\n' "$hc191Exit" >current191-final-tar01.exit.txt
test "$hc191Exit" = 0 || exit "$hc191Exit"
sha256sum current191-final-audit-closed01.tar.gz >current191-final-audit-closed01.sha256.txt
test ! -e current191-final-linux-readback01 || exit 95
mkdir current191-final-linux-readback01
tar -xzf current191-final-audit-closed01.tar.gz -C current191-final-linux-readback01 >current191-final-extract01.stdout.txt 2>current191-final-extract01.stderr.txt
hc191Exit=$?; printf '%s\n' "$hc191Exit" >current191-final-extract01.exit.txt
test "$hc191Exit" = 0 || exit "$hc191Exit"
hc191Args=("$hc191Task/dotnet/dotnet" exec --fx-version 10.0.12 --roll-forward Disable "$hc191Retention" verify "$hc191Task/current191-final-linux-readback01" "$hc191Task/current191-final-retention-manifest01.json" "$(sha256sum current191-final-retention-manifest01.json | cut -d' ' -f1)" "$hc191Task/current191-final-linux-readback01.json")
printf '%q ' "${hc191Args[@]}" >current191-final-linux-readback01.arguments.txt
printf '\n' >>current191-final-linux-readback01.arguments.txt
"${hc191Args[@]}" >current191-final-linux-readback01.stdout.txt 2>current191-final-linux-readback01.stderr.txt
printf '%s\n' "$?" >current191-final-linux-readback01.exit.txt
date -u +%FT%T.%NZ >current191-final-retention-entry01.finished.txt
