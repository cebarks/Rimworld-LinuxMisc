#!/usr/bin/env bash
set -euo pipefail

root=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
cd "$root"
build=Tests/Native/build
cc=${CC:-cc}
strict=(-std=c11 -Wall -Wextra -Wpedantic -Werror)

rm -rf Tests/Native/build
mkdir -p "$build"/{success,start-failure,old-host,partial-host}

"$cc" "${strict[@]}" -fPIC -shared Tests/Native/fake_gamemode.c \
    -Wl,-soname,libgamemode.so.0 -o "$build/success/libgamemode.so.0"
"$cc" "${strict[@]}" -fPIC -shared -DSTART_FAILS \
    '-DFAKE_ERROR="fake start failure"' Tests/Native/fake_gamemode.c \
    -Wl,-soname,libgamemode.so.0 -o "$build/start-failure/libgamemode.so.0"
"$cc" "${strict[@]}" -fPIC -shared -DOMIT_STATUS Tests/Native/fake_gamemode.c \
    -Wl,-soname,libgamemode.so.0 -o "$build/old-host/libgamemode.so.0"
"$cc" "${strict[@]}" -fPIC -shared -DOMIT_END Tests/Native/fake_gamemode.c \
    -Wl,-soname,libgamemode.so.0 -o "$build/partial-host/libgamemode.so.0"

"$cc" "${strict[@]}" -fPIC -shared -DLINUXMISC_GAMEMODE_TESTING \
    -INative Native/linuxmisc_gamemode.c -ldl -pthread \
    -Wl,-soname,liblinuxmisc_gamemode.so -o "$build/liblinuxmisc_gamemode_test.so"
"$cc" "${strict[@]}" -INative Tests/Native/test_shim.c \
    -ldl -pthread -o "$build/test_shim"

run_case() {
    local host=$1
    local scenario=$2
    LINUXMISC_GAMEMODE_TEST_HOST="$host" \
        "$build/test_shim" "$build/liblinuxmisc_gamemode_test.so" "$scenario"
}

run_case "$build/success/libgamemode.so.0" success
run_case "$build/success/libgamemode.so.0" concurrent
run_case "$build/start-failure/libgamemode.so.0" start-failure
run_case "$build/does-not-exist.so" missing-host
run_case "$build/old-host/libgamemode.so.0" missing-optional-status
run_case "$build/partial-host/libgamemode.so.0" missing-required-symbol

if readelf -d "$build/liblinuxmisc_gamemode_test.so" | grep -q 'Shared library: \[libgamemode'; then
    echo "FAIL: shim has a hard libgamemode dependency" >&2
    exit 1
fi

for symbol in linuxmisc_gamemode_start linuxmisc_gamemode_end linuxmisc_gamemode_status linuxmisc_gamemode_error_copy; do
    nm -D --defined-only "$build/liblinuxmisc_gamemode_test.so" | grep -q " $symbol$" || {
        echo "FAIL: missing exported ABI symbol $symbol" >&2
        exit 1
    }
done

echo "Native GameMode tests passed"
