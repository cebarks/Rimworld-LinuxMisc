# Safe benchmarking protocol

This protocol compares behavior on one machine and one representative save. It
does not establish a universal performance claim.

## Safety rules

- Do not drop system caches, change sysctls, change global THP policy, or run an
  OOM/unbounded-prefault probe.
- Do not benchmark while saves, package updates, indexing, or thermal throttling
  differ materially between runs.
- Keep the same RimWorld version, mod list/order, graphics options, launch
  method, map, camera position, storyteller state, and game speed.
- Back up the save. Use a copied save if the benchmark allows game time to
  advance.
- Restart RimWorld after every settings change. Settings are startup snapshots.
- Existing saved mod settings may differ from fresh-install defaults; record
  every effective setting rather than assuming defaults.

## Comparison matrix

Run at least these configurations:

1. **Baseline:** mod disabled.
2. **No-op control:** mod enabled, with CPU affinity, startup THP, periodic
   advice, prefaulting, file hints, and GameMode all disabled.
3. **Startup THP only.**
4. **GameMode only** (if the system host and daemon are installed).
5. **CPU affinity only** (optional; external placement is preferred for a
   reliable whole-process experiment).
6. **Periodic memory advice only** (optional).
7. **Bounded prefaulting only** (optional, start with the 64 MiB budget).
8. **File I/O hints only** (optional).
9. A combined configuration only after the individual comparisons are known.

Do not use unbounded prefaulting for routine in-game benchmarking. A successful
GameMode request is its own condition; it is not evidence that CPU pinning was
applied.

## Run procedure

1. Reboot only if that is part of normal usage; otherwise keep system uptime
   policy consistent. Never alter global caches between runs.
2. Record kernel, CPU, memory, GameMode, and mod build information:

   ```bash
   uname -a
   lscpu
   free -h
   gamemoded --version 2>/dev/null || true
   sha256sum Assemblies/RimWorldLinuxMisc.dll Assemblies/liblinuxmisc_gamemode.so
   ```

3. Select one save and a fixed camera position/game speed. Record the mod list
   and settings.
4. Launch RimWorld using the same command and environment for every run.
5. Load the save, wait a fixed warm-up period (for example five minutes of
   simulation time), then collect a fixed observation window (for example ten
   minutes). Choose durations before seeing results.
6. Repeat each configuration at least three times and rotate configuration
   order to reduce temperature/time-order bias. More repetitions are better
   than a single long run.
7. Report every run, including failed or interrupted runs and the reason for
   exclusion. Compare medians and variability; do not report only the best run.

## Lightweight measurements

Identify the process once per run:

```bash
pid=$(pgrep -xo RimWorld)
```

### CPU and memory samples

These commands are read-only:

```bash
ps -p "$pid" -o pid,etimes,%cpu,%mem,rss,vsz,nlwp,stat,cmd
awk '/^(voluntary_ctxt_switches|nonvoluntary_ctxt_switches):/ { print }' "/proc/$pid/status"
awk '/^(Rss|Pss|Private_Clean|Private_Dirty|Private_Hugetlb|Anonymous|AnonHugePages):/ { totals[$1] += $2 }
     END { for (key in totals) print key, totals[key], "kB" }' "/proc/$pid/smaps_rollup"
```

For periodic samples without installing extra tools:

```bash
for sample in $(seq 1 20); do
  date --iso-8601=seconds
  ps -p "$pid" -o etimes=,%cpu=,rss=,vsz=,nlwp=
  sleep 30
done
```

### Transparent Huge Pages

Sum `AnonHugePages` across mappings; mapping `Size` is not huge-page usage:

```bash
awk '/^AnonHugePages:/ { sum += $2 }
     END { printf "AnonHugePages: %d kB (%.1f MiB)\n", sum, sum / 1024 }' \
    "/proc/$pid/smaps"
```

A non-zero total confirms some anonymous memory is huge-page backed. It does
not by itself prove a simulation benefit.

### CPU affinity

Process summaries do not show every per-thread mask. Capture all observed
threads when evaluating the mod's best-effort policy:

```bash
for task in /proc/"$pid"/task/*; do
  tid=${task##*/}
  taskset -cp "$tid"
done | sort
```

The mod reconciles newly observed threads every 30 seconds, so sample after at
least one interval. Threads can disappear during enumeration, and narrower
pre-existing masks are intentionally preserved. For deterministic whole-game
placement, benchmark an external `taskset`, launcher, systemd scope, or cgroup
policy instead of interpreting the mod as a guarantee.

### GameMode

Record the mod log message and the system's GameMode configuration. Activation
only means the daemon accepted this client's request. Check affinity separately
if CPU placement is part of the hypothesis. Do not infer pinning from an active
GameMode status.

## Results table

Keep the raw samples and summarize without a promised TPS range:

| Build/config | Run | Warm-up | Window | Simulation metric | CPU median | RSS median | AnonHugePages | Notes |
| --- | ---: | ---: | ---: | --- | ---: | ---: | ---: | --- |
| baseline | 1 | 5 min | 10 min | user-recorded | | | | |
| no-op | 1 | 5 min | 10 min | user-recorded | | | | |
| THP only | 1 | 5 min | 10 min | user-recorded | | | | |

Use the same in-game simulation metric source for every run and describe how it
was collected. Treat accepted kernel advice and daemon requests as mechanism
checks, not performance results.
