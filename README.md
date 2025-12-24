# RimWorld Linux Misc

Performance optimizations for RimWorld on Linux.

## What It Does

Five Linux-specific performance optimizations:

1. **CPU Affinity**: Pins the game to specific CPU cores to improve cache locality and reduce context switches. Automatically detects hybrid CPUs (Intel 12th gen+) and prefers performance cores over efficiency cores.
2. **Transparent Huge Pages (THP)**: Uses 2MB memory pages instead of standard 4KB pages, reducing Translation Lookaside Buffer (TLB) misses for better memory performance.
3. **Memory Prefaulting**: Pre-faults memory pages to reduce runtime stuttering from page faults. Requires Linux kernel 5.14+.
4. **File I/O Hints**: Applies kernel hints (`MADV_SEQUENTIAL`, `MADV_WILLNEED`) to file-backed memory for faster save/load operations.
5. **GameMode Integration**: Activates Feral Interactive's gamemode for system-wide performance optimizations.

## Installation

### Steam Workshop

This mod is available on the [Steam Workshop](https://steamcommunity.com/app/294100/workshop/).

**Workshop Item**: [Link will be added after first publish]

Releases are automatically published to Steam Workshop when new versions are released (requires manual 2FA approval via Steam Mobile app on first run).

### Manual Installation

Download the latest release from the [GitHub Releases](https://github.com/cebarks/rimworld-linux-misc/releases) page and extract to your RimWorld `Mods/` directory.

## Requirements

**Platform**: Linux only. (The mod detects the platform at runtime and does nothing on Windows/macOS.)

**CPU Affinity**:
- Works on all Linux systems
- Automatically detects CPU topology (hybrid vs non-hybrid)
- No installation required

**Transparent Huge Pages (THP)**:
- Linux kernel with `CONFIG_TRANSPARENT_HUGEPAGE` enabled (standard on all modern distributions)

**Memory Prefaulting**:
- Linux kernel 5.14+ required
- Feature is automatically skipped on older kernels

**File I/O Hints**:
- Works on all Linux kernels
- No installation required

**GameMode**: [Feral Interactive's GameMode](https://github.com/FeralInteractive/gamemode)
- Install the `gamemode` package from your distribution's repositories
  - Fedora/RHEL: `sudo dnf install gamemode`
  - Ubuntu/Debian: `sudo apt install gamemode`
  - Arch: `sudo pacman -S gamemode`
- **Optional**: Can be disabled in mod settings if not installed



## Verification

### **Verify THP is working (while game is running):**
```bash
cat /proc/$(pgrep RimWorldLinux)/smaps | grep AnonHugePages | grep -v "0 kB"
```
Or if you want to see the total amount of allocated THP RAM:
```bash
cat /proc/$(pgrep RimWorldLinux)/smaps | grep "Size:" | awk '{sum += $2} END {print "Total huge pages: " sum " kB (" sum/1024 " MB)"}'
```
If you see non-zero values, THP is working.

### **Verify CPU affinity:**
```bash
taskset -cp $(pgrep RimWorldLinux)
```
Shows which CPU cores the game is pinned to.

**Note:** Memory prefaulting and file I/O hints log their status at startup but don't have simple verification commands. Check the log output.

## Configuration

Access settings via RimWorld's **Options → Mod Settings → Linux Performance Optimizations**:

**CPU Affinity:**
- **Enable CPU affinity optimization**: Toggle on/off (default: enabled)
- **Reserved cores for OS**: 0-4 cores (default: 1) - cores to leave for system processes
- **Prefer performance cores (hybrid CPUs)**: Toggle on/off (default: enabled) - pin to P-cores on Intel 12th gen+ CPUs
- **Apply CPU affinity with gamemode (Advanced)**: Override gamemode's CPU management (default: disabled) - may conflict

**Memory Optimization:**
- **Enable Transparent Huge Pages**: Toggle on/off (default: enabled)
- **Periodic Interval**: 0-300 seconds (default: 30) - re-apply optimizations periodically, 0 for startup-only
- **Verbose periodic logging**: Toggle on/off (default: enabled) - log each periodic execution
- **Enable memory prefaulting**: Toggle on/off (default: enabled) - requires kernel 5.14+
- **Enable file I/O hints**: Toggle on/off (default: enabled)

**GameMode:**
- **Enable gamemode integration**: Toggle on/off (default: enabled) - requires `gamemode` package installed

## Feature Interactions

### CPU Affinity & GameMode Overlap

Both **CPU Affinity** and **GameMode** can manage CPU scheduling, which may cause conflicts if both are active:

**What GameMode Does:**
- Depending on gamemode config it:
  - sets CPU governor to "performance" mode
  - May apply its own CPU affinity policies
  - Adjusts process priority and I/O scheduling
  - Enables GPU performance modes

**What This Mod's CPU Affinity Does:**
- Directly pins RimWorld to specific CPU cores using `sched_setaffinity()`
- Prefers performance cores on hybrid CPUs (Intel 12th gen+)
- Reserves cores for OS background tasks (rimworld is mostly single threaded, so reserving cores for OS/background tasks is negligible)

**Default Behavior (Safe):**
When both features are enabled, **CPU affinity automatically defers to gamemode** to prevent conflicts. The mod logs: `"Skipping CPU affinity - gamemode will handle CPU management"`.

**Recommendations:**

Choose the approach that fits your use case:

- **GameMode alone (recommended for most users)**: System-wide optimizations that work across all games. Good general-purpose performance boost.

- **Both (advanced)**: Enable the "Apply CPU affinity with gamemode" setting to force both features active. This may work if your gamemode configuration doesn't conflict, but can cause both features to fight over CPU scheduling. Only use if you understand the tradeoffs. This mod's settings should always take prescendence over gamemode's.

**Troubleshooting:**
If you experience CPU scheduling issues (unexpected core usage, performance problems), try disabling one of these features and restarting RimWorld.

## Building from Source

### Requirements
- .NET SDK 6.0 or later
- RimWorld assembly references (automatically provided via [RimRef NuGet package](https://www.nuget.org/packages/Krafs.Rimworld.Ref/))

### Build Commands

```bash
just build    # Recommended
# OR
cd Source && dotnet build -c Release
```

**Output:** `Assemblies/RimWorldLinuxMisc.dll`

**First build:** The build will automatically download RimRef assemblies from NuGet (~4MB download).

### Creating a Release

To create a distributable release zip:

```bash
just release
```

This will:
1. Build the mod in Release configuration
2. Get the version from git tags (`git describe --tags`) or use the most recent commit hash as a version instead.
3. Create a versioned zip file: `LinuxMisc-{VERSION}.zip`
4. Include: `About/`, `Assemblies/`, `README.md`, and `Source/` (excluding build artifacts)

The zip file can be uploaded to GitHub releases or shared directly with users.

## Platform Support

- **Linux**: Full support (requires kernel THP support, available in all modern kernels)
- **Windows/macOS**: Gracefully does nothing (no errors, no crashes)

## License

AGPL-3.0 - See [LICENSE](LICENSE) for details.
