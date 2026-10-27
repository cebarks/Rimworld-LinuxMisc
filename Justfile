# Common development tasks for RimWorld Linux Misc

set shell := ["bash", "-euo", "pipefail", "-c"]

default:
    @just --list

alias b := build

# Compile the .NET mod and, on Linux x64, the packaged GameMode shim.
build:
    dotnet build Source/RimWorldLinuxMisc.csproj -c Release --nologo

# Compile the native shim with strict warnings.
native-build:
    make -C Native all

# Run native fake-host tests. These never contact a real GameMode daemon.
test-native:
    Tests/Native/run_tests.sh

# Run managed loader and lifecycle tests against controlled fake shims.
test-gamemode:
    Tests/GameMode/run_tests.sh

# Run all independent suites after lane integration.
# Set ALLOW_MISSING_LANE_TESTS=1 only while validating an isolated lane worktree.
test: test-gamemode
    #!/usr/bin/env bash
    set -euo pipefail
    for project in Tests/Affinity/Affinity.Tests.csproj Tests/Memory/Memory.Tests.csproj Tests/SettingsUi/SettingsUi.Tests.csproj; do
        if [[ -f "$project" ]]; then
            dotnet run --project "$project" -c Release
        elif [[ "${ALLOW_MISSING_LANE_TESTS:-0}" == "1" ]]; then
            echo "Skipping not-yet-merged lane test project: $project"
        else
            echo "Required test project is missing: $project" >&2
            exit 1
        fi
    done
    python3 -m unittest discover -s Tests/Integration -p 'test_*.py'

# Remove production, native, test, and release outputs.
clean:
    dotnet clean Source/RimWorldLinuxMisc.csproj --nologo
    if [[ -f Tests/GameMode/GameMode.Tests.csproj ]]; then dotnet clean Tests/GameMode/GameMode.Tests.csproj --nologo; fi
    if [[ -f Tests/Affinity/Affinity.Tests.csproj ]]; then dotnet clean Tests/Affinity/Affinity.Tests.csproj --nologo; fi
    if [[ -f Tests/Memory/Memory.Tests.csproj ]]; then dotnet clean Tests/Memory/Memory.Tests.csproj --nologo; fi
    if [[ -f Tests/SettingsUi/SettingsUi.Tests.csproj ]]; then dotnet clean Tests/SettingsUi/SettingsUi.Tests.csproj --nologo; fi
    make -C Native clean
    rm -f Assemblies/*.dll Assemblies/*.pdb Assemblies/*.so Assemblies/*.deps.json Assemblies/*.runtimeconfig.json
    rm -f LinuxMisc-*.zip
    rm -rf Source/bin Source/obj Tests/GameMode/bin Tests/GameMode/obj Tests/Affinity/bin Tests/Affinity/obj Tests/Memory/bin Tests/Memory/obj Tests/SettingsUi/bin Tests/SettingsUi/obj Tests/Native/build Tests/Integration/__pycache__
    find Source -type f \( -name '*.user' -o -name '*.suo' \) -print -delete 2>/dev/null || true

rebuild: clean build

# Tail RimWorld log and filter for this mod's messages.
logs:
    @tail -f ~/.config/unity3d/Ludeon\ Studios/RimWorld\ by\ Ludeon\ Studios/Logs/Player.log | grep LinuxMisc

# Sum anonymous huge pages currently attributed to RimWorld.
verify-thp:
    @pid=$(pgrep -xo RimWorld); awk '/^AnonHugePages:/ { sum += $2 } END { printf "AnonHugePages: %d kB (%.1f MiB)\n", sum, sum / 1024 }' "/proc/$pid/smaps"

check-thp-status:
    @cat /sys/kernel/mm/transparent_hugepage/enabled

# Create a source-complete Linux x64 release archive.
release: build
    #!/usr/bin/env bash
    set -euo pipefail
    VERSION=$(git describe --tags --always --dirty)
    ZIP_NAME="LinuxMisc-${VERSION}.zip"
    TEMP_DIR=$(mktemp -d)
    trap 'find "${TEMP_DIR}" -depth -delete 2>/dev/null || true' EXIT
    MOD_DIR="${TEMP_DIR}/LinuxMisc"

    [[ -f Assemblies/RimWorldLinuxMisc.dll ]]
    [[ -f Assemblies/liblinuxmisc_gamemode.so ]]
    [[ $(find Assemblies -maxdepth 1 -type f -name '*.dll' | wc -l) -eq 1 ]]
    ! find Assemblies -maxdepth 1 -type f \( -name '*.deps.json' -o -name '*.runtimeconfig.json' \) | grep -q .
    ! readelf -d Assemblies/liblinuxmisc_gamemode.so | grep -q 'Shared library: \[libgamemode'

    mkdir -p "${MOD_DIR}/Assemblies" "${MOD_DIR}/docs"
    cp -r About "${MOD_DIR}/"
    cp Assemblies/RimWorldLinuxMisc.dll Assemblies/liblinuxmisc_gamemode.so "${MOD_DIR}/Assemblies/"
    cp README.md LICENSE "${MOD_DIR}/"
    cp docs/benchmarking.md "${MOD_DIR}/docs/"
    cp -r Source "${MOD_DIR}/Source"
    cp -r Native "${MOD_DIR}/Native"
    for generated in "${MOD_DIR}/Source/bin" "${MOD_DIR}/Source/obj" "${MOD_DIR}/Native/build"; do
        if [[ -d "${generated}" ]]; then find "${generated}" -depth -delete; fi
    done
    find "${MOD_DIR}" -type f \( -name '*.user' -o -name '*.suo' \) -delete

    rm -f "${ZIP_NAME}"
    (cd "${TEMP_DIR}" && zip -r -q "${ZIP_NAME}" LinuxMisc)
    mv "${TEMP_DIR}/${ZIP_NAME}" .
    python3 Tests/Integration/verify_release.py "${ZIP_NAME}"
    echo "Created ${ZIP_NAME}"
