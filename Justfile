# Justfile for RimWorld Linux Misc
# Common development tasks for the mod

# List available recipes
default:
    @just --list

# Build aliases
alias b := build

# Build the mod in Release configuration
build:
    cd Source && dotnet build -c Release

# Clean build artifacts and generated files
clean:
    cd Source && dotnet clean
    rm -fv Assemblies/*.dll Assemblies/*.pdb
    rm -fv LinuxMisc-*.zip
    find Source -type f \( -name '*.user' -o -name '*.suo' \) -print -delete 2>/dev/null || true

# Clean and rebuild
rebuild: clean build

# Tail RimWorld log and filter for this mod's messages
logs:
    @tail -f ~/.config/unity3d/Ludeon\ Studios/RimWorld\ by\ Ludeon\ Studios/Player.log | grep LinuxMisc

# Check if THP is working for running RimWorld process
verify-thp:
    @echo "Checking for Transparent Huge Pages in RimWorld process..."
    @cat /proc/$(pgrep RimWorldLinux)/smaps | grep AnonHugePages | grep -v "0 kB" || echo "No huge pages found (RimWorld may not be running or THP not enabled)"

# Show system THP configuration
check-thp-status:
    @echo "System THP status:"
    @cat /sys/kernel/mm/transparent_hugepage/enabled

# Create a release zip with versioned filename
release: build
    #!/usr/bin/env bash
    set -euo pipefail

    # Get version from git tags
    VERSION=$(git describe --tags --always --dirty)
    ZIP_NAME="LinuxMisc-${VERSION}.zip"
    TEMP_DIR=$(mktemp -d)
    MOD_DIR="${TEMP_DIR}/LinuxMisc"

    echo "Creating release ${ZIP_NAME}..."

    # Create directory structure
    mkdir -p "${MOD_DIR}"

    # Copy required directories
    cp -r About "${MOD_DIR}/"
    cp -r Assemblies "${MOD_DIR}/"
    cp README.md "${MOD_DIR}/"

    # Copy Source/ excluding build artifacts
    cp -r Source/ "${MOD_DIR}/Source/"
    rm -rf "${MOD_DIR}/Source/bin" "${MOD_DIR}/Source/obj"
    find "${MOD_DIR}/Source/" -type f \( -name '*.user' -o -name '*.suo' \) -delete

    # Create zip (remove old one if exists)
    rm -f "${ZIP_NAME}"
    (cd "${TEMP_DIR}" && zip -r -q "${ZIP_NAME}" LinuxMisc)
    mv "${TEMP_DIR}/${ZIP_NAME}" .

    # Cleanup
    rm -rf "${TEMP_DIR}"

    echo "✓ Created ${ZIP_NAME}"
