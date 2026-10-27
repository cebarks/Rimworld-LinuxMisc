using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Xml;
using RimWorldLinuxMisc;
using RimWorldLinuxMisc.NativeInterop;
using Verse;

internal static class Program
{
    private const ulong Page = 4096;
    private static int failures;

    private static int Main()
    {
        Run(nameof(SettingsDefaultsAndLegacyMigration), SettingsDefaultsAndLegacyMigration);
        Run(nameof(MapParserPreservesPathAndClassifiesMappings), MapParserPreservesPathAndClassifiesMappings);
        Run(nameof(AllDisabledMakesNoNativeCallsAndNoTimer), AllDisabledMakesNoNativeCallsAndNoTimer);
        Run(nameof(FeaturesRemainIndependentWhenThpIsNever), FeaturesRemainIndependentWhenThpIsNever);
        Run(nameof(ThpHostModesUseTruthfulPaths), ThpHostModesUseTruthfulPaths);
        Run(nameof(PrefaultBudgetCountsFailedAndPartialAttempts), PrefaultBudgetCountsFailedAndPartialAttempts);
        Run(nameof(PeriodicPassRepeatsEnabledFeaturesWithOneMapsRead), PeriodicPassRepeatsEnabledFeaturesWithOneMapsRead);
        Run(nameof(PeriodicPassIsNonOverlappingAndShutdownCannotRearm), PeriodicPassIsNonOverlappingAndShutdownCannotRearm);
        Run(nameof(PeriodicStatusSnapshotIsConsistent), PeriodicStatusSnapshotIsConsistent);
        Run(nameof(OptionsAreSnapshotted), OptionsAreSnapshotted);
        Run(nameof(StartupOrderIsGamemodeAffinityMemory), StartupOrderIsGamemodeAffinityMemory);

        Console.WriteLine(failures == 0 ? "PASS: 11 memory tests" : $"FAIL: {failures} memory tests");
        return failures == 0 ? 0 : 1;
    }

    private static void SettingsDefaultsAndLegacyMigration()
    {
        LinuxMiscSettings fresh = new LinuxMiscSettings();
        Equal(false, fresh.enableCPUAffinity, "CPU affinity fresh default");
        Equal(true, fresh.enableTHP, "startup THP fresh default");
        Equal(false, fresh.enablePeriodicMadvise, "periodic fresh default");
        Equal(false, fresh.verbosePeriodicLogging, "verbose fresh default");
        Equal(false, fresh.enableMemoryPrefaulting, "prefault fresh default");
        Equal(false, fresh.enableFileIOHints, "file hint fresh default");
        Equal(64, fresh.prefaultBudgetMiB, "budget fresh default");
        Equal(false, fresh.allowUnboundedPrefaulting, "unbounded fresh default");

        LoadSettings(new Dictionary<string, object>
        {
            ["enablePeriodicMadvise"] = false,
            ["enableCPUAffinity"] = true,
            ["enableMemoryPrefaulting"] = true,
            ["enableFileIOHints"] = false
        }, out LinuxMiscSettings migrated);

        Equal(false, migrated.enableTHP, "legacy THP-labeled off choice migrates to startup THP off");
        Equal(false, migrated.enablePeriodicMadvise, "legacy periodic off remains off");
        Equal(true, migrated.enableCPUAffinity, "explicit legacy CPU choice is preserved");
        Equal(true, migrated.enableMemoryPrefaulting, "explicit legacy prefault choice is preserved");
        Equal(false, migrated.enableFileIOHints, "explicit legacy file choice is preserved");
        Equal(64, migrated.prefaultBudgetMiB, "new budget gets conservative default");
        Equal(false, migrated.allowUnboundedPrefaulting, "legacy prefault does not become unbounded");
    }

