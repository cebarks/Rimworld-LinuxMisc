#!/usr/bin/env bash
set -euo pipefail

root=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
project="$root/Tests/GameMode/GameMode.Tests.csproj"
native_build="$root/Tests/Native/build"
cc=${CC:-cc}

"$root/Tests/Native/run_tests.sh"

dotnet build "$project" -c Release --nologo
output="$root/Tests/GameMode/bin/Release/net10.0"
runner=(dotnet "$output/GameMode.Tests.dll")
shim="$output/liblinuxmisc_gamemode.so"

rm -f "$shim"
"${runner[@]}" session
"${runner[@]}" missing

"$cc" -std=c11 -Wall -Wextra -Wpedantic -Werror -fPIC -shared \
    "$root/Tests/GameMode/partial_shim.c" -o "$shim"
"${runner[@]}" partial

cp "$native_build/liblinuxmisc_gamemode_test.so" "$shim"
tmp_cwd=$(mktemp -d)
trap 'rmdir "$tmp_cwd"' EXIT
(
    cd "$tmp_cwd"
    LINUXMISC_GAMEMODE_TEST_HOST="$native_build/success/libgamemode.so.0" \
        "${runner[@]}" native-success
)
LINUXMISC_GAMEMODE_TEST_HOST="$native_build/start-failure/libgamemode.so.0" \
    "${runner[@]}" native-start-failure
LINUXMISC_GAMEMODE_TEST_HOST="$native_build/old-host/libgamemode.so.0" \
    "${runner[@]}" native-old-status

rm -f "$shim"
echo "Managed GameMode tests passed"
