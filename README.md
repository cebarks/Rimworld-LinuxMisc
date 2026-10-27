# RimWorld Linux Misc

Best-effort Linux integration for RimWorld 1.7. The mod can request Transparent
Huge Pages (THP), apply optional memory advice, reconcile per-thread CPU
affinity, and activate Feral Interactive's GameMode client. These controls are
not a promise of a particular TPS improvement; measure them on your own save
and system.

## Support and safety boundaries

- **Game version:** RimWorld 1.7 only.
- **Native package:** Linux x86-64. Other operating systems and architectures
  load the managed mod but skip unsupported native features.
- Settings are sampled during startup and changes require a game restart.
- Requests and advice can succeed without producing a measurable performance
  improvement.
- The mod does not change global THP policy, sysctls, caches, or cgroups.

For reliable whole-process CPU placement, prefer an external launcher,
`taskset`, or a systemd/cgroup policy. The in-mod affinity option is a
best-effort policy over the thread IDs it can observe. It reconciles newly
observed threads every 30 seconds, preserves narrower existing thread masks,
and cannot guarantee that every current or future game thread is pinned.

## Fresh-install defaults

| Control | Fresh default | Meaning |
| --- | --- | --- |
| CPU affinity | Off | Optional best-effort per-thread affinity reconciliation |
| Startup THP advice | On | One startup pass requests THP for eligible mappings |
| Periodic memory advice | Off | No background memory scan timer |
| Memory prefaulting | Off | No page-population requests |
| File I/O hints | Off | No advice for eligible file-backed mappings |
| GameMode integration | On | Request activation when the packaged shim and a system GameMode host are available |

Existing saved settings are preserved and can differ from these fresh defaults.
In particular, a previously disabled THP setting remains disabled after the
settings migration. Prefaulting is bounded by default (64 MiB attempted per
pass); unbounded prefaulting requires a separate explicit opt-in. The budget
counts attempted bytes, including failed advice, and is not a wall-time or
physical-memory guarantee.

## GameMode integration

The release includes `Assemblies/liblinuxmisc_gamemode.so`, a small C wrapper
around Feral Interactive's supported `gamemode_client.h`. The wrapper loads the
system GameMode client dynamically, so the mod has **no hard
`libgamemode.so` dependency**. Missing required shim/host functionality or a
failed daemon request is reported and skipped without preventing RimWorld
startup. Older hosts without the optional status query can still activate and
deactivate GameMode; an explicit status query fails safely when unsupported.

Install GameMode from your distribution if you want this feature:

```text
Fedora/RHEL:   sudo dnf install gamemode
Ubuntu/Debian: sudo apt install gamemode
Arch Linux:   sudo pacman -S gamemode
```

A successful GameMode activation means the daemon accepted this client's
request. It does not prove that GameMode pinned CPUs; that depends on the
system's GameMode configuration. By default the mod conservatively defers its
optional affinity policy after successful GameMode activation unless the user
explicitly enables coexistence.

The shim is resolved beside `RimWorldLinuxMisc.dll`, not from RimWorld's current
working directory. Linux architectures other than x86-64 skip GameMode until a
matching native artifact is provided.

## Feature notes

### CPU affinity

The opt-in policy works with actual allowed CPU IDs and per-thread masks. The
"reserved cores" value excludes logical CPUs from the mod's candidate set; it
does not reserve physical cores for the operating system. Hybrid-core
preference uses trustworthy topology information when available and falls back
conservatively rather than inferring P/E cores from frequency alone.

Use this to inspect a process-level summary, while remembering individual
threads may intentionally have narrower masks:

```bash
taskset -cp "$(pgrep -xo RimWorld)"
```

### Transparent Huge Pages

THP advice only asks the kernel to consider eligible anonymous mappings. Actual
huge-page backing depends on kernel policy, mapping eligibility, allocation,
and memory pressure.

Sum the amount currently reported for the process:

```bash
pid=$(pgrep -xo RimWorld)
awk '/^AnonHugePages:/ { sum += $2 } END { printf "AnonHugePages: %d kB (%.1f MiB)\n", sum, sum / 1024 }' "/proc/$pid/smaps"
```

This must sum `AnonHugePages`, not mapping `Size`.

### Periodic advice, prefaulting, and file hints

These are independent opt-ins. Leaving all of them disabled creates no periodic
memory timer and performs no corresponding native advice calls. Prefaulting can
increase startup work and memory pressure; begin with the bounded mode and
benchmark a representative save. File hints and successful `madvise` returns
are requests accepted by the kernel, not proof of faster loading.

## Installation

Extract a release into RimWorld's `Mods/` directory. A complete Linux x86-64
installation contains at least:

```text
LinuxMisc/About/About.xml
LinuxMisc/Assemblies/RimWorldLinuxMisc.dll
LinuxMisc/Assemblies/liblinuxmisc_gamemode.so
```

The managed mod can still build and load on non-Linux development systems; the
native GameMode artifact is produced only on a supported Linux x86-64 build
host.

## Building and testing

Requirements:

- .NET SDK 10
- `make` and a C11 compiler for the Linux x86-64 shim
- `just`
- `binutils`, `zip`, and Python 3 for release verification/packaging and integration tests

```bash
just build       # Managed build; also builds/copies the shim on Linux x86-64
just test        # Native fake hosts plus managed and optimization harnesses
just clean
just release     # Verified source-complete release archive

# Direct managed build (same Linux x86-64 native target is invoked by MSBuild)
dotnet build Source/RimWorldLinuxMisc.csproj -c Release
```

Native tests use controlled fake `libgamemode` hosts and never send requests to
a locally installed GameMode daemon. Release archives contain the mod DLL,
packaged shim, wrapper source and ABI header, the exact vendored upstream
header, provenance, and both project/upstream licenses. Test binaries and
`bin`/`obj` directories are excluded.

See [docs/benchmarking.md](docs/benchmarking.md) for a repeatable comparison
protocol.

## License

The mod is AGPL-3.0; see [LICENSE](LICENSE). The vendored Feral GameMode client
header retains its BSD 3-Clause license at
`Native/vendor/gamemode/LICENSE.txt`.