    private static void MapParserPreservesPathAndClassifiesMappings()
    {
        True(MemoryMapParser.TryParse("7f000000-7f004000 r--p 00000000 08:01 42 /opt/RimWorld Mods/My file.dll", out MemoryMapRegion file), "file map parses");
        Equal("/opt/RimWorld Mods/My file.dll", file.Pathname, "full pathname is retained");
        Equal(MemoryMapKind.File, file.Kind, "absolute path classified as file");

        True(MemoryMapParser.TryParse("1000-9000 rw-p 00000000 00:00 0", out MemoryMapRegion anonymous), "unnamed anonymous parses");
        Equal(MemoryMapKind.Anonymous, anonymous.Kind, "empty path classified anonymous");

        True(MemoryMapParser.TryParse("a000-b000 rw-p 00000000 00:00 0 [anon:GC heap]", out MemoryMapRegion named), "named anonymous parses");
        Equal(MemoryMapKind.Anonymous, named.Kind, "named anon classified anonymous");

        True(MemoryMapParser.TryParse("c000-d000 r-xp 00000000 00:00 0 [vdso]", out MemoryMapRegion special), "special parses");
        Equal(MemoryMapKind.Special, special.Kind, "vdso classified special");
    }

    private static void AllDisabledMakesNoNativeCallsAndNoTimer()
    {
        FakeHost host = new FakeHost { ThpMode = TransparentHugePageMode.Madvise };
        FakeTimerFactory timers = new FakeTimerFactory();
        MemoryOptimizationOptions options = Options();
        using MemoryOptimizationService service = new MemoryOptimizationService(options, host, timers);

        MemoryPassResult result = service.Start();

        Equal(0, host.NativeCalls.Count, "no native calls");
        Equal(0, host.MapReadCount, "maps not read");
        Equal(0, timers.Created.Count, "timer not created");
        Equal(false, result.AnyAttempted, "no attempted optimization");
    }

    private static void FeaturesRemainIndependentWhenThpIsNever()
    {
        FakeHost host = new FakeHost
        {
            ThpMode = TransparentHugePageMode.Never,
            Maps = new[]
            {
                "1000-5000 rw-p 00000000 00:00 0 [heap]",
                "8000-c000 r--p 00000000 08:01 5 /tmp/data file.xml"
            }
        };
        MemoryOptimizationOptions options = Options(enableThp: true, enablePrefault: true, enableFileHints: true, budgetBytes: 2 * Page);
        using MemoryOptimizationService service = new MemoryOptimizationService(options, host, new FakeTimerFactory());

        MemoryPassResult result = service.Start();

        Equal(0, host.NativeCalls.Count(c => c.Advice == MemoryAdvice.HugePage || c.Advice == MemoryAdvice.EnableProcessThp), "THP never issues no THP requests");
        Equal(1, host.NativeCalls.Count(c => c.Advice == MemoryAdvice.PopulateWrite), "prefault remains independent");
        Equal(1, host.NativeCalls.Count(c => c.Advice == MemoryAdvice.Sequential), "file hint remains independent");
        Equal(1, host.MapReadCount, "one maps read/pass");
        Equal(2 * Page, result.Prefault.BytesAttempted, "bounded prefault slice");
    }

    private static void ThpHostModesUseTruthfulPaths()
    {
        FakeHost always = new FakeHost { ThpMode = TransparentHugePageMode.Always };
        using (MemoryOptimizationService service = new MemoryOptimizationService(Options(enableThp: true), always, new FakeTimerFactory())) service.Start();
        Equal(1, always.NativeCalls.Count(c => c.Advice == MemoryAdvice.EnableProcessThp), "always mode uses process request");
        Equal(0, always.MapReadCount, "always success needs no maps");

        FakeHost madvise = new FakeHost { ThpMode = TransparentHugePageMode.Madvise, Maps = new[] { "1000-5000 rw-p 00000000 00:00 0" } };
        using (MemoryOptimizationService service = new MemoryOptimizationService(Options(enableThp: true), madvise, new FakeTimerFactory())) service.Start();
        Equal(1, madvise.NativeCalls.Count(c => c.Advice == MemoryAdvice.HugePage), "madvise mode advises anonymous range");

        FakeHost never = new FakeHost { ThpMode = TransparentHugePageMode.Never, Maps = new[] { "1000-5000 rw-p 00000000 00:00 0" } };
        using (MemoryOptimizationService service = new MemoryOptimizationService(Options(enableThp: true), never, new FakeTimerFactory())) service.Start();
        Equal(0, never.NativeCalls.Count, "never mode makes no THP syscall");
    }

