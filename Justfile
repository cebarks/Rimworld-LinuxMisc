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

# Clean build artifacts
clean:
    cd Source && dotnet clean
    rm -f Assemblies/*.dll

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
