# RimWorld Linux Misc

Performance optimizations for RimWorld on Linux.

## What It Does

Two Linux-specific performance optimizations:

1. **Transparent Huge Pages (THP)**: A Linux kernel feature that uses 2MB memory pages instead of standard 4KB pages, reducing Translation Lookaside Buffer (TLB) misses.
   - This may improve memory performance on large colonies. (If you notice a significant performance uplift, please reach out to me so I can use your colony as a benchmark.)
2. **GameMode Integration**: Activates Feral Interactive's gamemode.

## Requirements

**Platform**: Linux only. (The mod detects the platform at runtime and does nothing on Windows/macOS.)

**Transparent Huge Pages (THP)**:
- Linux kernel with `CONFIG_TRANSPARENT_HUGEPAGE` enabled (standard on all modern distributions)

**GameMode**: [Feral Interactive's GameMode](https://github.com/FeralInteractive/gamemode)
- Install the `gamemode` package from your distribution's repositories
  - Fedora/RHEL: `sudo dnf install gamemode`
  - Ubuntu/Debian: `sudo apt install gamemode`
  - Arch: `sudo pacman -S gamemode`
- **Optional**: Can be disabled in mod settings if not installed



## Verification

### Verify THP is working (system-level)

You can check that huge pages are being used after loading a save by running this:

```bash
cat /proc/$(pgrep RimWorldLinux)/smaps | grep AnonHugePages | grep -v "0 kB"
```

If you see non-zero values, THP is working.

## Configuration

Access settings via RimWorld's **Options → Mod Settings → Linux Misc**:

- **THP Re-application Interval**: 0-300 seconds (default: 30). Set to 0 for startup-only application.
- **GameMode Integration**: Toggle on/off (default: enabled). Requires `gamemode` package installed.

## Building from Source

#### Requires .NET SDK and RimWorld installed at the default Steam location.

```bash
cd Source
dotnet build -c Release
```

OR, more simply:
```bash
just build
```

Output: `Assemblies/RimWorldLinuxMisc.dll`

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