    private static void PrefaultBudgetCountsFailedAndPartialAttempts()
    {
        FakeHost host = new FakeHost
        {
            ThpMode = TransparentHugePageMode.Unavailable,
            Maps = new[]
            {
                "1000-4000 rw-p 00000000 00:00 0",
                "8000-d000 rw-p 00000000 00:00 0",
                "e000-f000 r--p 00000000 08:01 7 /tmp/still-runs.xml"
            },
            FailAdvice = MemoryAdvice.PopulateWrite
        };
        MemoryOptimizationOptions options = Options(enablePrefault: true, enableFileHints: true, budgetBytes: 5 * Page);
        using MemoryOptimizationService service = new MemoryOptimizationService(options, host, new FakeTimerFactory());

        MemoryPassResult result = service.Start();
        NativeCall[] prefaults = host.NativeCalls.Where(c => c.Advice == MemoryAdvice.PopulateWrite).ToArray();

        Equal(2, prefaults.Length, "budget spans failed calls");
        Equal(3 * Page, prefaults[0].Length, "first range attempted in full");
        Equal(2 * Page, prefaults[1].Length, "second range sliced to remaining cap");
        Equal(5 * Page, result.Prefault.BytesAttempted, "failures still consume attempted-byte budget");
        Equal(0, result.Prefault.SuccessfulCalls, "failures reported accurately");
        Equal((int?)12, result.Prefault.LastErrno, "structured result retains the last native errno");
        Equal(1, result.FileHints.SuccessfulCalls, "a failing prefault feature does not block file advice");

        FakeHost unboundedHost = new FakeHost { Maps = host.Maps, ThpMode = TransparentHugePageMode.Unavailable };
        using MemoryOptimizationService unbounded = new MemoryOptimizationService(Options(enablePrefault: true, allowUnbounded: true, budgetBytes: Page), unboundedHost, new FakeTimerFactory());
        unbounded.Start();
        Equal(8 * Page, unboundedHost.NativeCalls.Where(c => c.Advice == MemoryAdvice.PopulateWrite).Aggregate(0UL, (sum, call) => sum + call.Length), "separate unbounded option ignores cap");
    }

    private static void PeriodicPassRepeatsEnabledFeaturesWithOneMapsRead()
    {
        FakeHost host = new FakeHost
        {
            ThpMode = TransparentHugePageMode.Madvise,
            Maps = new[]
            {
                "1000-3000 rw-p 00000000 00:00 0",
                "4000-6000 r--p 00000000 08:01 9 /tmp/content.xml"
            }
        };
        FakeTimerFactory timers = new FakeTimerFactory();
        using MemoryOptimizationService service = new MemoryOptimizationService(
            Options(enablePeriodic: true, enablePrefault: true, enableFileHints: true, budgetBytes: Page),
            host,
            timers);

        service.Start();
        Equal(1, host.MapReadCount, "startup prefault/file share one maps read");
        FakeTimer timer = Single(timers.Created);
        timer.Fire();
        Equal(2, host.MapReadCount, "periodic THP/prefault/file share one additional maps read");
        Equal(1, service.CompletedPeriodicPasses, "periodic result published");
        Equal(1, timer.RearmCount, "one-shot timer rearmed after completion");
        Equal(1, service.LatestPeriodicResult.TransparentHugePages.SuccessfulCalls, "periodic THP result");
        Equal(1, service.LatestPeriodicResult.Prefault.SuccessfulCalls, "periodic prefault result");
        Equal(1, service.LatestPeriodicResult.FileHints.SuccessfulCalls, "periodic file result");
    }

    private static void PeriodicPassIsNonOverlappingAndShutdownCannotRearm()
    {
        FakeHost host = new FakeHost
        {
            ThpMode = TransparentHugePageMode.Madvise,
            Maps = new[] { "1000-2000 rw-p 00000000 00:00 0" },
            BlockAdvice = MemoryAdvice.HugePage
        };
        FakeTimerFactory timers = new FakeTimerFactory();
        MemoryOptimizationOptions options = Options(enablePeriodic: true, intervalMs: 1234);
        MemoryOptimizationService service = new MemoryOptimizationService(options, host, timers);
        service.Start();
        FakeTimer timer = Single(timers.Created);

        Thread first = new Thread(timer.Fire);
        first.Start();
        True(host.AdviceEntered.Wait(2000), "first callback entered advice");
        timer.Fire();
        Equal(1, host.NativeCalls.Count(c => c.Advice == MemoryAdvice.HugePage), "overlapping callback skipped");

        service.Shutdown();
        host.ReleaseAdvice.Set();
        True(first.Join(2000), "callback exits");
        Equal(0, timer.RearmCount, "callback cannot rearm after shutdown");
        Equal(1, timer.DisposeCount, "timer disposed exactly once");
        timer.Fire();
        Equal(1, host.NativeCalls.Count(c => c.Advice == MemoryAdvice.HugePage), "shutdown callback is inert");
        service.Dispose();
    }

    private static void PeriodicStatusSnapshotIsConsistent()
    {
        FakeTimerFactory timers = new FakeTimerFactory();
        using MemoryOptimizationService service = new MemoryOptimizationService(
            Options(enablePeriodic: true, enablePrefault: true), new SequencedHost(), timers);
        service.Start();
        var before = service.LatestPeriodicSnapshot;
        Equal(0L, before.Sequence, "no periodic result before first callback");
        FakeTimer timer = Single(timers.Created);
        Thread worker = new Thread(() =>
        {
            for (int i = 0; i < 1000; i++)
            {
                timer.Fire();
                Thread.Yield();
            }
        });
        worker.Start();
        do
        {
            var snapshot = service.LatestPeriodicSnapshot;
            if (snapshot.Sequence > 0)
                Equal(((ulong)snapshot.Sequence + 1) * Page, snapshot.Result.Prefault.BytesAttempted, "published sequence and result must come from the same pass");
        } while (!worker.Join(0));
        Equal(1000L, service.LatestPeriodicSnapshot.Sequence, "all periodic passes published");
        Equal(0L, before.Sequence, "previous snapshot remains immutable");
        Equal(0UL, before.Result.Prefault.BytesAttempted, "previous result remains immutable");
    }

    private static void OptionsAreSnapshotted()
    {
        LinuxMiscSettings settings = new LinuxMiscSettings
        {
            enableTHP = true,
            enablePeriodicMadvise = true,
            periodicMadviseInterval = 3210,
            enableMemoryPrefaulting = true,
            prefaultBudgetMiB = 7
        };
        MemoryOptimizationOptions options = MemoryOptimizationOptions.FromSettings(settings);
        settings.enableTHP = false;
        settings.enablePeriodicMadvise = false;
        settings.periodicMadviseInterval = 1;
        settings.prefaultBudgetMiB = 99;

        Equal(true, options.EnableStartupThp, "startup option snapshot");
        Equal(true, options.EnablePeriodicMadvise, "periodic option snapshot");
        Equal(3210, options.PeriodicIntervalMilliseconds, "interval snapshot");
        Equal(7UL * 1024 * 1024, options.PrefaultBudgetBytes, "budget snapshot");
    }

    private static void StartupOrderIsGamemodeAffinityMemory()
    {
        List<string> calls = new List<string>();
        OptimizationStartupResult result = OptimizationStartup.Initialize(
            () => { calls.Add("gamemode"); return true; },
            active => { calls.Add("affinity:" + active); return true; },
            () => { calls.Add("memory"); return true; });

        Equal("gamemode,affinity:True,memory", string.Join(',', calls), "initializer order and GameMode state forwarding");
        Equal(true, result.GamemodeEnabled, "gamemode result");
        Equal(true, result.CpuAffinityEnabled, "affinity result");
        Equal(true, result.MemoryEnabled, "memory result");
    }

    private static MemoryOptimizationOptions Options(bool enableThp = false, bool enablePeriodic = false, bool enablePrefault = false, bool enableFileHints = false, bool allowUnbounded = false, ulong budgetBytes = 64UL * 1024 * 1024, int intervalMs = 90000)
        => new MemoryOptimizationOptions(enableThp, enablePeriodic, intervalMs, false, enablePrefault, enableFileHints, budgetBytes, allowUnbounded);

    private static void LoadSettings(Dictionary<string, object> values, out LinuxMiscSettings settings)
    {
        Scribe_Values.Values.Clear();
        foreach (KeyValuePair<string, object> pair in values) Scribe_Values.Values[pair.Key] = pair.Value;
        XmlDocument document = new XmlDocument();
        XmlElement root = document.CreateElement("settings");
        document.AppendChild(root);
        foreach (string key in values.Keys) root.AppendChild(document.CreateElement(key));
        Scribe.loader.curXmlParent = root;
        Scribe.mode = LoadSaveMode.LoadingVars;
        settings = new LinuxMiscSettings();
        settings.ExposeData();
        Scribe.mode = LoadSaveMode.Inactive;
    }

    private static T Single<T>(IReadOnlyList<T> items)
    {
        Equal(1, items.Count, "single item expected");
        return items[0];
    }

    private static void Run(string name, Action test)
    {
        try { test(); Console.WriteLine("PASS " + name); }
        catch (Exception exception) { failures++; Console.Error.WriteLine("FAIL " + name + ": " + exception.Message); }
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
    }
}

internal sealed class FakeHost : IMemoryOptimizationHost
{
    public TransparentHugePageMode ThpMode = TransparentHugePageMode.Unavailable;
    public IReadOnlyList<string> Maps = Array.Empty<string>();
    public MemoryAdvice? FailAdvice;
    public MemoryAdvice? BlockAdvice;
    public int MapReadCount;
    public readonly List<NativeCall> NativeCalls = new List<NativeCall>();
    public readonly ManualResetEventSlim AdviceEntered = new ManualResetEventSlim(false);
    public readonly ManualResetEventSlim ReleaseAdvice = new ManualResetEventSlim(false);

    public bool IsLinux => true;
    public TransparentHugePageMode ReadTransparentHugePageMode() => ThpMode;
    public IReadOnlyList<string> ReadProcessMaps() { MapReadCount++; return Maps; }

    public bool TryApply(MemoryAdvice advice, ulong address, ulong length, out int errno)
    {
        lock (NativeCalls) NativeCalls.Add(new NativeCall(advice, address, length));
        if (BlockAdvice == advice)
        {
            AdviceEntered.Set();
            ReleaseAdvice.Wait(2000);
        }
        errno = FailAdvice == advice ? 12 : 0;
        return FailAdvice != advice;
    }
}

internal sealed class SequencedHost : IMemoryOptimizationHost
{
    private int pass;
    public bool IsLinux => true;
    public TransparentHugePageMode ReadTransparentHugePageMode() => TransparentHugePageMode.Madvise;
    public IReadOnlyList<string> ReadProcessMaps()
    {
        int pages = Interlocked.Increment(ref pass);
        return new[] { $"1000-{(0x1000UL + (ulong)pages * 4096):x} rw-p 00000000 00:00 0" };
    }
    public bool TryApply(MemoryAdvice advice, ulong address, ulong length, out int errno)
    {
        errno = 0;
        return true;
    }
}

internal readonly record struct NativeCall(MemoryAdvice Advice, ulong Address, ulong Length);

internal sealed class FakeTimerFactory : IMemoryTimerFactory
{
    public readonly List<FakeTimer> Created = new List<FakeTimer>();
    public IMemoryTimer Create(Action callback)
    {
        FakeTimer timer = new FakeTimer(callback);
        Created.Add(timer);
        return timer;
    }
}

internal sealed class FakeTimer : IMemoryTimer
{
    private readonly Action callback;
    public int ArmCount;
    public int RearmCount;
    public int DisposeCount;
    public bool Disposed;

    public FakeTimer(Action callback) { this.callback = callback; }
    public void Arm(int dueTimeMilliseconds)
    {
        if (ArmCount > 0) RearmCount++;
        ArmCount++;
    }
    public void Fire() => callback();
    public void Dispose() { if (!Disposed) { Disposed = true; DisposeCount++; } }
}
